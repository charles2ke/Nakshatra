import { useState, useEffect } from 'react';
import { getProducts, createProduct, updateProduct, deleteProduct } from '../../api/client';
import type { Product } from '../../api/client';

const EMPTY: Omit<Product,'id'> = { name:'', description:'', category:'', price:0, currency:'INR', stock:0, vendorId:'', imageUrl:'', tags:[] };

export default function SetupProductsPage() {
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
      <h1>Products Setup</h1>
      {error && <div className="error">{error}</div>}
      <form onSubmit={handleSubmit} data-testid="product-form" className="setup-form">
        <input data-testid="product-form-name" placeholder="Name" required {...field('name')} />
        <input data-testid="product-form-description" placeholder="Description" {...field('description')} />
        <input data-testid="product-form-category" placeholder="Category" {...field('category')} />
        <input data-testid="product-form-price" placeholder="Price" type="number" step="0.01" {...field('price')} />
        <input data-testid="product-form-stock" placeholder="Stock" type="number" {...field('stock')} />
        <input data-testid="product-form-vendor-id" placeholder="Vendor ID" {...field('vendorId')} />
        <input data-testid="product-form-image-url" placeholder="Image URL" {...field('imageUrl')} />
        <button type="submit" data-testid="product-form-submit">{editId ? 'Update' : 'Create'}</button>
        {editId && <button type="button" onClick={() => { setEditId(null); setForm(EMPTY); }}>Cancel</button>}
      </form>
      <table className="setup-table" data-testid="products-table">
        <thead><tr><th>Name</th><th>Category</th><th>Price</th><th>Stock</th><th>Actions</th></tr></thead>
        <tbody>
          {products.map(p => (
            <tr key={p.id} data-testid={`product-row-${p.id}`}>
              <td>{p.name}</td><td>{p.category}</td><td>{p.price}</td><td>{p.stock}</td>
              <td>
                <button data-testid={`edit-product-${p.id}`} onClick={() => { setEditId(p.id); const {id,...rest}=p; setForm(rest); }}>Edit</button>
                <button data-testid={`delete-product-${p.id}`} onClick={() => handleDelete(p.id)}>Delete</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
