import { useState, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { getOrder, getOrderStatus } from '../api/client';
import type { Order, OrderStatusResponse } from '../api/client';

export default function OrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [order, setOrder] = useState<Order | null>(null);
  const [status, setStatus] = useState<OrderStatusResponse | null>(null);

  useEffect(() => {
    if (!id) return;
    getOrder(id).then(setOrder).catch(() => {});
    getOrderStatus(id).then(setStatus).catch(() => {});
  }, [id]);

  if (!order) return <div data-testid="order-loading">Loading…</div>;

  return (
    <div data-testid="order-detail-page">
      <h1>Order #{order.id.slice(0,8)}</h1>
      <div data-testid="order-status-badge" className={`status-badge status-${order.status.toLowerCase()}`}>{order.status}</div>
      <table className="order-items-table">
        <thead><tr><th>Item</th><th>Price</th><th>Qty</th></tr></thead>
        <tbody>
          {order.items.map(i => (
            <tr key={i.productId}><td>{i.name}</td><td>{i.price}</td><td>{i.quantity}</td></tr>
          ))}
        </tbody>
      </table>
      <div>Subtotal: {order.subtotal}</div>
      <div>Tax: {order.tax}</div>
      <div>Total: <strong data-testid="order-total">{order.total}</strong></div>
      {status && (
        <div className="order-timeline" data-testid="order-timeline">
          <h2>Status History</h2>
          {status.history.map((h, i) => (
            <div key={i} className="timeline-item" data-testid={`timeline-item-${i}`}>
              <span className="timeline-status">{h.status}</span>
              <span className="timeline-time">{new Date(h.timestamp).toLocaleString()}</span>
              {h.note && <span className="timeline-note">{h.note}</span>}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
