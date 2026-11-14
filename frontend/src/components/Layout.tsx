import { Outlet, NavLink } from 'react-router-dom';
import type { MouseEvent } from 'react';
import { usePersona } from '../context/PersonaContext';
import { useBadges } from '../context/BadgeContext';
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
  const { t, formatNumber } = useLocale();
  const { cartCount, unreadCount } = useBadges();
  const counts: Record<string, number> = { cart: cartCount, notifications: unreadCount };

  const visibleNav = NAV.filter(n => currentUser && n.allow.includes(currentUser.persona));
  const focusMainContent = (event: MouseEvent<HTMLAnchorElement>) => {
    event.preventDefault();
    window.history.pushState(null, '', '#main-content');
    document.getElementById('main-content')?.focus();
  };

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content" data-testid="skip-link" onClick={focusMainContent}>{t('a11y.skipToContent')}</a>
      <header className="app-header" data-testid="app-header">
        <div className="header-brand">🌟 {t('brand')}</div>
        <nav data-testid="main-nav" aria-label={t('a11y.mainNav')}>
          {visibleNav.map(n => {
            const count = counts[n.key] ?? 0;
            const countId = `nav-${n.key}-count`;
            return (
              <span key={n.to} className={count > 0 ? 'nav-item has-badge' : 'nav-item'}>
                <NavLink
                  to={n.to}
                  className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}
                  data-testid={`nav-${n.key}`}
                  aria-describedby={count > 0 ? countId : undefined}
                >
                  {t(n.labelKey)}
                </NavLink>
                {count > 0 && (
                  <>
                    <span className="nav-badge" data-testid={`nav-${n.key}-badge`} aria-hidden="true">
                      {count > 99 ? '99+' : formatNumber(count)}
                    </span>
                    <span id={countId} hidden>{t(`nav.badge.${n.key}`, { count: formatNumber(count) })}</span>
                  </>
                )}
              </span>
            );
          })}
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
