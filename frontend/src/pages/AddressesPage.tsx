import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { getAddresses, createAddress, updateAddress, setDefaultAddress, deleteAddress, errorMessage } from '../api/client';
import type { Address, AddressInput } from '../api/client';
import { usePersona } from '../context/PersonaContext';
import { useLocale } from '../i18n/LocaleContext';
import { useToast } from '../hooks/useToast';

const EMPTY: AddressInput = {
  fullName: '', line1: '', line2: '', city: '', state: '', postalCode: '', country: '', phone: '', isDefault: false,
};
const FIELDS: { key: Exclude<keyof AddressInput, 'isDefault'>; testId: string; required?: boolean; autoComplete: string }[] = [
  { key: 'fullName', testId: 'address-full-name', required: true, autoComplete: 'name' },
  { key: 'line1', testId: 'address-line1', required: true, autoComplete: 'address-line1' },
  { key: 'line2', testId: 'address-line2', autoComplete: 'address-line2' },
  { key: 'city', testId: 'address-city', required: true, autoComplete: 'address-level2' },
  { key: 'state', testId: 'address-state', autoComplete: 'address-level1' },
  { key: 'postalCode', testId: 'address-postal-code', required: true, autoComplete: 'postal-code' },
  { key: 'country', testId: 'address-country', required: true, autoComplete: 'country-name' },
  { key: 'phone', testId: 'address-phone', autoComplete: 'tel' },
];

export default function AddressesPage() {
  const { currentUser } = usePersona();
  const { t } = useLocale();
  const { toast, showToast } = useToast();
  // Remembers whose book it is, so switching persona never shows the previous user's addresses.
  const [loaded, setLoaded] = useState<{ userId: string; addresses: Address[] | null } | null>(null);
  const [form, setForm] = useState<AddressInput | null>(null);
  const [editing, setEditing] = useState<Address | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const userId = currentUser?.id;

  useEffect(() => {
    if (!userId) return;
    let current = true;
    getAddresses(userId)
      .then(addresses => { if (current) setLoaded({ userId, addresses }); })
      .catch(() => { if (current) setLoaded({ userId, addresses: null }); });
    return () => { current = false; };
  }, [userId]);

  if (!currentUser) return <div>{t('cart.noUser')}</div>;

  const ready = loaded?.userId === currentUser.id;
  const addresses = ready ? loaded.addresses : null;

  async function reload(forUser: string) {
    try { setLoaded({ userId: forUser, addresses: await getAddresses(forUser) }); } catch { /* keep the current list */ }
  }

  function openForm(address: Address | null) {
    setEditing(address);
    setError('');
    setForm(address ? {
      fullName: address.fullName, line1: address.line1, line2: address.line2, city: address.city, state: address.state,
      postalCode: address.postalCode, country: address.country, phone: address.phone, isDefault: address.isDefault,
    } : { ...EMPTY });
  }

  function closeForm() {
    setForm(null);
    setEditing(null);
    setError('');
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!currentUser || !form) return;
    setBusy(true);
    setError('');
    try {
      if (editing) await updateAddress(currentUser.id, editing.id, form);
      else await createAddress(currentUser.id, form);
      showToast(editing ? t('addresses.updated') : t('addresses.added'));
      closeForm();
      await reload(currentUser.id);
    } catch (err) { setError(errorMessage(err)); }
    finally { setBusy(false); }
  }

  async function handleAction(action: () => Promise<unknown>, doneKey: string) {
    if (!currentUser) return;
    setBusy(true);
    try {
      await action();
      showToast(t(doneKey));
      await reload(currentUser.id);
    } catch (err) { showToast(errorMessage(err)); }
    finally { setBusy(false); }
  }

  return (
    <div data-testid="addresses-page">
      <h1>{t('addresses.title')}</h1>
      <div className="toast-region" role="status" aria-live="polite">
        {toast && <div className="toast" data-testid="addresses-toast">{toast}</div>}
      </div>
      {!form && (
        <button className="btn-primary" data-testid="address-add" onClick={() => openForm(null)}>{t('addresses.add')}</button>
      )}
      {form && (
        <form className="address-form" data-testid="address-form" onSubmit={handleSubmit} aria-labelledby="address-form-title">
          <h2 id="address-form-title">{editing ? t('addresses.editTitle') : t('addresses.addTitle')}</h2>
          {error && <div className="error" role="alert" data-testid="address-error">{error}</div>}
          <div className="address-fields">
            {FIELDS.map(f => (
              <label key={f.key}>
                {t(`addresses.field.${f.key}`)}{f.required && <span aria-hidden="true"> *</span>}
                <input
                  data-testid={f.testId}
                  value={form[f.key]}
                  required={f.required}
                  maxLength={200}
                  autoComplete={f.autoComplete}
                  onChange={e => setForm(prev => prev && { ...prev, [f.key]: e.target.value })}
                />
              </label>
            ))}
          </div>
          <label className="checkbox-label">
            <input
              type="checkbox"
              data-testid="address-is-default"
              checked={form.isDefault}
              // The default can be moved to another address but not simply switched off.
              disabled={editing?.isDefault}
              onChange={e => setForm(prev => prev && { ...prev, isDefault: e.target.checked })}
            />
            {t('addresses.makeDefault')}
          </label>
          <div className="row-actions">
            <button type="submit" className="btn-primary" data-testid="address-submit" disabled={busy}>
              {editing ? t('addresses.save') : t('addresses.addSubmit')}
            </button>
            <button type="button" className="btn-secondary" data-testid="address-cancel" onClick={closeForm}>{t('addresses.cancel')}</button>
          </div>
        </form>
      )}
      {!ready && <div data-testid="addresses-loading">{t('addresses.loading')}</div>}
      {ready && !addresses && <div className="error" data-testid="addresses-error">{t('addresses.loadFailed')}</div>}
      {addresses && addresses.length === 0 && <div className="empty-state" data-testid="addresses-empty">{t('addresses.empty')}</div>}
      {addresses && addresses.length > 0 && (
        <ul className="address-list" data-testid="address-list">
          {addresses.map(a => (
            <li key={a.id} className={a.isDefault ? 'address-card default' : 'address-card'} data-testid={`address-${a.id}`}>
              {a.isDefault && <span className="default-badge" data-testid={`address-default-${a.id}`}>{t('addresses.default')}</span>}
              <strong>{a.fullName}</strong>
              <div>{a.line1}</div>
              {a.line2 && <div>{a.line2}</div>}
              <div>{[a.city, a.state, a.postalCode].filter(Boolean).join(', ')}</div>
              <div>{a.country}</div>
              {a.phone && <div>{t('addresses.phone', { phone: a.phone })}</div>}
              <div className="row-actions">
                <button className="btn-secondary btn-small" data-testid={`address-edit-${a.id}`} disabled={busy} onClick={() => openForm(a)}>
                  {t('addresses.edit')}
                </button>
                {!a.isDefault && (
                  <button
                    className="btn-secondary btn-small"
                    data-testid={`address-set-default-${a.id}`}
                    disabled={busy}
                    onClick={() => handleAction(() => setDefaultAddress(currentUser.id, a.id), 'addresses.defaultSet')}
                  >{t('addresses.setDefault')}</button>
                )}
                <button
                  className="btn-secondary btn-small"
                  data-testid={`address-delete-${a.id}`}
                  disabled={busy}
                  onClick={() => {
                    if (editing?.id === a.id) closeForm();
                    handleAction(() => deleteAddress(currentUser.id, a.id), 'addresses.deleted');
                  }}
                >{t('addresses.delete')}</button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
