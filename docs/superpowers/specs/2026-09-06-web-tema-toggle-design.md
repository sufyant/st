# apps/web icin light/dark/system tema toggle'i

Tarih: 2026-09-06
Durum: onaylandi

## Amac

`apps/web` dashboard'una shadcn'in resmi `next-themes` yaklasimiyla
Light/Dark/System tema secimi eklemek. Renk token'lari
(`packages/ui/src/styles/globals.css`) zaten `.dark` selector'iyla tanimli;
eksik olan tek sey hangi class'in `<html>`'e uygulanacagina karar veren
runtime mekanizma ve kullanicinin bunu secebilecegi bir arayuz.

## Kapsam

### Yeni bagimlilik

`next-themes` → `apps/web` dependency'si. `packages/ui`'a girmiyor (bkz.
Kararlar).

### Yeni dosya: `apps/web/src/components/theme-provider.tsx`

`next-themes`'in `ThemeProvider`'ini saran ince bir client component:
`attribute="class"`, `defaultTheme="system"`, `enableSystem`,
`disableTransitionOnChange`.

### Degisen: `apps/web/src/app/layout.tsx`

- `<html>`'e `suppressHydrationWarning` eklenir (next-themes'in tema class'ini
  ilk render'dan once enjekte eden inline script'i yuzunden React'in
  hydration uyarisini bastirmak icin gerekli, resmi next-themes kurulumunun
  bir parcasi).
- `ThemeProvider`, `ClerkProvider`'i sarar (sira onemli degil, ikisi
  birbirinden bagimsiz; okunabilirlik icin disaridan ice `ThemeProvider >
  ClerkProvider` sirasi kullanilacak).

### Degisen: `apps/web/src/components/nav-user.tsx`

Mevcut dropdown'daki `Account / Billing / Notifications` grubunun yanina
yeni bir grup: `DropdownMenuSub` ile acilan "Theme" alt-menusu, icinde
`DropdownMenuRadioGroup` + uc `DropdownMenuRadioItem` (Light/Sun,
Dark/Moon, System/Monitor ikonlariyla). Deger `next-themes`'in `useTheme()`
hook'undan okunur ve yazilir (`theme`, `setTheme`).

Bu, `packages/ui/src/components/ui/dropdown-menu.tsx`'de zaten var olan
`DropdownMenuSub`, `DropdownMenuSubTrigger`, `DropdownMenuSubContent`,
`DropdownMenuRadioGroup`, `DropdownMenuRadioItem` primitiflerini kullanir —
`packages/ui`'da yeni component gerekmiyor.

## Kararlar ve gerekceleri

### `next-themes` ve provider `apps/web`'de, `packages/ui`'da degil

`apps/admin`, `apps/marketing`, `apps/mobile` henuz bos klasorler, hic
scaffold edilmemis. Su an tek tuketici `apps/web`. `ThemeProvider`
config'i (`defaultTheme`, `attribute`) zaten app'in root layout'unun
karari — `ClerkProvider`'in da ayni dosyada olmasi gibi. `packages/ui`
su an sadece stateless/generic primitifler + paylasilan `globals.css`
token'lari tasiyor; `next-themes` bagimliligini oraya eklemek bugun hicbir
tuketiciye hizmet etmeyen bir soyutlama olur. Ikinci gercek Next app
geldiginde (somut kopyalanma sinyali cikinca) `packages/ui`'a tasima ucuz
bir refactor.

### Toggle konumu: `NavUser` dropdown'i icinde submenu

sidebar-08 pattern'inde header sadece breadcrumb + `SidebarTrigger`
tasiyor, sade kalmasi hedefleniyor. Kullanici tercihleri
(Account/Billing/Notifications) zaten `NavUser` dropdown'inda toplu; tema
secimi de mantiksal olarak oraya ait bir hesap tercihi. Header'a ayri ikon
buton veya sidebar footer'a bagimsiz buton alternatifleri degerlendirildi,
NavUser submenu'su tercih edildi.

### Uc secenekli (Light/Dark/System), iki secenekli degil

shadcn'in resmi `next-themes` ornegi bu sekilde. Kullanicinin isletim
sistemi temasini otomatik takip etme secenegini (System) kaybetmemek icin
iki secenekli basit toggle yerine radio group ile uc secenek sunuluyor.

### Flash of unstyled theme onlemi eklenmiyor

`next-themes` kendi inline script'ini `<head>`'e enjekte ederek ilk
boyamadan once dogru class'i uyguladigi icin ekstra kod gerekmiyor; sadece
`suppressHydrationWarning` yeterli.

## Kapsam disi

- Clerk'in kendi UI'i (`@clerk/ui/themes` ile gelen `shadcn` teması,
  `apps/web/src/app/layout.tsx`'teki `ClerkProvider`) bu degisiklikte
  dinamik light/dark'a baglanmiyor; Clerk sayfalari (sign-in) kapsam disi.
- `packages/ui`'a tema soyutlamasi eklenmiyor (bkz. Kararlar).
- Sistem temasi disinda ekstra tema varyanti (orn. "high contrast") yok.

## Dogrulama olcutleri

| Adim | Kontrol |
|---|---|
| Kurulum | `pnpm --filter @st/web add next-themes` sonrasi `pnpm install` temiz |
| Tip | `pnpm turbo check-types` hatasiz |
| Dev | `apps/web` dev server'da NavUser > Theme > Light/Dark/System tek tek secilir, sayfa ve tum shadcn bilesenleri (sidebar, dropdown, avatar) dogru renklenir |
| Kalicilik | Secim sonrasi sayfa yenilenince tema korunur (localStorage) |
| Sistem takibi | System secili iken isletim sistemi temasi degisince sayfa otomatik guncellenir |
| Temizlik | `git status` temiz, sadece niyet edilen dosyalar degismis |
