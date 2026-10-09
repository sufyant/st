namespace Api.IntegrationTests;

// Waits for a state a test reads, such as a committed row, rather than for a signal from inside a handler: Wolverine runs handler
// middleware before it commits the handler's transaction and before it ends the handler's span.
internal static class Waiting
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    public static async Task UntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Patience);
        while (!await condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }

    public static Task UntilAsync(Func<bool> condition) => UntilAsync(() => Task.FromResult(condition()));
}
