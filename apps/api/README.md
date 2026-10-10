# API

The API of the multi-tenant SaaS starter template: a .NET modular monolith on PostgreSQL. One build output runs as a `web` process, which serves HTTP, and a `worker` process, which handles messages. Tenant data is isolated by PostgreSQL row level security.

- [docs/local-run.md](docs/local-run.md): run the API on a local machine, from an empty database to an accepted invitation.
- [docs/configuration.md](docs/configuration.md): every setting, whether it is required, and its default.
- [docs/setup.md](docs/setup.md): what the API needs from its database and identity providers.
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): the architectural rules. They are authoritative.

Build and test from this folder:

```sh
dotnet build Api.slnx
dotnet test --solution Api.slnx   # needs Docker: the tests run on PostgreSQL in a container
```
