using Api.Tests.Shared;
using Xunit;

namespace Api.Tests.Integration;

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>;
