import { useState, useEffect, useRef } from 'react';
import { useParams } from 'react-router-dom';
import { getOrder, getOrderStatus, getTrace, cancelOrder, errorMessage } from '../api/client';
import type { Order, OrderStatus, OrderStatusResponse, SupplyChainTrace } from '../api/client';
import { usePersona } from '../context/PersonaContext';
import { useLocale } from '../i18n/LocaleContext';

// Matches the order service: once an order has shipped it can no longer be cancelled.
const CANCELLABLE: OrderStatus[] = ['Created', 'AwaitingPayment', 'Paid', 'Packed'];

export default function OrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { currentUser } = usePersona();
  const { t, formatCurrency, formatDate } = useLocale();
  const [order, setOrder] = useState<Order | null>(null);
  const [status, setStatus] = useState<OrderStatusResponse | null>(null);
  const [trace, setTrace] = useState<SupplyChainTrace | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [notice, setNotice] = useState('');
  const [cancelError, setCancelError] = useState('');
  const cancelButton = useRef<HTMLButtonElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    if (!id) return;
    getOrder(id).then(setOrder).catch(() => {});
    getOrderStatus(id).then(setStatus).catch(() => {});
    getTrace(id).then(setTrace).catch(() => {});
  }, [id]);

  function keepOrder() {
    setConfirming(false);
    cancelButton.current?.focus();
  }

  async function handleCancel() {
    if (!order) return;
    setCancelling(true);
    setCancelError('');
    try {
      const updated = await cancelOrder(order.id);
      setOrder(updated);
      setNotice(t('orderDetail.cancelled'));
      // The cancel button is about to disappear; keep keyboard focus on the page.
      heading.current?.focus();
      getOrderStatus(order.id).then(setStatus).catch(() => {});
      getTrace(order.id).then(setTrace).catch(() => {});
    } catch (e) {
      setCancelError(errorMessage(e));
      cancelButton.current?.focus();
      // The order may have moved on (e.g. shipped) since this page loaded.
      getOrder(order.id).then(setOrder).catch(() => {});
    } finally {
      setCancelling(false);
      setConfirming(false);
    }
  }

  if (!order) return <div data-testid="order-loading">{t('orderDetail.loading')}</div>;

  const canCancel = CANCELLABLE.includes(order.status) && !!currentUser
    && (currentUser.persona === 'Admin' || (currentUser.persona === 'Customer' && currentUser.id === order.userId));

  return (
    <div data-testid="order-detail-page">
      <h1 ref={heading} tabIndex={-1}>Order #{order.id.slice(0,8)}</h1>
      <div data-testid="order-status-badge" className={`status-badge status-${order.status.toLowerCase()}`}>{order.status}</div>
      <div role="status" aria-live="polite">
        {notice && <div className="notice" data-testid="order-cancel-notice">{notice}</div>}
      </div>
      {cancelError && <div className="error" role="alert" data-testid="order-cancel-error">{cancelError}</div>}
      {canCancel && (
        <div className="order-actions">
          <button
            ref={cancelButton}
            type="button"
            className="btn-danger"
            data-testid="cancel-order-btn"
            aria-expanded={confirming}
            aria-controls="cancel-order-confirm-panel"
            onClick={() => { setCancelError(''); setConfirming(true); }}
          >
            {t('orderDetail.cancel')}
          </button>
          {confirming && (
            <div
              id="cancel-order-confirm-panel"
              className="confirm-panel"
              role="group"
              aria-labelledby="cancel-order-confirm-text"
              data-testid="cancel-order-confirm-panel"
              onKeyDown={e => { if (e.key === 'Escape' && !cancelling) keepOrder(); }}
            >
              <p id="cancel-order-confirm-text">{t('orderDetail.cancelConfirm')}</p>
              <div className="confirm-actions">
                <button type="button" className="btn-danger" data-testid="cancel-order-confirm" disabled={cancelling} onClick={handleCancel}>
                  {cancelling ? t('orderDetail.cancelling') : t('orderDetail.cancelYes')}
                </button>
                {/* Focus the safe choice first so an accidental Enter does not cancel the order. */}
                <button type="button" className="btn-secondary" data-testid="cancel-order-keep" autoFocus disabled={cancelling} onClick={keepOrder}>
                  {t('orderDetail.cancelKeep')}
                </button>
              </div>
            </div>
          )}
        </div>
      )}
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
