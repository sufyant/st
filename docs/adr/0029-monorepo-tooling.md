# 29. Monorepo Tooling: Turborepo + .NET İçin İnce Wrapper

**Status:** Accepted
**Date:** 2026-09-07

## Context

Monorepo, pnpm + Turborepo ile TS tarafını (`apps/web`, `apps/mobile`,
`apps/admin`, `packages/*`) yönetiyor. `apps/api` .NET projesi olduğu
için Turborepo'nun native anladığı `package.json`/`node_modules`
bağımlılık grafiğinin dışında kalıyor; ama geliştiricinin tek bir
komutla (`pnpm dev`) hem frontend hem backend'i ayağa kaldırabilmesi
isteniyor.

## Decision

Turborepo, TS tarafını (web/mobile/admin/packages) yönetmeye devam
eder. `apps/api` içine, `dotnet run`/`dotnet watch` gibi komutları saran
ince bir `package.json` konur (gerçek bir Node.js bağımlılığı yok,
sadece Turborepo'nun script çalıştırma arayüzüne uyum sağlayan bir
kabuk). Böylece `pnpm dev`, Turborepo'nun görev grafiği üzerinden hem TS
uygulamalarını hem .NET API'yi birlikte ayağa kaldırır.

## Consequences

### Positive
- Geliştirici deneyimi tek bir komutla (`pnpm dev`) korunur; .NET
  projesi için ayrı bir terminal sekmesi açıp `dotnet run` çalıştırmak
  gerekmez.
- Turborepo'nun cache/paralel çalıştırma altyapısı hâlâ TS tarafı için
  tam olarak çalışır; .NET tarafı sadece "bu script'i çalıştır"
  seviyesinde katılıyor, Turborepo'nun cache mekanizmasına .NET
  build'ini dahil etmeye çalışmıyoruz (bu, .NET'in kendi build/cache
  araçlarıyla çelişebilirdi).

### Trade-offs
- `apps/api/package.json`, gerçek bir Node.js paketi değil, sadece bir
  script wrapper'ı olduğu için, bu klasöre bakan birinin "neden burada
  package.json var" sorusu sorması doğal — bu, ADR'nin kendisiyle (bu
  dosya) cevaplanıyor.

## Alternatives Considered

### .NET için ayrı bir başlatma script'i (Turborepo dışında)
Turborepo'nun görev grafiğinden tamamen bağımsız bir shell script (örn.
`./start-all.sh`) de aynı işlevi görebilirdi, ama bu, geliştiricinin
"frontend'i Turborepo ile, backend'i ayrı bir script ile başlatıyorum"
şeklinde iki farklı zihinsel model taşımasını gerektirirdi. İnce
`package.json` wrapper'ı, .NET'i de Turborepo'nun tek, tutarlı
`pnpm dev` arayüzüne dahil ediyor.
