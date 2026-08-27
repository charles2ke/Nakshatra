import { useState, useEffect } from 'react';
import { getShipments, advanceShipment } from '../api/client';
import type { Shipment } from '../api/client';

export default function FulfillmentPage() {
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
      <h1>Fulfillment Board</h1>
      {error && <div className="error">{error}</div>}
      {loading && <div>Loading…</div>}
      <div data-testid="shipments-list" className="shipments-board">
        {shipments.map(s => (
          <div key={s.orderId} className={`shipment-card status-${s.status.toLowerCase()}`} data-testid={`shipment-${s.orderId}`}>
            <div>Order: {s.orderId.slice(0,8)}</div>
            <div>Status: <span data-testid={`shipment-status-${s.orderId}`}>{s.status}</span></div>
            <div>Carrier: {s.carrier}</div>
            <div>Tracking: {s.trackingNumber}</div>
            {s.status !== 'Delivered' && (
              <button data-testid={`advance-${s.orderId}`} onClick={() => handleAdvance(s.orderId)}>
                Advance Status
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
