# 22. Ortak Client: OpenAPI'den Generate Edilen TypeScript Client

**Status:** Accepted
**Date:** 2026-09-07

## Context

web/mobile/admin, aynı .NET backend'ine karşı konuşacak; her istemcinin
API tiplerini elle, ayrı ayrı yazması hem tekrar hem de senkronizasyon
riski (backend bir alanı değiştirdiğinde istemcilerin elle güncellenmesi
gerekir, unutulursa runtime hatası) yaratır.

## Decision

ASP.NET Core'dan OpenAPI şeması generate edilir; bu şemadan
`packages/api-client`'a bir TypeScript client generate edilir. web/
mobile/admin, aynı generated client'ı import eder; elle yazılmış,
backend tipleriyle senkronize tutulması gereken duplicate tip tanımı
yok.

## Consequences

### Positive
- Backend'de bir DTO değiştiğinde (alan eklendi/kaldırıldı), TypeScript
  client'ı yeniden generate etmek yeterli — derleme zamanında
  (TypeScript tip hatası olarak) hangi istemci kodunun güncellenmesi
  gerektiği görünür hale gelir; sessiz runtime senkronizasyon hatası
  riski ortadan kalkar.
- Monorepo'nun `packages/api-client` paketi, Turborepo'nun bağımlılık
  grafiğinde web/mobile/admin'in ortak bağımlılığı olarak modellenir —
  üç ayrı istemcinin üç ayrı, elle senkronize edilen tip seti yerine tek
  bir kaynak.

### Trade-offs
- Generate adımı build/dev pipeline'ının bir parçası olmalı (backend
  değiştiğinde client yeniden üretilmeli); bu adımı atlayan bir
  geliştirici eski, güncel olmayan bir client ile çalışabilir — bu
  riski azaltmak için generate adımı CI'da ve `pnpm dev` akışında
  otomatikleştirilir.

## Alternatives Considered

### Elle yazılmış TypeScript tipleri
En hızlı başlangıç ama backend değiştikçe elle güncellenmesi gereken,
senkronizasyondan kolayca düşen bir duplicate kaynak yaratır — tam
olarak bu ADR'nin çözmeye çalıştığı problem.
