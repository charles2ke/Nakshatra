import { useState, useEffect } from 'react';
import { getShipments, advanceShipment, getAllOrders, completeReturn, errorMessage } from '../api/client';
import type { Order, Shipment } from '../api/client';
import { useLocale } from '../i18n/LocaleContext';
import { useToast } from '../hooks/useToast';

export default function FulfillmentPage() {
  const { t } = useLocale();
  const [shipments, setShipments] = useState<Shipment[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [returns, setReturns] = useState<Order[]>([]);
  const [receivingId, setReceivingId] = useState<string | null>(null);
  const { toast, showToast } = useToast();

  async function load() {
    setLoading(true);
    try { setShipments(await getShipments()); } catch { /* ignore */ }
    setLoading(false);
  }

  useEffect(() => { load(); }, []);

  useEffect(() => {
    let current = true;
    getAllOrders()
      .then(orders => { if (current) setReturns(orders.filter(o => o.status === 'ReturnRequested')); })
      .catch(() => {});
    return () => { current = false; };
  }, []);

  async function handleReceiveReturn(orderId: string) {
    setError('');
    setReceivingId(orderId);
    try {
      await completeReturn(orderId);
      setReturns(prev => prev.filter(o => o.id !== orderId));
      showToast(t('fulfillment.returnReceived', { order: orderId.slice(0, 8) }));
    } catch (e) { setError(errorMessage(e)); }
    finally { setReceivingId(null); }
  }

  async function handleAdvance(orderId: string) {
    setError('');
    try {
      const updated = await advanceShipment(orderId);
      setShipments(prev => prev.map(s => s.orderId === orderId ? updated : s));
    } catch (e) {
      // e.g. the order is unpaid or was cancelled; reload so the board shows the current state.
      setError(errorMessage(e));
      load();
    }
  }

  return (
    <div data-testid="fulfillment-page">
      <h1>{t('fulfillment.title')}</h1>
      {error && <div className="error" role="alert" data-testid="fulfillment-error">{error}</div>}
      <div className="toast-region" role="status" aria-live="polite">
        {toast && <div className="toast" data-testid="fulfillment-toast">{toast}</div>}
      </div>
      {loading && <div>{t('fulfillment.loading')}</div>}
      <div data-testid="shipments-list" className="shipments-board">
        {shipments.map(s => (
          <div key={s.orderId} className={`shipment-card status-${s.status.toLowerCase()}`} data-testid={`shipment-${s.orderId}`}>
            <div>{t('fulfillment.order')}: {s.orderId.slice(0,8)}</div>
            <div>{t('fulfillment.status')}: <span data-testid={`shipment-status-${s.orderId}`}>{s.status}</span></div>
            <div>{t('fulfillment.carrier')}: {s.carrier}</div>
            <div>{t('fulfillment.tracking')}: {s.trackingNumber}</div>
            {s.status !== 'Delivered' && s.status !== 'Cancelled' && (
              <button data-testid={`advance-${s.orderId}`} onClick={() => handleAdvance(s.orderId)}>
                {t('fulfillment.advance')}
              </button>
            )}
          </div>
        ))}
      </div>
      <section className="returns-section" data-testid="returns-section" aria-labelledby="returns-title">
        <h2 id="returns-title">{t('fulfillment.returnsTitle')}</h2>
        {returns.length === 0 ? (
          <div className="empty-state" data-testid="returns-empty">{t('fulfillment.returnsEmpty')}</div>
        ) : (
          <div className="shipments-board">
            {returns.map(o => (
              <div key={o.id} className="shipment-card status-returnrequested" data-testid={`return-to-receive-${o.id}`}>
                <div>{t('fulfillment.order')}: {o.id.slice(0, 8)}</div>
                <div>{o.items.map(i => `${i.name} × ${i.quantity}`).join(', ')}</div>
                {o.returnReason && <div>{t('orderDetail.returnReasonLabel', { reason: o.returnReason })}</div>}
                <button data-testid={`receive-return-${o.id}`} disabled={receivingId !== null} onClick={() => handleReceiveReturn(o.id)}>
                  {t('fulfillment.receiveReturn')}
                </button>
              </div>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}
