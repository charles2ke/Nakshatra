import { useState, useEffect } from 'react';
import { getProducts, createProduct, updateProduct, deleteProduct } from '../../api/client';
import type { Product } from '../../api/client';
import { useLocale } from '../../i18n/LocaleContext';

const EMPTY: Omit<Product,'id'> = { name:'', description:'', category:'', price:0, currency:'INR', stock:0, vendorId:'', imageUrl:'', tags:[] };

export default function SetupProductsPage() {
  const { t } = useLocale();
  const [products, setProducts] = useState<Product[]>([]);
  const [form, setForm] = useState<Omit<Product,'id'>>(EMPTY);
  const [editId, setEditId] = useState<string|null>(null);
  const [error, setError] = useState('');

  async function load() { try { setProducts(await getProducts()); } catch { /* ignore */ } }
  useEffect(() => { load(); }, []);

  function field(k: keyof typeof form) {
    return { value: String((form as Record<string,unknown>)[k] ?? ''), onChange: (e: React.ChangeEvent<HTMLInputElement>) => setForm(f => ({ ...f, [k]: k === 'price' || k === 'stock' ? Number(e.target.value) : e.target.value })) };
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      if (editId) { await updateProduct(editId, form); } else { await createProduct(form); }
      setForm(EMPTY); setEditId(null); load();
    } catch (e) { setError(String(e)); }
  }

  async function handleDelete(id: string) {
    try { await deleteProduct(id); load(); } catch (e) { setError(String(e)); }
  }

  return (
    <div data-testid="setup-products-page">
      <h1>{t('setup.products.title')}</h1>
      {error && <div className="error">{error}</div>}
      <form onSubmit={handleSubmit} data-testid="product-form" className="setup-form">
        <input data-testid="product-form-name" placeholder={t('setup.products.form.name')} required {...field('name')} />
        <input data-testid="product-form-description" placeholder={t('setup.products.form.description')} {...field('description')} />
        <input data-testid="product-form-category" placeholder={t('setup.products.form.category')} {...field('category')} />
        <input data-testid="product-form-price" placeholder={t('setup.products.form.price')} type="number" step="0.01" {...field('price')} />
        <input data-testid="product-form-stock" placeholder={t('setup.products.form.stock')} type="number" {...field('stock')} />
        <input data-testid="product-form-vendor-id" placeholder={t('setup.products.form.vendorId')} {...field('vendorId')} />
        <input data-testid="product-form-image-url" placeholder={t('setup.products.form.imageUrl')} {...field('imageUrl')} />
        <button type="submit" data-testid="product-form-submit">{editId ? t('setup.products.update') : t('setup.products.create')}</button>
        {editId && <button type="button" onClick={() => { setEditId(null); setForm(EMPTY); }}>{t('setup.products.cancel')}</button>}
      </form>
      <table className="setup-table" data-testid="products-table">
        <thead><tr>
          <th>{t('setup.products.col.name')}</th>
          <th>{t('setup.products.col.category')}</th>
          <th>{t('setup.products.col.price')}</th>
          <th>{t('setup.products.col.stock')}</th>
          <th>{t('setup.products.col.actions')}</th>
        </tr></thead>
        <tbody>
          {products.map(p => (
            <tr key={p.id} data-testid={`product-row-${p.id}`}>
              <td>{p.name}</td><td>{p.category}</td><td>{p.price}</td><td>{p.stock}</td>
              <td>
                <button data-testid={`edit-product-${p.id}`} onClick={() => { setEditId(p.id); const {id,...rest}=p; setForm(rest); }}>{t('setup.products.edit')}</button>
                <button data-testid={`delete-product-${p.id}`} onClick={() => handleDelete(p.id)}>{t('setup.products.delete')}</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
