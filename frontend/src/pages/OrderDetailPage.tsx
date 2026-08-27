import { useState, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { getOrder, getOrderStatus, getTrace } from '../api/client';
import type { Order, OrderStatusResponse, SupplyChainTrace } from '../api/client';
import { useLocale } from '../i18n/LocaleContext';

export default function OrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { t, formatCurrency, formatDate } = useLocale();
  const [order, setOrder] = useState<Order | null>(null);
  const [status, setStatus] = useState<OrderStatusResponse | null>(null);
  const [trace, setTrace] = useState<SupplyChainTrace | null>(null);

  useEffect(() => {
    if (!id) return;
    getOrder(id).then(setOrder).catch(() => {});
    getOrderStatus(id).then(setStatus).catch(() => {});
    getTrace(id).then(setTrace).catch(() => {});
  }, [id]);

  if (!order) return <div data-testid="order-loading">{t('orderDetail.loading')}</div>;

  return (
    <div data-testid="order-detail-page">
      <h1>Order #{order.id.slice(0,8)}</h1>
      <div data-testid="order-status-badge" className={`status-badge status-${order.status.toLowerCase()}`}>{order.status}</div>
      <table className="order-items-table">
        <thead><tr><th>{t('cart.col.product')}</th><th>{t('cart.col.price')}</th><th>{t('cart.col.qty')}</th></tr></thead>
        <tbody>
          {order.items.map(i => (
            <tr key={i.productId}><td>{i.name}</td><td>{formatCurrency(i.price, order.currency)}</td><td>{i.quantity}</td></tr>
          ))}
        </tbody>
      </table>
      <div>{t('orderDetail.subtotal')}: {formatCurrency(order.subtotal, order.currency)}</div>
      <div>{t('orderDetail.tax')}: {formatCurrency(order.tax, order.currency)}</div>
      <div>{t('orderDetail.total')}: <strong data-testid="order-total">{formatCurrency(order.total, order.currency)}</strong></div>
      {status && (
        <div className="order-timeline" data-testid="order-timeline">
          <h2>{t('orderDetail.statusHistory')}</h2>
          {status.history.map((h, i) => (
            <div key={i} className="timeline-item" data-testid={`timeline-item-${i}`}>
              <span className="timeline-status">{h.status}</span>
              <span className="timeline-time">{formatDate(h.timestamp)}</span>
              {h.note && <span className="timeline-note">{h.note}</span>}
            </div>
          ))}
        </div>
      )}
      {trace && (
        <div className="order-timeline" data-testid="supplychain-trace">
          <h2>{t('trace.title')}</h2>
          <div data-testid="trace-current-stage" className="status-badge">{trace.currentStage}</div>
          {trace.estimatedDelivery && (
            <div data-testid="trace-eta">{t('trace.eta')}: {formatDate(trace.estimatedDelivery)}</div>
          )}
          {trace.events.map((e, i) => (
            <div key={i} className="timeline-item" data-testid={`trace-event-${i}`}>
              <span className="timeline-status">{e.stage}</span>
              <span className="timeline-time">{formatDate(e.timestamp)}</span>
              {e.description && <span className="timeline-note">{e.description}</span>}
              {e.location && <span className="timeline-note">{e.location}</span>}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
