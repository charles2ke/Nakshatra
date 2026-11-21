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

The e2e tests mock every `/api/*` call, so no backend is needed:

- `e2e/helpers.ts` holds the fixtures, the persona injectors and `mockAllApis(page)`. The address
  book, wish list and coupon mocks (`mockAddressBook`, `mockWishList`, `mockCoupons`) keep state
  for the page and follow the service rules, and `priceCheckout()` mirrors the order service's
  pricing for `/api/orders/quote`.
- `e2e/app.spec.ts` covers navigation, personas, locales, accessibility and the core flows;
  `e2e/shopping.spec.ts` covers deals, recently viewed, Buy Now, reviews, questions and answers,
  stock alerts, the wish list, search refinements, saved for later, addresses, checkout options,
  order filters, returns, coupon setup, and delivery and expiry dates in other time zones.
- The route registered last wins, and `**/api/search**` also matches `/api/search/suggest`, so
  override search results with a URL predicate rather than another glob.
- Tests that assert dates pin `test.use({ timezoneId: 'UTC' })` and `page.clock.setFixedTime(...)`.
  The `west of UTC` and `east of UTC` blocks in `shopping.spec.ts` run in `America/New_York` and
  `Asia/Kolkata` time instead, so a calendar day that shifts with the browser's time zone fails them.
- Tests save screenshots to `e2e/screenshots/`.

## Build

```bash
npm run build
```

---

## UX conventions

- **Design tokens** live in `src/index.css` (`:root`): colours, radii, shadows and spacing. Use the
  variables instead of hard-coded hex values so a theme change stays in one place.
- **Every interactive element carries a `data-testid`** so Playwright can target it without relying
  on visible text, which changes per locale.
- **Accessibility baseline**: a skip link jumps to `#main-content`, all focusable elements get a
  visible `:focus-visible` ring, icon-only controls have `aria-label`s, toasts sit in an
  `aria-live="polite"` region, and the persona menu is keyboard operable (closes on `Escape` or an
  outside click).
- **Loading and empty states**: lists render shimmer placeholders (`.skeleton-card`) while loading
  and an `.empty-state` panel with an actionable hint when there is nothing to show — never a blank
  page.
- **Motion** is disabled automatically under `prefers-reduced-motion: reduce`.
- **Responsive**: the header wraps and wide tables are wrapped in `.table-scroll` so they scroll
  horizontally on small screens instead of overflowing the page.
- **API errors**: `request()` in `src/api/client.ts` throws the server's `{ error }` message, so show
  `errorMessage(e)` in a `role="alert"` element rather than a generic failure text.
- **Header badges** come from `BadgeContext` (`useBadges()`). Pages that change the cart or the
  notification feed call `syncCart(cart)` / `syncUnread(count)` with the server response, or
  `refreshBadges()` when they have no fresh data; counts also refresh on every navigation.
- **Type-ahead**: `SuggestInput` is an ARIA combobox over `/api/search/suggest` (arrow keys,
  `Enter`, `Escape`); reuse it for any keyword field.
- **Recently viewed** products are kept per user in the browser (`localStorage` key
  `nakshatra_recent_<userId>`, the last 10 product ids) by `useRecentlyViewed`; the history is not
  stored on the server.

---

## Localization (i18n)

The app uses a lightweight, zero-dependency i18n layer built on plain React context.

### How it works

| File | Purpose |
|------|---------|
| `src/i18n/LocaleContext.tsx` | `LocaleProvider`, `useLocale()` hook — provides `t()`, `formatCurrency()`, `formatDate()`, `formatDay()`, `formatNumber()` |
| `src/i18n/locales/en.json` | English catalog (default) |
| `src/i18n/locales/es.json` | Spanish catalog |
| `src/i18n/locales/hi.json` | Hindi catalog |
| `src/components/LanguageSwitcher.tsx` | `<select>` in the header |

- Selected locale is persisted in `localStorage` key `nakshatra_locale`.
- `document.documentElement.lang` is updated on every switch.
- If a key is missing in the active locale the app falls back to `en` and logs a `console.warn` **once** per key.
- Numbers/currencies use `Intl.NumberFormat`; dates use `Intl.DateTimeFormat` with the active locale.
- `formatDate()` shows a moment in time (an order's `createdAt`, a status timestamp) in the browser's
  time zone. `formatDay()` shows a calendar day that the server sends in UTC (an estimated delivery
  date, a coupon's expiry) in UTC, so it reads as the same day in every time zone; a value without a
  zone is read as UTC as well.

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
