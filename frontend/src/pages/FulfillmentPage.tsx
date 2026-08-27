import { useState, useEffect } from 'react';
import { getShipments, advanceShipment } from '../api/client';
import type { Shipment } from '../api/client';
import { useLocale } from '../i18n/LocaleContext';

export default function FulfillmentPage() {
  const { t } = useLocale();
  const [shipments, setShipments] = useState<Shipment[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  async function load() {
    setLoading(true);
    try { setShipments(await getShipments()); } catch { /* ignore */ }
    setLoading(false);
  }

  useEffect(() => { load(); }, []);

  async function handleAdvance(orderId: string) {
    try {
      const updated = await advanceShipment(orderId);
      setShipments(prev => prev.map(s => s.orderId === orderId ? updated : s));
    } catch (e) { setError(String(e)); }
  }

  return (
    <div data-testid="fulfillment-page">
      <h1>{t('fulfillment.title')}</h1>
      {error && <div className="error">{error}</div>}
      {loading && <div>{t('fulfillment.loading')}</div>}
      <div data-testid="shipments-list" className="shipments-board">
        {shipments.map(s => (
          <div key={s.orderId} className={`shipment-card status-${s.status.toLowerCase()}`} data-testid={`shipment-${s.orderId}`}>
            <div>{t('fulfillment.order')}: {s.orderId.slice(0,8)}</div>
            <div>{t('fulfillment.status')}: <span data-testid={`shipment-status-${s.orderId}`}>{s.status}</span></div>
            <div>{t('fulfillment.carrier')}: {s.carrier}</div>
            <div>{t('fulfillment.tracking')}: {s.trackingNumber}</div>
            {s.status !== 'Delivered' && (
              <button data-testid={`advance-${s.orderId}`} onClick={() => handleAdvance(s.orderId)}>
                {t('fulfillment.advance')}
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
