# Nakshatra Frontend

React + Vite + TypeScript shopping portal frontend.

## Getting Started

```bash
cd frontend
npm install
npm run dev
```

Open http://localhost:5173

## Environment Variables

- `VITE_API_BASE_URL` — Backend API base URL (default: `http://localhost:5000`)

## Running Tests

```bash
npx playwright install --with-deps chromium
npm run test:e2e
```

## Build

```bash
npm run build
```

---

## Localization (i18n)

The app uses a lightweight, zero-dependency i18n layer built on plain React context.

### How it works

| File | Purpose |
|------|---------|
| `src/i18n/LocaleContext.tsx` | `LocaleProvider`, `useLocale()` hook — provides `t()`, `formatCurrency()`, `formatDate()`, `formatNumber()` |
| `src/i18n/locales/en.json` | English catalog (default) |
| `src/i18n/locales/es.json` | Spanish catalog |
| `src/i18n/locales/hi.json` | Hindi catalog |
| `src/components/LanguageSwitcher.tsx` | `<select>` in the header |

- Selected locale is persisted in `localStorage` key `nakshatra_locale`.
- `document.documentElement.lang` is updated on every switch.
- If a key is missing in the active locale the app falls back to `en` and logs a `console.warn` **once** per key.
- Numbers/currencies use `Intl.NumberFormat`; dates use `Intl.DateTimeFormat` with the active locale.

### Adding a new locale

1. Copy `src/i18n/locales/en.json` to e.g. `src/i18n/locales/fr.json`.
2. Translate all values (keys must stay identical).
3. Import the new file in `src/i18n/LocaleContext.tsx`:
   ```ts
   import fr from './locales/fr.json';
   ```
4. Add `'fr'` to the `Locale` type and both `CATALOGS` and `LOCALE_NAMES` maps:
   ```ts
   export type Locale = 'en' | 'es' | 'hi' | 'fr';
   export const LOCALE_NAMES: Record<Locale, string> = { en: 'English', es: 'Español', hi: 'हिन्दी', fr: 'Français' };
   const CATALOGS: Record<Locale, Catalog> = { en, es, hi, fr };
   ```
5. The language switcher will automatically show the new option.
