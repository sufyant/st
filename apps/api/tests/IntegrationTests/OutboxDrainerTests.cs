using Domain.ControlPlane.Tenants;
using Infrastructure.Messaging;
using Application.Features.Provisioning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

public sealed class OutboxDrainerTests
{
    [Fact]
    public async Task DrainAsync_ProcessesAPendingMessageAndStampsIt()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await EnqueueAsync(fixture, tenant.Id.Value);
        await using var drainer = CreateDrainer(fixture);

        // Act
        var processed = await drainer.DrainAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, processed);
        await using var context = fixture.CreateControlPlane();
        var message = await context.OutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(message.ProcessedAt);
        Assert.Equal(TenantStatus.Active, (await fixture.ReloadAsync(tenant.Id)).Status);
    }

    [Fact]
    public async Task DrainAsync_WhenTheHandlerFails_RecordsTheAttemptAndKeepsTheMessage()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        await EnqueueAsync(fixture, Guid.CreateVersion7());
        await using var drainer = CreateDrainer(fixture);

        // Act
        var processed = await drainer.DrainAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, processed);
        await using var context = fixture.CreateControlPlane();
        var message = await context.OutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(message.ProcessedAt);
        Assert.Equal(1, message.AttemptCount);
        Assert.NotNull(message.LastError);
        Assert.True(message.NextAttemptAt > message.CreatedAt);
    }

    [Fact]
    public async Task DrainAsync_SkipsMessagesThatExhaustedTheirAttempts()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await EnqueueAsync(fixture, tenant.Id.Value);

        await using (var context = fixture.CreateControlPlane())
        {
            var message = await context.OutboxMessages.SingleAsync(TestContext.Current.CancellationToken);

            for (var attempt = 0; attempt < OutboxMessage.MaximumAttempts; attempt++)
            {
                message.RecordFailure("boom", DateTimeOffset.UtcNow.AddHours(-1));
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var drainer = CreateDrainer(fixture);

        // Act
        var processed = await drainer.DrainAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, processed);
        Assert.Equal(TenantStatus.Provisioning, (await fixture.ReloadAsync(tenant.Id)).Status);
    }

    [Fact]
    public async Task DrainAsync_SkipsMessagesThatAreNotDueYet()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await EnqueueAsync(fixture, tenant.Id.Value);

        await using (var context = fixture.CreateControlPlane())
        {
            var message = await context.OutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
            message.RecordFailure("boom", DateTimeOffset.UtcNow.AddMinutes(10));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var drainer = CreateDrainer(fixture);

        // Act
        var processed = await drainer.DrainAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, processed);
    }

    [Fact]
    public async Task DrainAsync_RunConcurrently_ProcessesTheMessageOnce()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();
        var tenant = await fixture.AddProvisioningTenantAsync("acme");
        await EnqueueAsync(fixture, tenant.Id.Value);
        await using var first = CreateDrainer(fixture);
        await using var second = CreateDrainer(fixture);

        // Act
        var results = await Task.WhenAll(
            first.DrainAsync(TestContext.Current.CancellationToken),
            second.DrainAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(1, results.Sum());
    }

    [Fact]
    public async Task DrainAsync_ForAnUnknownMessageType_RecordsTheFailureAndKeepsTheMessage()
    {
        // Arrange
        await using var fixture = await ProvisioningFixture.StartAsync();

        await using (var context = fixture.CreateControlPlane())
        {
            context.OutboxMessages.Add(OutboxMessage.Create(
                Guid.CreateVersion7(),
                "SomethingNobodyHandles",
                "{}",
                DateTimeOffset.UtcNow));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var drainer = CreateDrainer(fixture);

        // Act
        var processed = await drainer.DrainAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, processed);
        await using var assertions = fixture.CreateControlPlane();
        var message = await assertions.OutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(message.ProcessedAt);
        Assert.Equal(1, message.AttemptCount);
        Assert.Contains("SomethingNobodyHandles", message.LastError);
    }

    private static OutboxDrainer CreateDrainer(ProvisioningFixture fixture) =>
        new(
            fixture.CreateControlPlane(),
            [
                new TenantProvisioningHandler(
                    fixture.CreateControlPlane(),
                    fixture.Provisioner,
                    TimeProvider.System)
            ],
            TimeProvider.System);

    private static async Task EnqueueAsync(ProvisioningFixture fixture, Guid tenantId)
    {
        await using var context = fixture.CreateControlPlane();
        context.OutboxMessages.Add(OutboxMessage.Create(
            Guid.CreateVersion7(),
            TenantProvisioningRequested.MessageType,
            $$"""{"TenantId":"{{tenantId}}","OwnerExternalUserId":"{{ProvisioningFixture.OwnerExternalUserId}}"}""",
            DateTimeOffset.UtcNow));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
