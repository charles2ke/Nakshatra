import { useState, useEffect } from 'react';
import { searchProducts, addToCart } from '../api/client';
import type { Product, SearchResult } from '../api/client';
import ProductCard from '../components/ProductCard';
import RecommendationStrip from '../components/RecommendationStrip';
import { usePersona } from '../context/PersonaContext';

export default function HomePage() {
  const { currentUser } = usePersona();
  const [query, setQuery] = useState('');
  const [result, setResult] = useState<SearchResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [recs, setRecs] = useState<Product[]>([]);
  const [toast, setToast] = useState('');

  async function doSearch(q: string) {
    setLoading(true);
    try {
      const r = await searchProducts({ q, pageSize: 12 });
      setResult(r);
    } catch { /* ignore */ }
    setLoading(false);
  }

  useEffect(() => { doSearch(''); }, []);

  async function handleAddToCart(p: Product) {
    if (!currentUser) { setToast('Please select a user first'); return; }
    try {
      const res = await addToCart(currentUser.id, p.id, 1);
      setRecs(res.recommendations ?? []);
      setToast(`Added "${p.name}" to cart`);
      setTimeout(() => setToast(''), 3000);
    } catch { setToast('Failed to add to cart'); }
  }

  return (
    <div data-testid="home-page">
      <h1>Nakshatra Shop</h1>
      <div className="search-bar">
        <input
          data-testid="home-search-input"
          placeholder="Search products…"
          value={query}
          onChange={e => setQuery(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && doSearch(query)}
        />
        <button data-testid="home-search-btn" onClick={() => doSearch(query)}>Search</button>
      </div>
      {toast && <div className="toast" data-testid="cart-toast">{toast}</div>}
      {loading && <div data-testid="loading">Loading…</div>}
      <div className="product-grid" data-testid="product-grid">
        {result?.items.map(p => (
          <ProductCard key={p.id} product={p} onAddToCart={handleAddToCart} />
        ))}
      </div>
      <RecommendationStrip products={recs} />
    </div>
  );
}
