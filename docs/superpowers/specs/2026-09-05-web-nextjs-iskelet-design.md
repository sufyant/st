# apps/web Next.js iskeleti ve monorepo kokü

Tarih: 2026-09-05
Durum: onaylandi

## Amac

`apps/web` altinda calisan bir Next.js uygulamasi ve onu barindiran pnpm +
Turborepo kokü kurmak. Bu, uc adimli bir isin ilk adimi:

1. Bu spec: workspace kokü + `apps/web` iskeleti
2. Sonraki: shadcn, `packages/ui` altinda paylasilan paket olarak
3. Sonraki: Clerk

Ikinci ve ucuncu adimlar bu spec'in kapsami disinda. Burada sadece calisan bir
iskelet ve onun uzerine bina edilecek kok yapisi var.

## Kapsam

### Kokte olusacak dosyalar

```
package.json          private, packageManager pnpm@10.30.3, komutlar turbo uzerinden
pnpm-workspace.yaml   apps/*, packages/* + ignoredBuiltDependencies
turbo.json            dev, build, lint, check-types
biome.json            tek konfig, next + react domain'leri acik
tsconfig.base.json    ek strict bayraklar
.gitignore            mevcut dosyaya .turbo/ eklenir
```

### apps/web

`create-next-app@16.3.4` ciktisi, su bayraklarla:

```
--ts --tailwind --biome --app --src-dir --import-alias "@/*"
--use-pnpm --skip-install --disable-git
```

Gelen surumler: Next 16.3.4, React 19.2.8, Tailwind 4, TypeScript 5.
Paket adi `@st/web` olarak degistirilir.

## Kararlar ve gerekceleri

### Paket adi `@st/web`

`@st/` kapsami bu repoda daha once kullanilmis bir kural (`@st/web`,
`@st/tokens`). Uc faydasi var: npm'deki gercek paketlerle cakismaz,
`"@st/ui": "workspace:*"` satiri kendini anlatir, `pnpm --filter @st/*` tum
workspace'i tek ifadeyle hedefler. `.claude/launch.json` zaten bu ismi bekliyor,
ama bu ismin gerekcesi degil sonucudur.

### Lint ve format: Biome

ESLint yerine Biome secildi. Biome 2.5.12 semasinda `next` adinda bir kural
alani var ve `eslint-config-next` kapsaminin neredeyse tamamini karsiliyor:
`noImgElement`, `noHeadElement`, `noHeadImportInDocument`,
`noDocumentImportInPage`, `noSyncScripts`, `noUnwantedPolyfillio`,
`useGoogleFontDisplay`, `useGoogleFontPreconnect`, `useHookAtTopLevel`,
`useExhaustiveDependencies`. Karsiligini bulamadigimiz tek kural
`no-async-client-component`.

Kazanc: lint ve format tek arac, uc Next uygulamasi icin kokte tek konfig,
Rust ile yazildigi icin CI'da belirgin hiz farki. Kayip: dar eklenti
ekosistemi, tam tip-farkinda lint yok, o tek Next kurali. shadcn ve Clerk
kurulumlarinin ikisi de lint aracina bagimli degil.

### Turborepo baştan giriyor

Tek uygulama varken cache, siralama ve degisiklige gore filtreleme faydalarinin
ucu de bosta duruyor. Ikinci ve ucuncu Next uygulamasi `packages/ui`'ye
baglandiginda deger aciliyor: `ui` icindeki tek satir degisikligi Turbo yokken
uc uygulamayi da bastan build ettirir. Sonradan gecis yapmamak icin simdi
giriyor.

## Komutlar

Turbo gorev adlari paket script adlariyla birebir eslesmek zorunda. Sablon
`apps/web`'e `dev`, `build`, `start`, `lint`, `format` yaziyor; `check-types`
eklenir:

```
apps/web:  dev, build, start, lint, format, check-types
kok:       dev, build, lint, format, check-types  (hepsi turbo uzerinden)
```

`check-types` icerigi `next typegen && tsc --noEmit`. `next typegen`, Next'in
urettigi tipler olmadan `tsc`'nin yanlis hata vermesini onler.

## Sablon ciktisina yapilacak dort duzeltme

`create-next-app` tek uygulama varsayarak yaziyor. Monorepo'ya oturtmak icin:

1. **Biome konfigi koke tasinir.** Sablon `apps/web/biome.json` yaziyor.
   Icerik koke alinir, app icindeki silinir. Uc Next uygulamasi ayni kurallari
   paylassin diye.
2. **pnpm workspace dosyasi birlesir.** Sablon `apps/web/pnpm-workspace.yaml`
   yaziyor, icinde `sharp` ve `unrs-resolver` icin `ignoredBuiltDependencies`
   var. Icerik kokteki dosyaya tasinir, app'teki silinir. Tek lock dosyasi
   kokte olur.
3. **Biome surumu yukseltilir.** Sablon `2.4.2` sabitliyor, guncel surum
   `2.5.12`.
4. **tsconfig ikiye ayrilir.** Kokteki `tsconfig.base.json` ek sikiligi tasir
   (`noUncheckedIndexedAccess`), `apps/web/tsconfig.json` onu genisletip Next'in
   urettigi her seyi korur. Next `dev` sirasinda bu dosyaya dokunuyor, o yuzden
   urettikleri silinmez.

## Silinmeyen sablon dosyalari

`apps/web/AGENTS.md` ve `apps/web/CLAUDE.md` sablonla geliyor.

`AGENTS.md` icerigi her `next dev` calismasinda
`node_modules/next/dist/server/lib/generate-agent-files.js` tarafindan yeniden
yaziliyor. Silinirse tekrar beliriyor, commit edilmezse calisma agaci surekli
kirli kaliyor. Icerigi ajanlara "bu senin bildigin Next degil,
`node_modules/next/dist/docs/` altindaki dokumani oku" diyen bir uyari.

`CLAUDE.md` tek satir, `@AGENTS.md` importundan ibaret.

Ikisi de commit edilir.

## Kapsam disi

- **`.env.example` yazilmaz.** Su an icine koyacak tek degisken yok. Clerk
  adiminda ilk anahtar gelince olusturulur.
- **React Compiler kapali.** `--react-compiler` bayragi mevcut ama build
  suresine karsilik olculecek bir performans sorunu yok.
- **pnpm catalog kullanilmaz.** Ikinci uygulama gelince surum hizalamasi icin
  degerli olur.
- **i18n, Dockerfile, CI, hata sayfalari yok.** Eski `apps/web` kurulumunda
  vardi, bu adimda kapsam disi.

## Dogrulama olcutleri

| Adim | Kontrol |
|---|---|
| Kurulum | Kokte `pnpm install` biter, lock dosyasi tek ve kokte |
| Build | `pnpm turbo build` hatasiz |
| Lint | `pnpm turbo lint` temiz |
| Tip | `pnpm turbo check-types` temiz |
| Dev | `pnpm --filter @st/web dev` ile sayfa 3000'de acilir, tarayicidan dogrulanir |
| Temizlik | `next dev` calistiktan sonra `git status` temiz |
