# apps/web Next.js Iskeleti Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Kokte pnpm workspace ve Turborepo kurup `apps/web` altinda calisan bir Next.js 16 uygulamasi olusturmak.

**Architecture:** Kok bir pnpm workspace; `apps/*` ve `packages/*` altindaki paketleri toplar. Turborepo gorev orkestrasyonunu ve cache'i yonetir, gorev adlari paket script adlariyla birebir eslesir. Biome tek binary olarak koke kurulur ve tum repoyu tek konfigle tarar. `apps/web` `create-next-app` ciktisidir, uzerine monorepo'ya oturmasi icin dort duzeltme yapilir.

**Tech Stack:** pnpm 10.30.3, Turborepo 2.10.12, Next.js 16.3.4, React 19.2.8, Tailwind CSS 4, TypeScript 5, Biome 2.5.12, Node 24.

## Global Constraints

- Paket adi `@st/web`. Kapsam `@st/` bu repoda zorunlu kuraldir.
- Kok `package.json` `private: true` ve `packageManager: "pnpm@10.30.3"` icerir.
- Tek lock dosyasi vardir ve kokte durur. `apps/web` altinda lock dosyasi veya `pnpm-workspace.yaml` bulunmaz.
- Tek Biome konfigi vardir ve kokte durur. `apps/web/biome.json` bulunmaz.
- `@biomejs/biome` yalnizca kok devDependency'sidir, surum `^2.5.12`.
- `apps/web/AGENTS.md` ve `apps/web/CLAUDE.md` silinmez, commit edilir.
- `.env.example` bu planda olusturulmaz.
- React Compiler kapalidir, `--react-compiler` bayragi kullanilmaz.
- Next'in urettigi `apps/web/tsconfig.json` icerigi silinmez, sadece `extends` eklenir.
- Spec: `docs/superpowers/specs/2026-09-05-web-nextjs-iskelet-design.md`

---

### Task 1: Kok workspace iskeleti

Bu gorev pnpm'e "burasi bir workspace" demeyi saglar. Sonrasindaki her gorev kokten `pnpm install` calistirmaya dayanir.

**Files:**
- Create: `package.json`
- Create: `pnpm-workspace.yaml`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: yok, ilk gorev.
- Produces: kok `package.json` (`name: "st"`, `private: true`, `packageManager: "pnpm@10.30.3"`) ve `pnpm-workspace.yaml` (`apps/*`, `packages/*`). Task 2 ve 3 bu iki dosyayi degistirir.

- [ ] **Step 1: Kok package.json'u yaz**

`package.json`:

```json
{
  "name": "st",
  "version": "0.0.0",
  "private": true,
  "packageManager": "pnpm@10.30.3",
  "engines": {
    "node": ">=22"
  }
}
```

`scripts` alani bilerek yok. Komutlar Task 3'te Turborepo ile birlikte eklenir.

- [ ] **Step 2: pnpm-workspace.yaml'i yaz**

`pnpm-workspace.yaml`:

```yaml
packages:
  - apps/*
  - packages/*
```

`apps/admin`, `apps/api`, `apps/marketing`, `apps/mobile`, `packages/ui`, `packages/api-client` klasorleri su an sadece `.gitkeep` iceriyor. Icinde `package.json` olmayan klasoru pnpm gormezden gelir, bu bir hata degildir.

- [ ] **Step 3: .gitignore'a .turbo ekle**

`.gitignore` dosyasinda `# Build output` basliginin altindaki `dist/` satirinin hemen ustune ekle:

```
# Turborepo
.turbo/
```

- [ ] **Step 4: Kurulumu dogrula**

Run: `pnpm install`
Expected: Basarili biter. Cikti "Done" der. Kokte `pnpm-lock.yaml` olusur, `node_modules` olusur. Hicbir paket kurulmadigi icin cikti kisadir.

Run: `pnpm ls -r --depth -1`
Expected: Sadece kok paketi (`st`) listelenir. Baska paket yok, cunku henuz `package.json` iceren alt klasor yok.

- [ ] **Step 5: Commit**

