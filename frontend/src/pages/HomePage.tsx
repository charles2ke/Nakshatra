import { useState, useEffect } from 'react';
import { searchProducts, addToCart } from '../api/client';
import type { Product, SearchResult } from '../api/client';
import ProductCard from '../components/ProductCard';
import RecommendationStrip from '../components/RecommendationStrip';
import SuggestInput from '../components/SuggestInput';
import { usePersona } from '../context/PersonaContext';
import { useBadges } from '../context/BadgeContext';
import { useLocale } from '../i18n/LocaleContext';
import { useToast } from '../hooks/useToast';

export default function HomePage() {
  const { currentUser } = usePersona();
  const { t } = useLocale();
  const { syncCart } = useBadges();
  const [query, setQuery] = useState('');
  const [result, setResult] = useState<SearchResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [recs, setRecs] = useState<Product[]>([]);
  const { toast, showToast } = useToast();

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
      syncCart(res.cart);
      showToast(t('home.addedToCart', { name: p.name }));
    } catch { showToast(t('home.addToCartFailed')); }
  }

  return (
    <div data-testid="home-page">
      <h1 data-testid="home-title">{t('home.title')}</h1>
      <form className="search-bar" role="search" onSubmit={e => { e.preventDefault(); doSearch(query); }}>
        <SuggestInput
          testId="home-search-input"
          type="search"
          aria-label={t('home.searchPlaceholder')}
          placeholder={t('home.searchPlaceholder')}
          value={query}
          onChange={setQuery}
          onSelect={s => { setQuery(s); doSearch(s); }}
        />
        <button type="submit" data-testid="home-search-btn">{t('home.searchBtn')}</button>
      </form>
      <div className="toast-region" role="status" aria-live="polite">
        {toast && <div className="toast" data-testid="cart-toast">{toast}</div>}
      </div>
      {loading && (
        <div className="skeleton-grid" data-testid="loading" role="status" aria-label={t('home.loading')}>
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
