import { useState, useEffect, useRef, useCallback } from 'react';
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
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const showToast = useCallback((message: string) => {
    setToast(message);
    if (toastTimer.current) clearTimeout(toastTimer.current);
    toastTimer.current = setTimeout(() => setToast(''), 3000);
  }, []);

  useEffect(() => () => { if (toastTimer.current) clearTimeout(toastTimer.current); }, []);

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
    if (!currentUser) { showToast(t('home.addToCartNoUser')); return; }
    try {
      const res = await addToCart(currentUser.id, p.id, 1);
      setRecs(res.recommendations ?? []);
      showToast(t('home.addedToCart', { name: p.name }));
    } catch { showToast(t('home.addToCartFailed')); }
  }

  return (
    <div data-testid="home-page">
      <h1 data-testid="home-title">{t('home.title')}</h1>
      <div className="search-bar">
        <input
          data-testid="home-search-input"
          type="search"
          aria-label={t('home.searchPlaceholder')}
          placeholder={t('home.searchPlaceholder')}
          value={query}
          onChange={e => setQuery(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && doSearch(query)}
        />
        <button data-testid="home-search-btn" onClick={() => doSearch(query)}>{t('home.searchBtn')}</button>
      </div>
      <div className="toast-region" role="status" aria-live="polite">
        {toast && <div className="toast" data-testid="cart-toast">{toast}</div>}
      </div>
      {loading && (
        <div className="skeleton-grid" data-testid="loading" aria-label={t('home.loading')}>
          {Array.from({ length: 8 }, (_, i) => <div key={i} className="skeleton-card" />)}
        </div>
      )}
      {!loading && result && result.items.length === 0 && (
        <div className="empty-state" data-testid="home-empty">{t('home.noResults')}</div>
      )}
      {!loading && (
        <div className="product-grid" data-testid="product-grid">
          {result?.items.map(p => (
            <ProductCard key={p.id} product={p} onAddToCart={handleAddToCart} />
          ))}
        </div>
      )}
      <RecommendationStrip products={recs} />
    </div>
  );
}
