import { useState, useEffect } from 'react';
import { searchProducts, addToCart } from '../api/client';
import type { Product, SearchResult } from '../api/client';
import ProductCard from '../components/ProductCard';
import RecommendationStrip from '../components/RecommendationStrip';
import { usePersona } from '../context/PersonaContext';
import { useLocale } from '../i18n/LocaleContext';

export default function HomePage() {
  const { currentUser } = usePersona();
  const { t } = useLocale();
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
    if (!currentUser) { setToast(t('home.addToCartNoUser')); return; }
    try {
      const res = await addToCart(currentUser.id, p.id, 1);
      setRecs(res.recommendations ?? []);
      setToast(t('home.addedToCart', { name: p.name }));
      setTimeout(() => setToast(''), 3000);
    } catch { setToast(t('home.addToCartFailed')); }
  }

  return (
    <div data-testid="home-page">
      <h1 data-testid="home-title">{t('home.title')}</h1>
      <div className="search-bar">
        <input
          data-testid="home-search-input"
          placeholder={t('home.searchPlaceholder')}
          value={query}
          onChange={e => setQuery(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && doSearch(query)}
        />
        <button data-testid="home-search-btn" onClick={() => doSearch(query)}>{t('home.searchBtn')}</button>
      </div>
      {toast && <div className="toast" data-testid="cart-toast">{toast}</div>}
      {loading && <div data-testid="loading">{t('home.loading')}</div>}
      <div className="product-grid" data-testid="product-grid">
        {result?.items.map(p => (
          <ProductCard key={p.id} product={p} onAddToCart={handleAddToCart} />
        ))}
      </div>
      <RecommendationStrip products={recs} />
    </div>
  );
}
