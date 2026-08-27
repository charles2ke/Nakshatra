import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { getOrdersByUser } from '../api/client';
import type { Order } from '../api/client';
import { usePersona } from '../context/PersonaContext';

export default function OrdersPage() {
  const { currentUser } = usePersona();
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!currentUser) return;
    setLoading(true);
    getOrdersByUser(currentUser.id).then(setOrders).catch(() => {}).finally(() => setLoading(false));
  }, [currentUser]);

  if (!currentUser) return <div>Please select a user.</div>;

  return (
    <div data-testid="orders-page">
      <h1>My Orders</h1>
      {loading && <div>Loading…</div>}
      {!loading && orders.length === 0 && <div data-testid="orders-empty">No orders yet.</div>}
      <div data-testid="orders-list">
        {orders.map(o => (
          <div key={o.id} className="order-card" data-testid={`order-card-${o.id}`}>
            <Link to={`/orders/${o.id}`}>
              <div><strong>Order #{o.id.slice(0,8)}</strong></div>
              <div>Status: <span data-testid={`order-status-${o.id}`}>{o.status}</span></div>
              <div>Total: {o.currency} {o.total?.toFixed(2)}</div>
              <div>Date: {new Date(o.createdAt).toLocaleDateString()}</div>
            </Link>
          </div>
        ))}
      </div>
    </div>
  );
}
