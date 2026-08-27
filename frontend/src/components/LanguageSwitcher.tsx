import { useLocale, LOCALE_NAMES } from '../i18n/LocaleContext';
import type { Locale } from '../i18n/LocaleContext';

export default function LanguageSwitcher() {
  const { locale, setLocale, t } = useLocale();
  return (
    <div className="lang-switcher" data-testid="lang-switcher">
      <label htmlFor="lang-select" className="lang-label">{t('lang.switcher.label')}:</label>
      <select
        id="lang-select"
        data-testid="lang-select"
        value={locale}
        onChange={e => setLocale(e.target.value as Locale)}
      >
        {(Object.entries(LOCALE_NAMES) as [Locale, string][]).map(([code, name]) => (
          <option key={code} value={code}>{name}</option>
        ))}
      </select>
    </div>
  );
}