```bash
git add package.json pnpm-workspace.yaml .gitignore pnpm-lock.yaml
git commit -m "pnpm workspace kokunu kur

apps/* ve packages/* altindaki paketleri toplayan kok workspace. Node 24
uzerinde pnpm 10.30.3 sabitlendi. .turbo ciktisi gitignore'a eklendi.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: apps/web'i olustur ve monorepo'ya oturt

`create-next-app` tek uygulama varsayarak yaziyor. Bu gorev ciktiyi uretir ve spec'te belirlenen dort duzeltmeyi uygular.

**Files:**
- Create: `apps/web/` (create-next-app ciktisi)
- Create: `biome.json` (kok, `apps/web/biome.json`'dan tasinir)
- Modify: `apps/web/package.json`
- Modify: `pnpm-workspace.yaml`
- Delete: `apps/web/pnpm-workspace.yaml`
- Delete: `apps/web/biome.json`

**Interfaces:**
- Consumes: Task 1'in kok `package.json` ve `pnpm-workspace.yaml` dosyalari.
- Produces: `@st/web` paketi, scriptleri `dev`, `build`, `start`, `check-types`. Kok `biome.json`, `next` ve `react` domain'leri acik. Task 3 bu script adlarina turbo gorevleriyle baglanir, Task 4 `apps/web/tsconfig.json`'a dokunur.

- [ ] **Step 1: create-next-app'i calistir**

```bash
pnpm dlx create-next-app@16.3.4 apps/web \
  --ts --tailwind --biome --app --src-dir \
  --import-alias "@/*" --use-pnpm --skip-install --disable-git --yes
