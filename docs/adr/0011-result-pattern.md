# 11. Result Pattern: Beklenen Hatalarda Exception Değil

**Status:** Accepted
**Date:** 2026-09-07

## Context

Bir command/query handler'ı çalışırken iki tür "başarısızlık" olabilir:
beklenmeyen sistem hataları (veritabanı bağlantısı koptu, disk doldu) ve
beklenen iş hataları (kullanıcı yetersiz bakiyeyle işlem yapmaya
çalıştı, email zaten kayıtlı). İkincisi normal akışın bir parçasıdır,
exception fırlatmak hem performans maliyeti taşır hem de "bu bir hata mı
yoksa normal bir iş sonucu mu" ayrımını kod okuyucusundan gizler.

## Decision

Beklenen iş hataları için `Result<T>` tipi kullanılır (başarı/
başarısızlık + hata detayı taşıyan bir dönüş tipi); exception yalnızca
gerçekten istisnai, beklenmeyen durumlar için ayrılır. `Result<T>`'in
hata tipi, API katmanında HTTP status kodlarına (örn. validation hatası
→ 400, yetki hatası → 403, bulunamadı → 404) temiz bir şekilde eşlenir.

## Consequences

### Positive
- Bir handler'ın imzasına bakarak "bu işlem başarısız olabilir mi"
  sorusu direkt cevaplanır — `Result<T>` dönen bir metod, çağıranı hata
  durumunu ele almaya zorlar (tip sistemi üzerinden), exception'lar gibi
  sessizce yukarı fırlatılıp unutulamaz.
- Exception fırlatma/yakalama maliyeti (stack unwinding) sadece
  gerçekten istisnai durumlarda ödenir, normal iş akışının bir parçası
  olan "yetersiz bakiye" gibi durumlarda değil.

### Trade-offs
- Handler zincirlerinde `Result<T>` sonuçlarını birbirine bağlamak
  (örn. bir adım başarısızsa sonrakini çalıştırmamak) için yardımcı
  metodlar (map/bind gibi) gerekir; bu, exception'ların doğal "otomatik
  yukarı fırlama" davranışına kıyasla biraz daha fazla elle kod ister.

## Alternatives Considered

### Her şey için exception
.NET'te en tanıdık yaklaşım, ama beklenen iş hatalarını da exception
olarak modellemek, "bu try/catch bir hatayı mı yoksa normal bir iş
kuralını mı yakalıyor" ayrımını kod okuyucusuna bırakır ve API
katmanında her exception tipini elle bir HTTP status koduna çevirmek
için merkezi bir exception-handling middleware'i gerektirir — bu da
hatanın nerede üretildiği ile nerede HTTP'ye çevrildiği arasına bir
dolaylama katmanı ekler.
