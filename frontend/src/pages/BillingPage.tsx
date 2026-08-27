import { useState, useEffect } from 'react';
import { getInvoices } from '../api/client';
import type { Invoice } from '../api/client';
import { usePersona } from '../context/PersonaContext';

export default function BillingPage() {
  const { currentUser } = usePersona();
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!currentUser) return;
    setLoading(true);
    getInvoices(currentUser.id).then(setInvoices).catch(() => {}).finally(() => setLoading(false));
  }, [currentUser]);

  if (!currentUser) return <div>Please select a user.</div>;

  return (
    <div data-testid="billing-page">
      <h1>Invoices</h1>
      {loading && <div>Loading…</div>}
      {!loading && invoices.length === 0 && <div data-testid="invoices-empty">No invoices found.</div>}
      <div data-testid="invoices-list">
        {invoices.map(inv => (
          <div key={inv.id} className="invoice-card" data-testid={`invoice-${inv.id}`}>
            <div>Invoice #{inv.id.slice(0,8)}</div>
            <div>Order: {inv.orderId.slice(0,8)}</div>
            <div>Total: {inv.currency} {inv.total.toFixed(2)}</div>
            <div>Issued: {new Date(inv.issuedAt).toLocaleDateString()}</div>
            <div>Paid: <span data-testid={`invoice-paid-${inv.id}`}>{inv.paid ? 'Yes' : 'No'}</span></div>
          </div>
        ))}
      </div>
    </div>
  );
}