```

Bayraklarin gerekcesi: `--skip-install` cunku kurulum kokten yapilacak, `--disable-git` cunku zaten bir git deposundayiz.

Expected: "Success! Created apps/web" cikar. Olusan dosyalar:

```
apps/web/.gitignore          apps/web/next.config.ts       apps/web/src/app/globals.css
apps/web/AGENTS.md           apps/web/package.json         apps/web/src/app/layout.tsx
apps/web/CLAUDE.md           apps/web/pnpm-workspace.yaml  apps/web/src/app/page.tsx
apps/web/README.md           apps/web/postcss.config.mjs   apps/web/src/app/favicon.ico
apps/web/biome.json          apps/web/tsconfig.json        apps/web/public/*.svg
apps/web/next-env.d.ts
```

- [ ] **Step 2: Biome konfigini koke tasi ve surumu yukselt**

```bash
mv apps/web/biome.json biome.json
```

Sonra kokteki `biome.json` icinde `$schema` satirini `2.4.2` yerine `2.5.12` yap. Dosyanin son hali:

```json
{
  "$schema": "https://biomejs.dev/schemas/2.5.12/schema.json",
  "vcs": {
    "enabled": true,
    "clientKind": "git",
    "useIgnoreFile": true
  },
  "files": {
    "ignoreUnknown": true,
    "includes": ["**", "!node_modules", "!.next", "!dist", "!build", "!.turbo"]
  },
  "formatter": {
    "enabled": true,
    "indentStyle": "space",
    "indentWidth": 2
  },
  "css": {
    "parser": {
      "tailwindDirectives": true
    }
  },
  "linter": {
    "enabled": true,
    "rules": {
      "recommended": true
    },
    "domains": {
      "next": "recommended",
      "react": "recommended"
    }
  },
  "assist": {
    "actions": {
      "source": {
        "organizeImports": "on"
      }
    }
  }
}
```

Sablondan tek farki `$schema` surumu ve `!.turbo` girdisi.

- [ ] **Step 3: pnpm workspace dosyalarini birlestir**

`apps/web/pnpm-workspace.yaml` icerigi su:

```yaml
ignoredBuiltDependencies:
  - sharp
  - unrs-resolver
```

Bunu kokteki `pnpm-workspace.yaml`'a tasi, dosyanin son hali:

```yaml
packages:
  - apps/*
  - packages/*

ignoredBuiltDependencies:
  - sharp
  - unrs-resolver
```

Sonra app'teki dosyayi sil:

```bash
rm apps/web/pnpm-workspace.yaml
```

- [ ] **Step 4: apps/web/package.json'u duzenle**

Dosyanin son hali:

```json
{
  "name": "@st/web",
  "version": "0.1.0",
  "private": true,
  "scripts": {
    "dev": "next dev",
    "build": "next build",
    "start": "next start",
    "check-types": "next typegen && tsc --noEmit"
  },
  "dependencies": {
    "next": "16.3.4",
    "react": "19.2.8",
    "react-dom": "19.2.8"
  },
  "devDependencies": {
    "@tailwindcss/postcss": "^4",
    "@types/node": "^20",
    "@types/react": "^19",
    "@types/react-dom": "^19",
    "tailwindcss": "^4",
    "typescript": "^5"
  }
}
```

Sablona gore dort degisiklik: `name` artik `@st/web`, `packageManager` alani kaldirildi cunku kokte duruyor, `lint` ve `format` scriptleri kaldirildi ve `@biomejs/biome` devDependency'si silindi cunku Biome kokte tek yerden calisiyor, `check-types` eklendi. `next typegen` olmadan `tsc` Next'in urettigi tipleri bulamayip yanlis hata verir.

- [ ] **Step 5: Biome'u koke ekle**

```bash
pnpm add -D -w @biomejs/biome@^2.5.12
```

`-w` bayragi paketi kok `package.json`'a yazar.

- [ ] **Step 6: Kurulumu calistir ve tek lock dosyasini dogrula**

Run: `pnpm install`
Expected: Basarili biter, `apps/web/node_modules` olusur.

Run: `find . -name "pnpm-lock.yaml" -not -path "*/node_modules/*"`
Expected: Tek satir, `./pnpm-lock.yaml`. `apps/web` altinda lock dosyasi cikarsa Step 1'deki `--skip-install` bayragi atlanmis demektir.

Run: `pnpm ls -r --depth -1`
Expected: Iki paket listelenir, `st` ve `@st/web`.

- [ ] **Step 7: Build ve lint'i dogrula**

Run: `pnpm --filter @st/web build`
Expected: "Compiled successfully" ve route listesi. Hata yok.

Run: `pnpm exec biome check`
Expected: "Checked N files. No fixes applied." Hata ve uyari yok.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "apps/web'i Next.js 16 ile kur

create-next-app ciktisi: TypeScript, App Router, Tailwind 4, src/ dizini,
@/* alias. Paket adi @st/web.

Sablonun tek uygulama varsayan dort ciktisi monorepo'ya oturtuldu: biome.json
koke tasindi ve 2.5.12'ye cekildi, pnpm-workspace.yaml kokteki dosyayla
birlesti, @biomejs/biome kok devDependency'si oldu, check-types scripti eklendi.

AGENTS.md ve CLAUDE.md commit edildi cunku AGENTS.md icerigini her next dev
calismasi yeniden yaziyor; commit edilmezse calisma agaci surekli kirli kalir.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Turborepo ve kok komutlari

Bu gorev tek giris noktasini kurar. Sonrasinda `pnpm build` kokten calisir ve ikinci uygulama geldiginde ek konfig gerekmez.

**Files:**
- Create: `turbo.json`
- Modify: `package.json`

**Interfaces:**
- Consumes: Task 2'nin `@st/web` script adlari (`dev`, `build`, `check-types`).
- Produces: kok scriptleri `dev`, `build`, `check-types`, `lint`, `format`. Task 5 bunlari kullanir.

- [ ] **Step 1: Turborepo'yu koke ekle**

```bash
pnpm add -D -w turbo@^2.10.12
```

- [ ] **Step 2: turbo.json'u yaz**

`turbo.json`:

```json
{
  "$schema": "https://turborepo.com/schema.json",
  "tasks": {
    "build": {
      "dependsOn": ["^build"],
      "outputs": [".next/**", "!.next/cache/**"]
    },
    "check-types": {
      "dependsOn": ["^build"]
    },
    "dev": {
      "cache": false,
      "persistent": true
    }
  }
}
```

Alanlarin anlami: `dependsOn: ["^build"]` bir paketin gorevini baslatmadan once bagimli oldugu paketlerin `build` gorevini calistirir, `packages/ui` dogdugunda dogru sirayi bu saglar. `outputs` cache'lenecek dosyalari soyler, `.next/cache` disarida cunku o zaten Next'in kendi cache'i. `dev` icin `cache: false` ve `persistent: true` cunku surekli calisan bir surec.

`lint` bilerek turbo gorevi degil. Biome kokte tek gecisde tum repoyu tariyor, paket basina gorev tanimlamak her pakete Biome bagimliligi eklemeyi gerektirir ve karsiliginda bir sey kazandirmaz.

- [ ] **Step 3: Kok scriptlerini ekle**

Kok `package.json` dosyasinin son hali:

```json
{
  "name": "st",
  "version": "0.0.0",
  "private": true,
  "packageManager": "pnpm@10.30.3",
  "engines": {
    "node": ">=22"
  },
  "scripts": {
    "dev": "turbo dev",
    "build": "turbo build",
    "check-types": "turbo check-types",
    "lint": "biome check",
    "format": "biome format --write"
  },
  "devDependencies": {
    "@biomejs/biome": "^2.5.12",
    "turbo": "^2.10.12"
  }
}
```

- [ ] **Step 4: Turbo gorevlerini dogrula**

Run: `pnpm build`
Expected: Turbo `@st/web#build` gorevini calistirir, "1 successful, 1 total" der.

Run: `pnpm build`
Expected: Ayni komut ikinci kez calistirildiginda "FULL TURBO" veya "cached" ciktisi verir, sure belirgin sekilde duser. Bu cache'in calistigini gosterir.

Run: `pnpm check-types`
Expected: "1 successful, 1 total". TypeScript hatasi yok.

Run: `pnpm lint`
Expected: "No fixes applied." Hata ve uyari yok.

- [ ] **Step 5: Commit**

```bash
git add turbo.json package.json pnpm-lock.yaml
git commit -m "Turborepo'yu kur ve kok komutlarini tanimla

build, check-types ve dev gorevleri. build ciktisi cache'leniyor, dev
cache'lenmiyor. dependsOn ^build packages/ui dogdugunda dogru sirayi verecek.

lint turbo gorevi degil: Biome kokte tek gecisde tum repoyu tariyor.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Paylasilan TypeScript tabani

Bu gorev ek tip sikiligini tek yerde toplar, boylece `packages/ui` ve sonraki uygulamalar ayni tabani devralir.

**Files:**
- Create: `tsconfig.base.json`
- Modify: `apps/web/tsconfig.json`

**Interfaces:**
- Consumes: Task 2'nin urettigi `apps/web/tsconfig.json`.
- Produces: `tsconfig.base.json`, sonraki paketler `"extends": "../../tsconfig.base.json"` ile devralir.

- [ ] **Step 1: tsconfig.base.json'u yaz**

`tsconfig.base.json`:

```json
{
  "$schema": "https://json.schemastore.org/tsconfig",
  "compilerOptions": {
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "skipLibCheck": true
  }
}
```

`noUncheckedIndexedAccess` dizi ve nesne erisimlerinin sonucuna `undefined` ekler. `arr[0]` artik dogrudan kullanilamaz, once kontrol edilmesi gerekir. Bu, tip sisteminin yakalayabildigi en yaygin calisma zamani hatasi sinifini kapatir.

- [ ] **Step 2: apps/web/tsconfig.json'a extends ekle**

Dosyanin en ustune `extends` satirini ekle. Geri kalan hicbir sey silinmez, Next `dev` sirasinda bu dosyaya kendi ekliyor. Son hali:

```json
{
  "extends": "../../tsconfig.base.json",
  "compilerOptions": {
    "target": "ES2017",
    "lib": ["dom", "dom.iterable", "esnext"],
    "allowJs": true,
    "skipLibCheck": true,
    "strict": true,
    "noEmit": true,
    "esModuleInterop": true,
    "module": "esnext",
    "moduleResolution": "bundler",
    "resolveJsonModule": true,
    "isolatedModules": true,
    "jsx": "react-jsx",
    "incremental": true,
    "plugins": [
      {
        "name": "next"
      }
    ],
    "paths": {
      "@/*": ["./src/*"]
    }
  },
  "include": [
    "next-env.d.ts",
    "**/*.ts",
    "**/*.tsx",
    ".next/types/**/*.ts",
    ".next/dev/types/**/*.ts",
    "**/*.mts"
  ],
  "exclude": ["node_modules"]
}
```

- [ ] **Step 3: Tabanin gercekten devralindigini dogrula**

Run: `pnpm --filter @st/web exec tsc --showConfig | grep noUncheckedIndexedAccess`
Expected: `"noUncheckedIndexedAccess": true` satiri cikar. Cikmazsa `extends` yolu yanlistir.

- [ ] **Step 4: Tip kontrolunu calistir**

Run: `pnpm check-types`
Expected: "1 successful, 1 total". Sablonun urettigi dosyalarda dizi erisimi yok, bu yuzden yeni hata cikmamali. Hata cikarsa sablon dosyasini duzelt, kurali gevsetme.

- [ ] **Step 5: Commit**

```bash
git add tsconfig.base.json apps/web/tsconfig.json
git commit -m "Paylasilan TypeScript tabanini ekle

