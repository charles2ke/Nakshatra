import { useState, useEffect, useRef } from 'react';
import type { FormEvent } from 'react';
import { useParams } from 'react-router-dom';
import { getOrder, getOrderStatus, getTrace, cancelOrder, requestReturn, completeReturn, errorMessage, RETURN_WINDOW_DAYS } from '../api/client';
import type { Order, OrderStatus, OrderStatusResponse, SupplyChainTrace } from '../api/client';
import { usePersona } from '../context/PersonaContext';
import { useLocale } from '../i18n/LocaleContext';

// Matches the order service: once an order has shipped it can no longer be cancelled.
const CANCELLABLE: OrderStatus[] = ['Created', 'AwaitingPayment', 'Paid', 'Packed'];
const IN_TRANSIT: OrderStatus[] = ['Created', 'AwaitingPayment', 'Paid', 'Packed', 'Shipped'];
const RETURN_REASONS = ['damaged', 'wrongItem', 'notAsDescribed', 'noLongerNeeded', 'other'];
const DAY_MS = 24 * 60 * 60 * 1000;
const MAX_RETURN_REASON = 500;

export default function OrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { currentUser } = usePersona();
  const { t, formatCurrency, formatDate, formatDay } = useLocale();
  const [order, setOrder] = useState<Order | null>(null);
  const [status, setStatus] = useState<OrderStatusResponse | null>(null);
  const [trace, setTrace] = useState<SupplyChainTrace | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [notice, setNotice] = useState('');
  const [cancelError, setCancelError] = useState('');
  const cancelButton = useRef<HTMLButtonElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const [returnOpen, setReturnOpen] = useState(false);
  const [returnReason, setReturnReason] = useState(RETURN_REASONS[0]);
  const [returnComments, setReturnComments] = useState('');
  const [returnBusy, setReturnBusy] = useState(false);
  const [returnError, setReturnError] = useState('');
  const returnButton = useRef<HTMLButtonElement>(null);
  // When the page opened; the return window is judged against this rather than re-reading the clock each render.
  const [openedAt] = useState(() => Date.now());

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

  function refreshTracking(orderId: string) {
    getOrderStatus(orderId).then(setStatus).catch(() => {});
    getTrace(orderId).then(setTrace).catch(() => {});
  }

  async function handleRequestReturn(e: FormEvent) {
    e.preventDefault();
    if (!order) return;
    const comments = returnComments.trim();
    const reason = `${t(`orderDetail.returnReason.${returnReason}`)}${comments ? `: ${comments}` : ''}`.slice(0, MAX_RETURN_REASON);
    setReturnBusy(true);
    setReturnError('');
    try {
      const updated = await requestReturn(order.id, reason);
      setOrder(updated);
      setReturnOpen(false);
      setNotice(t('orderDetail.returnRequested'));
      heading.current?.focus();
      refreshTracking(order.id);
    } catch (err) { setReturnError(errorMessage(err)); }
    finally { setReturnBusy(false); }
  }

  async function handleCompleteReturn() {
    if (!order) return;
    setReturnBusy(true);
    setReturnError('');
    try {
      const updated = await completeReturn(order.id);
      setOrder(updated);
      setNotice(t('orderDetail.returnCompleted'));
      refreshTracking(order.id);
    } catch (err) { setReturnError(errorMessage(err)); }
    finally { setReturnBusy(false); }
  }

  if (!order) return <div data-testid="order-loading">{t('orderDetail.loading')}</div>;

  const isOwnerOrAdmin = !!currentUser
    && (currentUser.persona === 'Admin' || (currentUser.persona === 'Customer' && currentUser.id === order.userId));
  const canCancel = CANCELLABLE.includes(order.status) && isOwnerOrAdmin;
  // The return window runs from the latest delivery, as the order service counts it.
  const deliveredAt = status?.orderId === order.id
    ? [...status.history].reverse().find(h => h.status === 'Delivered')?.timestamp
    : undefined;
  const returnDeadline = deliveredAt ? new Date(new Date(deliveredAt).getTime() + RETURN_WINDOW_DAYS * DAY_MS) : null;
  const returnOpenNow = order.status === 'Delivered' && !!returnDeadline && openedAt <= returnDeadline.getTime();
  const canReceiveReturn = order.status === 'ReturnRequested' && !!currentUser
    && (currentUser.persona === 'Admin' || currentUser.persona === 'FulfillmentAgent');

  return (
    <div data-testid="order-detail-page">
      <h1 ref={heading} tabIndex={-1}>Order #{order.id.slice(0,8)}</h1>
      <div data-testid="order-status-badge" className={`status-badge status-${order.status.toLowerCase()}`}>{order.status}</div>
      <div role="status" aria-live="polite">
        {notice && <div className="notice" data-testid="order-cancel-notice">{notice}</div>}
      </div>
      {order.status === 'Delivered' && returnDeadline && (
        <div className="return-window" data-testid="return-window">
          {returnOpenNow
            ? t('orderDetail.returnEligible', { date: formatDate(returnDeadline.toISOString()) })
            : t('orderDetail.returnClosed', { date: formatDate(returnDeadline.toISOString()) })}
        </div>
      )}
      {(order.status === 'ReturnRequested' || order.status === 'Returned') && (
        <div className="notice" data-testid="return-status">
          {order.status === 'ReturnRequested' ? t('orderDetail.returnInProgress') : t('orderDetail.returnDone')}
          {order.returnReason && <div className="choice-note" data-testid="return-reason-text">{t('orderDetail.returnReasonLabel', { reason: order.returnReason })}</div>}
        </div>
      )}
      {returnError && <div className="error" role="alert" data-testid="return-error">{returnError}</div>}
      {returnOpenNow && isOwnerOrAdmin && (
        <div className="order-actions">
          <button
            ref={returnButton}
            type="button"
            className="btn-secondary"
            data-testid="return-order-btn"
            aria-expanded={returnOpen}
            aria-controls="return-panel"
            onClick={() => { setReturnError(''); setReturnOpen(open => !open); }}
          >
            {t('orderDetail.returnItems')}
          </button>
          {returnOpen && (
            <form id="return-panel" className="confirm-panel" data-testid="return-panel" onSubmit={handleRequestReturn} aria-labelledby="return-panel-title">
              <p id="return-panel-title"><strong>{t('orderDetail.returnTitle')}</strong></p>
              <label className="field-label">
                {t('orderDetail.returnWhy')}
                <select data-testid="return-reason" value={returnReason} onChange={e => setReturnReason(e.target.value)}>
                  {RETURN_REASONS.map(r => <option key={r} value={r}>{t(`orderDetail.returnReason.${r}`)}</option>)}
                </select>
              </label>
              <label className="field-label">
                {t('orderDetail.returnComments')}
                <textarea data-testid="return-comments" rows={2} maxLength={400} value={returnComments} onChange={e => setReturnComments(e.target.value)} />
              </label>
              <div className="confirm-actions">
                <button type="submit" className="btn-primary" data-testid="return-submit" disabled={returnBusy}>
                  {returnBusy ? t('orderDetail.returnSubmitting') : t('orderDetail.returnSubmit')}
                </button>
                <button type="button" className="btn-secondary" data-testid="return-cancel" disabled={returnBusy} onClick={() => { setReturnOpen(false); returnButton.current?.focus(); }}>
                  {t('orderDetail.cancelKeep')}
                </button>
              </div>
            </form>
          )}
        </div>
      )}
      {canReceiveReturn && (
        <div className="order-actions">
          <button type="button" className="btn-primary" data-testid="complete-return-btn" disabled={returnBusy} onClick={handleCompleteReturn}>
            {t('orderDetail.receiveReturn')}
          </button>
        </div>
      )}
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
      {(order.discount ?? 0) > 0 && (
        <div data-testid="order-discount">
          {t('checkout.discount')}{order.couponCode ? ` (${order.couponCode})` : ''}: −{formatCurrency(order.discount ?? 0, order.currency)}
        </div>
      )}
      {order.shippingFee !== undefined && (
        <div data-testid="order-shipping">
          {t('checkout.shipping')}{order.deliverySpeed ? ` (${t(`checkout.delivery.${order.deliverySpeed}`)})` : ''}: {order.shippingFee === 0 ? t('checkout.free') : formatCurrency(order.shippingFee, order.currency)}
        </div>
      )}
      {(order.giftWrapFee ?? 0) > 0 && (
        <div data-testid="order-gift-wrap">{t('checkout.giftWrapLine')}: {formatCurrency(order.giftWrapFee ?? 0, order.currency)}</div>
      )}
      <div>{t('orderDetail.tax')}: {formatCurrency(order.tax, order.currency)}</div>
      <div>{t('orderDetail.total')}: <strong data-testid="order-total">{formatCurrency(order.total, order.currency)}</strong></div>
      {order.estimatedDelivery && IN_TRANSIT.includes(order.status) && (
        <div className="order-eta" data-testid="order-eta">{t('orders.arriving', { date: formatDay(order.estimatedDelivery) })}</div>
      )}
      {order.giftMessage && (
        <div className="gift-message" data-testid="order-gift-message">{t('orderDetail.giftMessage', { message: order.giftMessage })}</div>
      )}
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
            <div data-testid="trace-eta">{t('trace.eta')}: {formatDay(trace.estimatedDelivery)}</div>
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
