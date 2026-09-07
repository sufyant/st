# 26. Logging: Serilog + Seq, Otomatik Tenant/Request/User Enrichment

**Status:** Accepted
**Date:** 2026-09-07

## Context

Multi-tenant bir sistemde bir hata/log satırının hangi tenant'ta, hangi
istekte, hangi kullanıcı tarafından tetiklendiğini bilmeden debug etmek
neredeyse imkansız. Solo/küçük bir ekip için ayrıca maliyetli bir
gözlemlenebilirlik altyapısı (örn. Datadog, tam ölçekli ELK stack)
kurmak bugünün ölçeğine göre fazla.

## Decision

Serilog (yapılandırılmış loglama kütüphanesi) + Seq (log görüntüleme/
arama arayüzü, solo/küçük ekip için ücretsiz tier) kullanılır. Her log
satırı, merkezi pipeline'da
([ADR 0010](0010-hand-rolled-mediator-pipeline.md)'daki pipeline
behavior zincirinin bir parçası olarak) otomatik olarak tenant_id,
request_id ve user_id ile enrich edilir — her log çağrısında bu
alanları elle eklemeye gerek yok.

## Consequences

### Positive
- Yapılandırılmış (structured) loglama, log satırlarının sadece
  okunabilir metin değil, sorgulanabilir alanlar (tenant_id, request_id,
  user_id) taşıması anlamına gelir — Seq'te "bu tenant'ın son 1 saatteki
  tüm hataları" gibi bir sorgu, metin arama yerine alan bazlı
  filtreleme ile yapılabilir.
- Otomatik enrichment, her log çağrısında
  `logger.LogError("...", tenantId, requestId, userId)` gibi tekrar
  eden, unutulmaya açık bir kalıp yazma ihtiyacını ortadan kaldırır —
  enrichment bir kere, merkezi pipeline'da tanımlanır.
- Seq'in ücretsiz tier'ı solo/küçük ekibin bugünkü ihtiyacına yetiyor;
  ekip büyürse Grafana+Loki gibi daha ölçeklenebilir bir alternatife
  geçiş, Serilog'un sink (çıktı hedefi) soyutlaması sayesinde loglama
  kodunun kendisini değiştirmeden yapılabilir.

### Trade-offs
- Seq, tek bir process/instance olarak çalıştığı sürece kendisi bir
  "tek nokta" haline gelir (log toplama arayüzü); bu, bugünkü ölçek için
  kabul edilebilir, ama ekip/trafiğin büyümesi durumunda not edilen
  Grafana+Loki geçişiyle ele alınacak bir sınırlama.

## Alternatives Considered

### Datadog veya benzeri tam ölçekli SaaS gözlemlenebilirlik platformu
Çok daha kapsamlı (APM, metrics, tracing dahil) ama solo/küçük ekip için
hem maliyet hem de kurulum karmaşıklığı bugünkü ihtiyacın çok üzerinde;
Seq'in ücretsiz tier'ı log arama/filtreleme ihtiyacını zaten
karşılıyor.