noUncheckedIndexedAccess kokte tanimlandi, apps/web devraliyor. Next'in
tsconfig.json'a yazdiklari korundu cunku dev sirasinda dosyaya dokunuyor.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Dev sunucusunu ucdan uca dogrula

Onceki gorevler build ve tip kontrolunu dogruladi. Bu gorev uygulamanin tarayicida gercekten acildigini ve `next dev` calistiktan sonra calisma agacinin temiz kaldigini dogrular.

**Files:**
- Create: `.claude/launch.json`

**Interfaces:**
- Consumes: Task 3'un `dev` gorevi, Task 2'nin `@st/web` paket adi.
- Produces: yok, son gorev.

- [ ] **Step 1: launch.json'u yaz**

`.claude/launch.json` bu oturumda calisma agacindan silinmisti. Tarayici onizlemesi dev sunucusunu bu dosyadan baslatiyor, geri yaziliyor:

```json
{
  "version": "0.0.1",
  "configurations": [
    {
      "name": "web",
      "runtimeExecutable": "pnpm",
      "runtimeArgs": ["--filter", "@st/web", "dev"],
      "port": 3000
    }
  ]
}
```

- [ ] **Step 2: Dev sunucusunu baslat**

`preview_start` aracini `{"name": "web"}` ile cagir.
Expected: Sunucu ayaga kalkar, tarayici sekmesi `http://localhost:3000` adresinde acilir.

