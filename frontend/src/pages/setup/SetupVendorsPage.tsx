import { useState, useEffect } from 'react';
import { getVendors, createVendor, updateVendor } from '../../api/client';
import type { Vendor } from '../../api/client';
import { useLocale } from '../../i18n/LocaleContext';

const EMPTY: Omit<Vendor,'id'> = { name:'', email:'', phone:'', address:'', rating:0, active:true };

export default function SetupVendorsPage() {
  const { t } = useLocale();
  const [vendors, setVendors] = useState<Vendor[]>([]);
  const [form, setForm] = useState<Omit<Vendor,'id'>>(EMPTY);
  const [editId, setEditId] = useState<string|null>(null);
  const [error, setError] = useState('');

  async function load() { try { setVendors(await getVendors()); } catch { /* ignore */ } }
  useEffect(() => { load(); }, []);

  function field(k: keyof typeof form) {
    return { value: String((form as Record<string,unknown>)[k] ?? ''), onChange: (e: React.ChangeEvent<HTMLInputElement>) => setForm(f => ({ ...f, [k]: k === 'rating' ? Number(e.target.value) : e.target.value })) };
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      if (editId) { await updateVendor(editId, form); } else { await createVendor(form); }
      setForm(EMPTY); setEditId(null); load();
    } catch (e) { setError(String(e)); }
  }

  return (
    <div data-testid="setup-vendors-page">
      <h1>{t('setup.vendors.title')}</h1>
      {error && <div className="error">{error}</div>}
      <form onSubmit={handleSubmit} data-testid="vendor-form" className="setup-form">
        <input data-testid="vendor-form-name" placeholder={t('setup.vendors.form.name')} required {...field('name')} />
        <input data-testid="vendor-form-email" placeholder={t('setup.vendors.form.email')} type="email" {...field('email')} />
        <input data-testid="vendor-form-phone" placeholder={t('setup.vendors.form.phone')} {...field('phone')} />
        <input data-testid="vendor-form-address" placeholder={t('setup.vendors.form.address')} {...field('address')} />
        <button type="submit" data-testid="vendor-form-submit">{editId ? t('setup.vendors.update') : t('setup.vendors.create')}</button>
        {editId && <button type="button" onClick={() => { setEditId(null); setForm(EMPTY); }}>{t('setup.vendors.cancel')}</button>}
      </form>
      <table className="setup-table" data-testid="vendors-table">
        <thead><tr>
          <th>{t('setup.vendors.col.name')}</th>
          <th>{t('setup.vendors.col.email')}</th>
          <th>{t('setup.vendors.col.phone')}</th>
          <th>{t('setup.vendors.col.actions')}</th>
        </tr></thead>
        <tbody>
          {vendors.map(v => (
            <tr key={v.id} data-testid={`vendor-row-${v.id}`}>
              <td>{v.name}</td><td>{v.email}</td><td>{v.phone}</td>
              <td>
                <button data-testid={`edit-vendor-${v.id}`} onClick={() => { setEditId(v.id); const {id,...rest}=v; setForm(rest); }}>{t('setup.vendors.edit')}</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
