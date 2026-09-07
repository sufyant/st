# 1. Tenant İzolasyonu: Schema-per-Tenant

**Status:** Accepted
**Date:** 2026-09-07

## Context

Multi-tenant starter kit'te her tenant'ın verisi diğerlerinden güvenli
şekilde izole edilmeli. Üç klasik yaklaşım var: (a) tek şema + tenant_id
kolonu (row-level isolation), (b) tek Postgres instance içinde tenant
başına ayrı şema (schema-per-tenant), (c) tenant başına ayrı veritabanı
(DB-per-tenant). Referans alınan bir şirket mimarisi DB-per-tenant
kullanıyor; ancak bu proje henüz ürünü belirlenmemiş bir starter kit,
solo/küçük ekip tarafından işletilecek ve tenant sayısının başlangıçta
düşük-orta ölçekte kalması bekleniyor.

## Decision

Schema-per-tenant kullanılacak: tek Postgres instance, her tenant kendi
Postgres schema'sına sahip (örn. `tenant_acme`, `tenant_globex`).
Paylaşımlı control-plane verisi ([ADR 0002](0002-control-plane-admin-schema.md))
`admin` schema'sında tutulacak.

## Consequences

### Positive
- Row-level `tenant_id`'ye göre çok daha güçlü izolasyon: yanlış yazılmış
  bir WHERE koşulu tenant sınırını aşamaz, çünkü sınır şema düzeyinde.
- DB-per-tenant'a kıyasla operasyonel maliyet çok düşük: tek connection
  pool, tek instance, tek backup rejimi.
- PostgreSQL `search_path` mekanizmasıyla runtime'da schema seçimi native
  ve ucuz.

### Trade-offs
- Migration'lar her tenant schema'sına ayrı ayrı uygulanmalı
  ([ADR 0014](0014-schema-per-tenant-migrations.md)) — DB-per-tenant kadar
  izole değil ama tek DB kadar da basit değil.
- Tenant sayısı çok büyürse (binlerce), tek Postgres instance'ının schema
  sayısı pratik bir tavan oluşturabilir; bu senaryo için DB-per-tenant'a
  geçiş yolu kasıtlı olarak açık bırakıldı (control-plane zaten `admin`
  schema'sında ayrı tutulduğu için, bir tenant'ı farklı bir Postgres
  instance'ına taşımak mimariyi yeniden yazmayı gerektirmez).

## Alternatives Considered

### Row-level `tenant_id`
En düşük operasyonel maliyet, ama izolasyon garantisi tamamen uygulama
kodunun disiplinine bağlı — her sorguda `tenant_id` filtresi unutulursa
veri sızıntısı olur. Ousterhout'un "A Philosophy of Software Design"
kitabındaki "derin modül" fikriyle çelişir: izolasyon her sorgu yazan
geliştiricinin hafızasına bırakılan sığ bir garanti olurdu. Bu, güvenlik
kritik bir katmanda kabul edilebilir bir cognitive load değil.

### DB-per-tenant
Referans şirket mimarisi bunu kullanıyor ve en güçlü izolasyonu sağlıyor.
Ancak her yeni tenant için ayrı bir veritabanı provision etmek, ayrı
connection pool yönetmek, ayrı backup/restore prosedürü işletmek — bu
projenin mevcut ölçeği (henüz tek ürün belirlenmemiş bir starter kit)
için gereksiz operasyonel karmaşıklık ekliyor. Ousterhout'un ifadesiyle
bu, "complexity is anything related to the structure of a software
system that makes it hard to understand and modify" tanımına giren bir
maliyet — bugünün ihtiyacını çözmek için yarının ölçek problemini
bugünden satın almak. The Pragmatic Programmer'ın "don't over-engineer,
but leave room to grow" ilkesiyle uyumlu olarak, DB-per-tenant'a geçiş
yolu mimaride kapalı tutulmuyor; `admin` schema'sının tenant kaydı zaten
hangi tenant'ın hangi bağlantı dizesini kullandığını taşıyacak şekilde
genişletilebilir ([ADR 0002](0002-control-plane-admin-schema.md)).
Mythical Man-Month'un hatırlattığı gibi, "ikinci sistem etkisi"ne düşüp
gelecekte belki gerekecek bir ölçek için bugünden aşırı mühendislik
yapmak, asıl teslim edilmesi gereken ürünü geciktirir.