- [ ] **Step 3: Sayfanin gercekten render edildigini dogrula**

`read_page` aracini cagir.
Expected: Next.js sablon sayfasinin icerigi gorunur. Sayfada "Get started by editing" metni ve `src/app/page.tsx` gecer.

`read_console_messages` aracini `{"onlyErrors": true}` ile cagir.
Expected: Bos. Konsol hatasi yok.

`preview_logs` aracini `{"level": "error"}` ile cagir.
Expected: Bos. Sunucu hatasi yok.

- [ ] **Step 4: Ekran goruntusu al**

`computer` aracini `{"action": "screenshot"}` ile cagir. Bu goruntu kullaniciya kurulumun calistiginin kaniti olarak sunulur.

- [ ] **Step 5: Calisma agacinin temiz kaldigini dogrula**

Dev sunucusu en az bir kez calistiktan sonra:

Run: `git status --short`
Expected: Sadece `?? .claude/launch.json` cikar. `apps/web/AGENTS.md` degismis gorunuyorsa `next dev` icerigi yeniden yazmis demektir; o degisikligi de commit et, silme.

- [ ] **Step 6: Dev sunucusunu durdur**

`preview_stop` aracini Step 2'nin dondurdugu `serverId` ile cagir.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Dev sunucusu icin launch.json'u geri yaz

Tarayici onizlemesi @st/web dev gorevini bu dosyadan baslatiyor.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```
