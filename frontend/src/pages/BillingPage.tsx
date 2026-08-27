import { useState, useEffect } from 'react';
import { getInvoices } from '../api/client';
import type { Invoice } from '../api/client';
import { usePersona } from '../context/PersonaContext';
import { useLocale } from '../i18n/LocaleContext';

export default function BillingPage() {
  const { currentUser } = usePersona();
  const { t, formatCurrency, formatDate } = useLocale();
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!currentUser) return;
    setLoading(true);
    getInvoices(currentUser.id).then(setInvoices).catch(() => {}).finally(() => setLoading(false));
  }, [currentUser]);

  if (!currentUser) return <div>{t('billing.noUser')}</div>;

  return (
    <div data-testid="billing-page">
      <h1>{t('billing.title')}</h1>
      {loading && <div>{t('billing.loading')}</div>}
      {!loading && invoices.length === 0 && <div data-testid="invoices-empty">{t('billing.empty')}</div>}
      <div data-testid="invoices-list">
        {invoices.map(inv => (
          <div key={inv.id} className="invoice-card" data-testid={`invoice-${inv.id}`}>
            <div>{t('billing.invoice')}{inv.id.slice(0,8)}</div>
            <div>{t('billing.order')}: {inv.orderId.slice(0,8)}</div>
            <div>{t('billing.total')}: {formatCurrency(inv.total, inv.currency)}</div>
            <div>{t('billing.issued')}: {formatDate(inv.issuedAt)}</div>
            <div>{t('billing.paid')}: <span data-testid={`invoice-paid-${inv.id}`}>{inv.paid ? t('billing.paid.yes') : t('billing.paid.no')}</span></div>
          </div>
        ))}
      </div>
    </div>
  );
}
