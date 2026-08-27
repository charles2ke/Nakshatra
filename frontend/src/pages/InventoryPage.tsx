import { useState, useEffect, useCallback } from 'react';
import { getInventory, getReorderSuggestions, getInventoryForecast, replenishInventory, adjustInventory } from '../api/client';
import type { InventoryItem, InventoryForecast } from '../api/client';
import { usePersona } from '../context/PersonaContext';
import { useLocale } from '../i18n/LocaleContext';

export default function InventoryPage() {
  const { t, formatNumber, formatDate } = useLocale();
  const { currentUser } = usePersona();
  const [items, setItems] = useState<InventoryItem[]>([]);
  const [suggestions, setSuggestions] = useState<InventoryForecast[]>([]);
  const [forecast, setForecast] = useState<InventoryForecast | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [stock, reorder] = await Promise.all([getInventory(), getReorderSuggestions()]);
      setItems(stock);
      setSuggestions(reorder);
    } catch (e) {
      setError(String(e));
    }
    setLoading(false);
  }, []);

  useEffect(() => { load(); }, [load]);

  async function handleForecast(productId: string) {
    setError('');
    try { setForecast(await getInventoryForecast(productId)); }
    catch (e) { setError(String(e)); }
  }

  async function handleReplenish(item: InventoryItem, quantity: number) {
    setError('');
    try {
      const updated = await replenishInventory(item.productId, quantity);
      setItems(prev => prev.map(i => i.productId === updated.productId ? updated : i));
      setMessage(t('inventory.replenished', { qty: String(quantity), product: item.productId }));
    } catch (e) { setError(String(e)); }
  }

  async function handleReceive(item: InventoryItem, quantity: number) {
    setError('');
    try {
      const updated = await adjustInventory(item.productId, quantity, 'goods-received');
      setItems(prev => prev.map(i => i.productId === updated.productId ? updated : i));
      setMessage(t('inventory.received', { qty: String(quantity), product: item.productId }));
    } catch (e) { setError(String(e)); }
  }

  if (!currentUser) return <div data-testid="inventory-no-user">{t('inventory.noUser')}</div>;

  return (
    <div data-testid="inventory-page">
      <h1>{t('inventory.title')}</h1>
      {error && <div className="error" data-testid="inventory-error">{error}</div>}
      {message && <div data-testid="inventory-message">{message}</div>}
      {loading && <div>{t('inventory.loading')}</div>}

      <h2>{t('inventory.reorderTitle')}</h2>
      <div data-testid="reorder-suggestions" className="shipments-board">
        {suggestions.length === 0 && !loading && <div data-testid="reorder-empty">{t('inventory.reorderEmpty')}</div>}
        {suggestions.map(s => (
          <div key={s.productId} className="shipment-card" data-testid={`reorder-${s.productId}`}>
            <div><strong>{s.productId}</strong></div>
            <div>{t('inventory.daysOfCover')}: {formatNumber(s.daysOfCoverRemaining)}</div>
            <div>{t('inventory.reorderPoint')}: {formatNumber(Math.round(s.reorderPoint))}</div>
            <div>{t('inventory.recommendedQty')}: <span data-testid={`reorder-qty-${s.productId}`}>{formatNumber(s.recommendedOrderQuantity)}</span></div>
            <button data-testid={`replenish-${s.productId}`} onClick={() => {
              const item = items.find(i => i.productId === s.productId);
              if (item) handleReplenish(item, s.recommendedOrderQuantity);
            }}>{t('inventory.replenish')}</button>
          </div>
        ))}
      </div>

      <h2>{t('inventory.stockTitle')}</h2>
      <table className="setup-table" data-testid="inventory-table">
        <thead>
          <tr>
            <th>{t('inventory.col.product')}</th>
            <th>{t('inventory.col.warehouse')}</th>
            <th>{t('inventory.col.onHand')}</th>
            <th>{t('inventory.col.reserved')}</th>
            <th>{t('inventory.col.available')}</th>
            <th>{t('inventory.col.onOrder')}</th>
            <th>{t('inventory.col.leadTime')}</th>
            <th>{t('inventory.col.actions')}</th>
          </tr>
        </thead>
        <tbody>
          {items.map(i => (
            <tr key={i.productId} data-testid={`inventory-row-${i.productId}`}>
              <td>{i.productId}</td>
              <td>{i.warehouse}</td>
              <td data-testid={`on-hand-${i.productId}`}>{formatNumber(i.onHand)}</td>
              <td>{formatNumber(i.reserved)}</td>
              <td>{formatNumber(i.available)}</td>
              <td data-testid={`on-order-${i.productId}`}>{formatNumber(i.onOrder)}</td>
              <td>{t('inventory.days', { days: String(i.leadTimeDays) })}</td>
              <td>
                <button data-testid={`forecast-${i.productId}`} onClick={() => handleForecast(i.productId)}>{t('inventory.forecast')}</button>
                <button data-testid={`receive-${i.productId}`} onClick={() => handleReceive(i, 10)}>{t('inventory.receive')}</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {forecast && (
        <div className="invoice-card" data-testid="forecast-panel">
          <h2>{t('inventory.forecastTitle', { product: forecast.productId })}</h2>
          <div>{t('inventory.avgDailyDemand')}: {formatNumber(Math.round(forecast.averageDailyDemand * 100) / 100)}</div>
          <div>{t('inventory.forecastDemand', { days: String(forecast.horizonDays) })}: {formatNumber(Math.round(forecast.forecastDemand))}</div>
          <div>{t('inventory.safetyStock')}: {formatNumber(Math.round(forecast.safetyStock))}</div>
          <div>{t('inventory.eoq')}: <span data-testid="forecast-eoq">{formatNumber(Math.round(forecast.economicOrderQuantity))}</span></div>
          <div>{t('inventory.reorderNow')}: {forecast.reorderNow ? t('common.yes') : t('common.no')}</div>
          <ul data-testid="forecast-series">
            {forecast.dailyForecast.slice(0, 7).map(p => (
              <li key={p.date}>{formatDate(p.date)}: {formatNumber(Math.round(p.forecastUnits * 10) / 10)}</li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
