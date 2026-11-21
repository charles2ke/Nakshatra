import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { getCoupons, createCoupon, updateCoupon, deleteCoupon, errorMessage } from '../../api/client';
import type { Coupon, CouponInput, CouponKind } from '../../api/client';
import { useLocale } from '../../i18n/LocaleContext';

// Coupons are priced in the order service's currency.
const CURRENCY = 'USD';
interface CouponForm { code: string; description: string; kind: CouponKind; value: string; minSubtotal: string; expiresOn: string; active: boolean; }
const EMPTY: CouponForm = { code: '', description: '', kind: 'Percent', value: '', minSubtotal: '', expiresOn: '', active: true };

function toInput(c: Coupon): CouponInput {
  return { description: c.description, kind: c.kind, value: c.value, minSubtotal: c.minSubtotal, expiresAt: c.expiresAt, active: c.active };
}

export default function SetupCouponsPage() {
  const { t, formatCurrency, formatDay, formatNumber } = useLocale();
  const [coupons, setCoupons] = useState<Coupon[]>([]);
  const [form, setForm] = useState<CouponForm>(EMPTY);
  const [editCode, setEditCode] = useState<string | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let current = true;
    getCoupons().then(list => { if (current) setCoupons(list); }).catch(() => {});
    return () => { current = false; };
  }, []);

  async function reload() {
    try { setCoupons(await getCoupons()); } catch { /* keep the current list */ }
  }

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError('');
    try {
      await action();
      await reload();
      return true;
    } catch (e) {
      setError(errorMessage(e));
      return false;
    } finally { setBusy(false); }
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const body: CouponInput = {
      description: form.description.trim(),
      kind: form.kind,
      value: Number(form.value),
      minSubtotal: Number(form.minSubtotal) || 0,
      // A coupon stays valid through the whole of its last day.
      expiresAt: form.expiresOn ? `${form.expiresOn}T23:59:59Z` : null,
      active: form.active,
    };
    const ok = await run(() => (editCode ? updateCoupon(editCode, body) : createCoupon({ ...body, code: form.code.trim().toUpperCase() })));
    if (ok) { setForm(EMPTY); setEditCode(null); }
  }

  function startEdit(c: Coupon) {
    setEditCode(c.id);
    setError('');
    setForm({
      code: c.id, description: c.description, kind: c.kind, value: String(c.value), minSubtotal: c.minSubtotal > 0 ? String(c.minSubtotal) : '',
      expiresOn: c.expiresAt ? c.expiresAt.slice(0, 10) : '', active: c.active,
    });
  }

  function cancelEdit() {
    setEditCode(null);
    setForm(EMPTY);
    setError('');
  }

  const set = <K extends keyof CouponForm>(key: K, value: CouponForm[K]) => setForm(f => ({ ...f, [key]: value }));

  return (
    <div data-testid="setup-coupons-page">
      <h1>{t('setup.coupons.title')}</h1>
      {error && <div className="error" role="alert" data-testid="coupon-form-error">{error}</div>}
      <form onSubmit={handleSubmit} data-testid="coupon-form" className="setup-form">
        <input
          data-testid="coupon-form-code"
          aria-label={t('setup.coupons.form.code')}
          placeholder={t('setup.coupons.form.code')}
          required
          pattern="[A-Za-z0-9]{3,20}"
          title={t('setup.coupons.form.codeHint')}
          disabled={editCode !== null}
          value={form.code}
          onChange={e => set('code', e.target.value)}
        />
        <input
          data-testid="coupon-form-description"
          aria-label={t('setup.coupons.form.description')}
          placeholder={t('setup.coupons.form.description')}
          maxLength={200}
          value={form.description}
          onChange={e => set('description', e.target.value)}
        />
        <select data-testid="coupon-form-kind" aria-label={t('setup.coupons.form.kind')} value={form.kind} onChange={e => set('kind', e.target.value as CouponKind)}>
          <option value="Percent">{t('setup.coupons.kind.Percent')}</option>
          <option value="Fixed">{t('setup.coupons.kind.Fixed')}</option>
        </select>
        <input
          data-testid="coupon-form-value"
          aria-label={t('setup.coupons.form.value')}
          placeholder={t('setup.coupons.form.value')}
          type="number"
          min="0.01"
          step="0.01"
          max={form.kind === 'Percent' ? 100 : undefined}
          required
          value={form.value}
          onChange={e => set('value', e.target.value)}
        />
        <input
          data-testid="coupon-form-min-subtotal"
          aria-label={t('setup.coupons.form.minSubtotal')}
          placeholder={t('setup.coupons.form.minSubtotal')}
          type="number"
          min="0"
          step="0.01"
          value={form.minSubtotal}
          onChange={e => set('minSubtotal', e.target.value)}
        />
        <label className="checkbox-label">
          {t('setup.coupons.form.expires')}
          <input data-testid="coupon-form-expires" type="date" value={form.expiresOn} onChange={e => set('expiresOn', e.target.value)} />
        </label>
        <label className="checkbox-label">
          <input type="checkbox" data-testid="coupon-form-active" checked={form.active} onChange={e => set('active', e.target.checked)} />
          {t('setup.coupons.form.active')}
        </label>
        <button type="submit" data-testid="coupon-form-submit" disabled={busy}>{editCode ? t('setup.coupons.update') : t('setup.coupons.create')}</button>
        {editCode && <button type="button" data-testid="coupon-form-cancel" onClick={cancelEdit}>{t('setup.coupons.cancel')}</button>}
      </form>
      <div className="table-scroll">
        <table className="setup-table" data-testid="coupons-table">
          <thead><tr>
            <th>{t('setup.coupons.col.code')}</th>
            <th>{t('setup.coupons.col.description')}</th>
            <th>{t('setup.coupons.col.discount')}</th>
            <th>{t('setup.coupons.col.minSubtotal')}</th>
            <th>{t('setup.coupons.col.expires')}</th>
            <th>{t('setup.coupons.col.status')}</th>
            <th>{t('setup.coupons.col.actions')}</th>
          </tr></thead>
          <tbody>
            {coupons.map(c => (
              <tr key={c.id} data-testid={`coupon-row-${c.id}`}>
                <td><code>{c.id}</code></td>
                <td>{c.description}</td>
                <td>{c.kind === 'Percent' ? `${formatNumber(c.value)}%` : formatCurrency(c.value, CURRENCY)}</td>
                <td>{c.minSubtotal > 0 ? formatCurrency(c.minSubtotal, CURRENCY) : '—'}</td>
                <td>{c.expiresAt ? formatDay(c.expiresAt) : t('setup.coupons.never')}</td>
                <td data-testid={`coupon-status-${c.id}`}>{c.active ? t('setup.coupons.active') : t('setup.coupons.inactive')}</td>
                <td>
                  <button
                    data-testid={`coupon-toggle-${c.id}`}
                    disabled={busy}
                    onClick={() => run(() => updateCoupon(c.id, { ...toInput(c), active: !c.active }))}
                  >{c.active ? t('setup.coupons.deactivate') : t('setup.coupons.activate')}</button>
                  <button data-testid={`coupon-edit-${c.id}`} disabled={busy} onClick={() => startEdit(c)}>{t('setup.coupons.edit')}</button>
                  <button
                    data-testid={`coupon-delete-${c.id}`}
                    disabled={busy}
                    onClick={async () => { if (await run(() => deleteCoupon(c.id)) && editCode === c.id) cancelEdit(); }}
                  >{t('setup.coupons.delete')}</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
