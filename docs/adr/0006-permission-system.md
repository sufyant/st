# 6. Permission Sistemi: Native Policy-Based Authorization

**Status:** Accepted
**Date:** 2026-09-07

## Context

Tenant içinde rol bazlı yetkilendirme (RBAC) gerekiyor — örn. "admin"
tüm işlemleri yapabilir, "member" sadece okuma/sınırlı yazma yapabilir.
Bu kontrolün nerede ve nasıl uygulanacağına karar verilmeli.

## Decision

ASP.NET Core'un native claims/policy-based authorization mekanizması
kullanılır. `admin` schema'sındaki Role→Permission eşleşmesi, tenant
çözümleme sırasında kullanıcının `ClaimsPrincipal`'ına permission
claim'leri olarak yazılır. Command/query pipeline'ında bir
`[RequiresPermission("...")]` attribute'u/pipeline behavior'ı bu
claim'leri kontrol eder. İstek işleme sırası: Auth (Clerk JWT doğrulama)
→ Tenant çözümleme ([ADR 0004](0004-path-based-tenant-resolution.md)) →
Membership kontrolü ([ADR 0005](0005-membership-and-invitation.md)) →
Permission kontrolü → Validation (FluentValidation) → Handler →
SaveChanges (Unit of Work,
[ADR 0010](0010-hand-rolled-mediator-pipeline.md)).

## Consequences

### Positive
- ASP.NET Core'un yerleşik authorization altyapısı kullanıldığı için ek
  bir framework/kütüphane bağımlılığı yok.
- Sıralama net ve tek bir yerde (middleware/pipeline zinciri) tanımlı:
  bir isteğin permission kontrolüne ulaşabilmesi için önce authenticated,
  sonra geçerli bir tenant üyesi olması garanti.
- Permission tanımları (Role→Permission eşlemesi) veritabanında olduğu
  için kod değişikliği/deploy gerektirmeden güncellenebilir.

### Trade-offs
- Claim üretimi (tenant çözümleme adımında) ile claim tüketimi
  (handler'daki `[RequiresPermission]`) farklı katmanlarda olduğu için,
  yeni bir permission eklerken hem `admin` schema'daki tanımın hem de
  handler'daki attribute'un tutarlı olması geliştiricinin sorumluluğunda.

## Alternatives Considered

### Her handler içinde elle if/else permission kontrolü
En esnek ama en kolay unutulan yaklaşım — yeni bir handler eklerken
permission kontrolünü eklemeyi unutmak, sessizce güvenlik açığı yaratır.
Pipeline behavior yaklaşımı, kontrolü "varsayılan olarak açık" değil
"varsayılan olarak kapalı, attribute ile açılan" bir modele çevirir.
