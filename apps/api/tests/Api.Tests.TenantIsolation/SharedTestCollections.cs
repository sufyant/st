using Api.Tests.Shared;
using Xunit;

namespace Api.Tests.TenantIsolation;

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>;
