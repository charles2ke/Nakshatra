import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { getOrdersByUser } from '../api/client';
import type { Order } from '../api/client';
import { usePersona } from '../context/PersonaContext';
import { useLocale } from '../i18n/LocaleContext';

export default function OrdersPage() {
  const { currentUser } = usePersona();
  const { t, formatCurrency, formatDate } = useLocale();
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!currentUser) return;
    setLoading(true);
    getOrdersByUser(currentUser.id).then(setOrders).catch(() => {}).finally(() => setLoading(false));
  }, [currentUser]);

  if (!currentUser) return <div>{t('orders.noUser')}</div>;

  return (
    <div data-testid="orders-page">
      <h1>{t('orders.title')}</h1>
      {loading && <div>{t('orders.loading')}</div>}
      {!loading && orders.length === 0 && <div data-testid="orders-empty">{t('orders.empty')}</div>}
      <div data-testid="orders-list">
        {orders.map(o => (
          <div key={o.id} className="order-card" data-testid={`order-card-${o.id}`}>
            <Link to={`/orders/${o.id}`}>
              <div><strong>Order #{o.id.slice(0,8)}</strong></div>
              <div>{t('orders.status')}: <span data-testid={`order-status-${o.id}`}>{o.status}</span></div>
              <div>{t('orders.total')}: {formatCurrency(o.total ?? 0, o.currency)}</div>
              <div>{t('orders.date')}: {formatDate(o.createdAt)}</div>
            </Link>
          </div>
        ))}
      </div>
    </div>
  );
}
