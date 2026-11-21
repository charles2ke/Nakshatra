import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { getOrdersByUser, addToCart } from '../api/client';
import type { Order, OrderItem, OrderStatus } from '../api/client';
import { usePersona } from '../context/PersonaContext';
import { useBadges } from '../context/BadgeContext';
import { useLocale } from '../i18n/LocaleContext';
import { useToast } from '../hooks/useToast';

const DAY_MS = 24 * 60 * 60 * 1000;
const STATUS_GROUPS: Record<string, OrderStatus[]> = {
  open: ['Created', 'AwaitingPayment', 'Paid', 'Packed'],
  shipped: ['Shipped'],
  delivered: ['Delivered'],
  cancelled: ['Cancelled'],
  returns: ['ReturnRequested', 'Returned'],
};
const IN_TRANSIT: OrderStatus[] = ['Created', 'AwaitingPayment', 'Paid', 'Packed', 'Shipped'];
const TIME_WINDOWS: Record<string, number> = { '30d': 30, '6m': 182 };

export default function OrdersPage() {
  const { currentUser } = usePersona();
  const { t, formatCurrency, formatDate, formatDay } = useLocale();
  const { syncCart } = useBadges();
  const { toast, showToast } = useToast();
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);
  const [query, setQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  // "Last N days" filters fix their start when chosen, so the list stays stable between renders.
  const [timeFilter, setTimeFilter] = useState<{ value: string; since: number | null }>({ value: '', since: null });
  const [buyingKey, setBuyingKey] = useState<string | null>(null);

  useEffect(() => {
    if (!currentUser) return;
    setLoading(true);
    getOrdersByUser(currentUser.id).then(setOrders).catch(() => {}).finally(() => setLoading(false));
  }, [currentUser]);

  if (!currentUser) return <div>{t('orders.noUser')}</div>;

  const canBuyAgain = currentUser.persona === 'Customer' || currentUser.persona === 'Admin';
  const years = [...new Set(orders.map(o => new Date(o.createdAt).getFullYear()))].sort((a, b) => b - a);
  const needle = query.trim().toLowerCase();
  const visible = orders.filter(o => {
    if (needle && !o.id.toLowerCase().includes(needle) && !o.items.some(i => i.name.toLowerCase().includes(needle))) return false;
    if (statusFilter && !STATUS_GROUPS[statusFilter]?.includes(o.status)) return false;
    if (timeFilter.since !== null) return new Date(o.createdAt).getTime() >= timeFilter.since;
    if (timeFilter.value) return new Date(o.createdAt).getFullYear() === Number(timeFilter.value);
    return true;
  });

  function chooseTimeFilter(value: string) {
    const days = TIME_WINDOWS[value];
    setTimeFilter({ value, since: days ? Date.now() - days * DAY_MS : null });
  }

  async function handleBuyAgain(order: Order, item: OrderItem) {
    if (!currentUser || buyingKey) return;
    setBuyingKey(`${order.id}:${item.productId}`);
    try {
      const res = await addToCart(currentUser.id, item.productId, 1);
      syncCart(res.cart);
      showToast(t('home.addedToCart', { name: item.name }));
    } catch { showToast(t('home.addToCartFailed')); }
    finally { setBuyingKey(null); }
  }

  return (
    <div data-testid="orders-page">
      <h1>{t('orders.title')}</h1>
      <div className="toast-region" role="status" aria-live="polite">
        {toast && <div className="toast" data-testid="orders-toast">{toast}</div>}
      </div>
      {orders.length > 0 && (
        <div className="orders-toolbar" role="search">
          <input
            type="search"
            data-testid="orders-search"
            aria-label={t('orders.search')}
            placeholder={t('orders.search')}
            value={query}
            onChange={e => setQuery(e.target.value)}
          />
          <label>
            {t('orders.filterStatus')}
            <select data-testid="orders-status-filter" value={statusFilter} onChange={e => setStatusFilter(e.target.value)}>
              <option value="">{t('orders.filter.all')}</option>
              {Object.keys(STATUS_GROUPS).map(g => <option key={g} value={g}>{t(`orders.filter.${g}`)}</option>)}
            </select>
          </label>
          <label>
            {t('orders.filterTime')}
            <select data-testid="orders-time-filter" value={timeFilter.value} onChange={e => chooseTimeFilter(e.target.value)}>
              <option value="">{t('orders.time.all')}</option>
              <option value="30d">{t('orders.time.30d')}</option>
              <option value="6m">{t('orders.time.6m')}</option>
              {years.map(y => <option key={y} value={String(y)}>{y}</option>)}
            </select>
          </label>
        </div>
      )}
      {loading && <div>{t('orders.loading')}</div>}
      {!loading && orders.length === 0 && <div data-testid="orders-empty">{t('orders.empty')}</div>}
      {!loading && orders.length > 0 && visible.length === 0 && (
        <div className="empty-state" data-testid="orders-filter-empty">{t('orders.noMatches')}</div>
      )}
      <div data-testid="orders-list">
        {visible.map(o => (
          <div key={o.id} className="order-card" data-testid={`order-card-${o.id}`}>
            <Link to={`/orders/${o.id}`}>
              <div><strong>Order #{o.id.slice(0,8)}</strong></div>
              <div>{t('orders.status')}: <span data-testid={`order-status-${o.id}`}>{o.status}</span></div>
              <div>{t('orders.total')}: {formatCurrency(o.total ?? 0, o.currency)}</div>
              <div>{t('orders.date')}: {formatDate(o.createdAt)}</div>
              {o.estimatedDelivery && IN_TRANSIT.includes(o.status) && (
                <div className="order-eta" data-testid={`order-eta-${o.id}`}>{t('orders.arriving', { date: formatDay(o.estimatedDelivery) })}</div>
              )}
            </Link>
            <ul className="order-card-items">
              {o.items.map(i => (
                <li key={i.productId}>
                  <Link to={`/product/${i.productId}`}>{i.name}</Link>
                  {canBuyAgain && (
                    <button
                      type="button"
                      className="btn-secondary btn-small"
                      data-testid={`buy-again-${o.id}-${i.productId}`}
                      disabled={buyingKey !== null}
                      onClick={() => handleBuyAgain(o, i)}
                    >{t('orders.buyAgain')}</button>
                  )}
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
    </div>
  );
}
