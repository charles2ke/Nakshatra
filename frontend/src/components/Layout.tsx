import { Outlet, NavLink } from 'react-router-dom';
import { usePersona } from '../context/PersonaContext';
import PersonaSwitcher from './PersonaSwitcher';
import type { Persona } from '../api/client';

interface NavItem { label: string; to: string; allow: Persona[]; }
const NAV: NavItem[] = [
  { label: 'Home', to: '/', allow: ['Customer','Vendor','Admin','FulfillmentAgent'] },
  { label: 'Search', to: '/search', allow: ['Customer','Admin'] },
  { label: 'Cart', to: '/cart', allow: ['Customer','Admin'] },
  { label: 'Orders', to: '/orders', allow: ['Customer','Vendor','Admin','FulfillmentAgent'] },
  { label: 'Billing', to: '/billing', allow: ['Customer','Vendor','Admin'] },
  { label: 'Fulfillment', to: '/fulfillment', allow: ['FulfillmentAgent','Admin'] },
  { label: 'Products Setup', to: '/setup/products', allow: ['Vendor','Admin'] },
  { label: 'Vendors Setup', to: '/setup/vendors', allow: ['Admin'] },
  { label: 'Users Setup', to: '/setup/users', allow: ['Admin'] },
];

export default function Layout() {
  const { currentUser } = usePersona();

  const visibleNav = NAV.filter(n => currentUser && n.allow.includes(currentUser.persona));

  return (
    <div className="app-shell">
      <header className="app-header" data-testid="app-header">
        <div className="header-brand">🌟 Nakshatra</div>
        <nav data-testid="main-nav">
          {visibleNav.map(n => (
            <NavLink key={n.to} to={n.to} className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'} data-testid={`nav-${n.label.toLowerCase().replace(/ /g, '-')}`}>
              {n.label}
            </NavLink>
          ))}
        </nav>
        <PersonaSwitcher />
      </header>
      <main className="app-main">
        <Outlet />
      </main>
    </div>
  );
}
