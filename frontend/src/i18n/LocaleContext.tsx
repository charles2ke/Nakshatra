import { createContext, useContext, useState, useCallback } from 'react';
import type { ReactNode } from 'react';
import en from './locales/en.json';
import es from './locales/es.json';
import hi from './locales/hi.json';

export type Locale = 'en' | 'es' | 'hi';

export const LOCALE_NAMES: Record<Locale, string> = {
  en: 'English',
  es: 'Español',
  hi: 'हिन्दी',
};

type Catalog = Record<string, string>;

const CATALOGS: Record<Locale, Catalog> = { en, es, hi };

const warned = new Set<string>();

function lookup(locale: Locale, key: string, vars?: Record<string, string>): string {
  let msg = CATALOGS[locale][key];
  if (msg === undefined) {
    if (locale !== 'en' && !warned.has(key)) {
      console.warn(`[i18n] Missing key "${key}" in locale "${locale}", falling back to "en"`);
      warned.add(key);
    }
    msg = CATALOGS['en'][key] ?? key;
  }
  if (vars) {
    Object.entries(vars).forEach(([k, v]) => { msg = msg.replace(`{${k}}`, v); });
  }
  return msg;
}

interface LocaleContextValue {
  locale: Locale;
  setLocale: (l: Locale) => void;
  t: (key: string, vars?: Record<string, string>) => string;
  formatCurrency: (amount: number, currency: string) => string;
  formatDate: (iso: string) => string;
  formatNumber: (n: number) => string;
}

const LocaleContext = createContext<LocaleContextValue>({
  locale: 'en',
  setLocale: () => {},
  t: (k) => k,
  formatCurrency: (a, c) => `${c} ${a}`,
  formatDate: (s) => s,
  formatNumber: (n) => String(n),
});

const LS_KEY = 'nakshatra_locale';

export function LocaleProvider({ children }: { children: ReactNode }) {
  const [locale, setLocaleState] = useState<Locale>(() => {
    const stored = localStorage.getItem(LS_KEY);
    return (stored && stored in CATALOGS ? stored : 'en') as Locale;
  });

  function setLocale(l: Locale) {
    setLocaleState(l);
    localStorage.setItem(LS_KEY, l);
    document.documentElement.lang = l;
  }

  // Set on mount
  useState(() => { document.documentElement.lang = locale; });

  const t = useCallback(
    (key: string, vars?: Record<string, string>) => lookup(locale, key, vars),
    [locale],
  );

  const formatCurrency = useCallback((amount: number, currency: string) => {
    try {
      return new Intl.NumberFormat(locale, { style: 'currency', currency }).format(amount);
    } catch {
      return `${currency} ${amount.toFixed(2)}`;
    }
  }, [locale]);

  const formatDate = useCallback((iso: string) => {
    try {
      return new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(new Date(iso));
    } catch {
      return iso;
    }
  }, [locale]);

  const formatNumber = useCallback((n: number) => {
    return new Intl.NumberFormat(locale).format(n);
  }, [locale]);

  return (
    <LocaleContext.Provider value={{ locale, setLocale, t, formatCurrency, formatDate, formatNumber }}>
      {children}
    </LocaleContext.Provider>
  );
}

export function useLocale() { return useContext(LocaleContext); }
