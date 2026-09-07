# 2. Control Plane: Paylaşımlı `admin` Schema

**Status:** Accepted
**Date:** 2026-09-07

## Context

Schema-per-tenant modelinde ([ADR 0001](0001-tenant-isolation-schema-per-tenant.md))
her tenant kendi izole şemasında yaşıyor, ama tenant'ların kendisi, kim
hangi tenant'a üye, davetler gibi "tenant'lar hakkındaki" veri hiçbir
tenant şemasına ait değil — bu veri tüm sistemin ortak, paylaşımlı
state'i.

## Decision

Ayrı bir `admin` schema'sı tanımlanır; tenant kaydı (`Tenants`), kullanıcı
(`Users` — tenant'lar arası kullanıcı profili, Clerk kullanıcı id'siyle
anahtarlanır ve [ADR 0016](0016-utc-timezone-policy.md)'ya göre
kullanıcının IANA timezone'unu da tutar), üyelik (`Memberships`), davet
(`Invitations`) ve rol→izin eşlemesi (`RolePermissions`) tabloları
burada tutulur. Tüm tenant şemalarından bağımsız, tek bir kopya olarak
var olur ve tenant çözümleme
([ADR 0004](0004-path-based-tenant-resolution.md)) ile membership
kontrolü ([ADR 0005](0005-membership-and-invitation.md)) ve permission
kontrolü ([ADR 0006](0006-permission-system.md)) bu şemaya sorgu atarak
çalışır.

## Consequences

### Positive
- "Hangi tenant'lar var, kim hangisine üye" sorusu tek bir yerden,
  tutarlı şekilde cevaplanır — N tane tenant şemasını gezmeye gerek yok.
- Yeni bir tenant oluşturma işlemi net bir sınır kazanır: `admin.tenants`'a
  satır ekle + yeni Postgres schema'sı oluştur + migration uygula.

### Trade-offs
- `admin` schema'sı sistemin tek "merkezi" parçası olduğu için, bu
  şemaya yazan kod yollarının (tenant oluşturma, davet kabul etme)
  doğruluğu kritik önem taşır; buradaki bir hata tüm tenant'ları
  etkileyebilir.

## Alternatives Considered

### Tenant kaydını her tenant şemasında kendi kendine tutmak
Merkezi bir kayıt olmadan, sistemin "kaç tenant var" sorusuna cevap
vermesi bile Postgres'in `pg_catalog`'unu şema isimlerine göre parse
etmeyi gerektirirdi — kırılgan ve okunabilirlikten uzak bir çözüm.
Control plane / tenant data ayrımı, çok-kiracılı sistemlerde standart bir
desendir; burada yeniden icat etmenin bir faydası yok.
