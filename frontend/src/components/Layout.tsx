import { Outlet, NavLink } from 'react-router-dom';
import { usePersona } from '../context/PersonaContext';
import PersonaSwitcher from './PersonaSwitcher';
import LanguageSwitcher from './LanguageSwitcher';
import { useLocale } from '../i18n/LocaleContext';
import type { Persona } from '../api/client';

interface NavItem { key: string; labelKey: string; to: string; allow: Persona[]; }
const NAV: NavItem[] = [
  { key: 'home',             labelKey: 'nav.home',           to: '/',               allow: ['Customer','Vendor','Admin','FulfillmentAgent'] },
  { key: 'search',           labelKey: 'nav.search',         to: '/search',         allow: ['Customer','Admin'] },
  { key: 'cart',             labelKey: 'nav.cart',           to: '/cart',           allow: ['Customer','Admin'] },
  { key: 'orders',           labelKey: 'nav.orders',         to: '/orders',         allow: ['Customer','Vendor','Admin','FulfillmentAgent'] },
  { key: 'billing',          labelKey: 'nav.billing',        to: '/billing',        allow: ['Customer','Vendor','Admin'] },
  { key: 'fulfillment',      labelKey: 'nav.fulfillment',    to: '/fulfillment',    allow: ['FulfillmentAgent','Admin'] },
  { key: 'inventory',        labelKey: 'nav.inventory',      to: '/inventory',      allow: ['Vendor','Admin'] },
  { key: 'notifications',    labelKey: 'nav.notifications',  to: '/notifications',  allow: ['Customer','Vendor','Admin','FulfillmentAgent'] },
  { key: 'products-setup',   labelKey: 'nav.setup.products', to: '/setup/products', allow: ['Vendor','Admin'] },
  { key: 'vendors-setup',    labelKey: 'nav.setup.vendors',  to: '/setup/vendors',  allow: ['Admin'] },
  { key: 'users-setup',      labelKey: 'nav.setup.users',    to: '/setup/users',    allow: ['Admin'] },
];

export default function Layout() {
  const { currentUser } = usePersona();
  const { t } = useLocale();

  const visibleNav = NAV.filter(n => currentUser && n.allow.includes(currentUser.persona));

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content" data-testid="skip-link">{t('a11y.skipToContent')}</a>
      <header className="app-header" data-testid="app-header">
        <div className="header-brand">🌟 {t('brand')}</div>
        <nav data-testid="main-nav" aria-label={t('a11y.mainNav')}>
          {visibleNav.map(n => (
            <NavLink
              key={n.to}
              to={n.to}
              className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}
              data-testid={`nav-${n.key}`}
            >
              {t(n.labelKey)}
            </NavLink>
          ))}
        </nav>
        <LanguageSwitcher />
        <PersonaSwitcher />
      </header>
      <main className="app-main" id="main-content" tabIndex={-1}>
        <Outlet />
      </main>
    </div>
  );
}
