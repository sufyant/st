# st

Multi-tenant uygulama monorepo'su.

## Yapı

```
apps/
  api          .NET 10 Web API (EF Core, PostgreSQL)
  web          Next.js
  marketing    Next.js
  mobile       Expo
packages/
  api-client   OpenAPI'den üretilen TypeScript istemcisi
docs/          Dokümantasyon
scripts/       Yardımcı script'ler
```

## Gereksinimler

- Node.js 24+
- pnpm 11+
- .NET SDK 10
- PostgreSQL

## Kurulum

```bash
pnpm install
```

## Mimari notlar

- **Multi-tenant, catalog pattern.** Tenant kaydı ve bağlantı bilgisi catalog şemasında; her tenant kendi şemasında.
- **Kodda `tenantId` geçmez.** Tenant, request başında middleware'de token'dan çözülür ve bağlantı seviyesinde (`search_path`) bağlanır.
- **API client elle yazılmaz.** .NET tarafında üretilen OpenAPI şemasından otomatik çıkarılır.
