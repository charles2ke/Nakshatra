import { useState, useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import { searchProducts, addToCart } from '../api/client';
import type { SearchResult, Product } from '../api/client';
import ProductCard from '../components/ProductCard';
import { usePersona } from '../context/PersonaContext';
import { useLocale } from '../i18n/LocaleContext';

export default function SearchPage() {
  const { currentUser } = usePersona();
  const { t, formatNumber } = useLocale();
  const [searchParams, setSearchParams] = useSearchParams();
  const [result, setResult] = useState<SearchResult | null>(null);
  const [loading, setLoading] = useState(false);

  const q = searchParams.get('q') || '';
  const category = searchParams.get('category') || '';
  const minPrice = searchParams.get('minPrice') || '';
  const maxPrice = searchParams.get('maxPrice') || '';

  async function doSearch() {
    setLoading(true);
    try {
      const r = await searchProducts({
        q: q || undefined,
        category: category || undefined,
        minPrice: minPrice ? Number(minPrice) : undefined,
        maxPrice: maxPrice ? Number(maxPrice) : undefined,
      });
      setResult(r);
    } catch { /* ignore */ }
    setLoading(false);
  }

  useEffect(() => { doSearch(); }, [q, category, minPrice, maxPrice]);

  function commitOnEnter(key: string) {
    return (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter') setParam(key, e.currentTarget.value);
    };
  }

  function setParam(key: string, val: string) {
    const p = new URLSearchParams(searchParams);
    if (val) p.set(key, val); else p.delete(key);
    setSearchParams(p);
  }

  return (
    <div data-testid="search-page">
      <h1>{t('search.title')}</h1>
      <div className="search-filters" data-testid="search-filters">
        <input data-testid="search-q" aria-label={t('search.keywords')} placeholder={t('search.keywords')} defaultValue={q} onBlur={e => setParam('q', e.target.value)} onKeyDown={commitOnEnter('q')} />
        <input data-testid="search-category" aria-label={t('search.category')} placeholder={t('search.category')} defaultValue={category} onBlur={e => setParam('category', e.target.value)} onKeyDown={commitOnEnter('category')} />
        <input data-testid="search-min-price" aria-label={t('search.minPrice')} placeholder={t('search.minPrice')} type="number" defaultValue={minPrice} onBlur={e => setParam('minPrice', e.target.value)} onKeyDown={commitOnEnter('minPrice')} />
        <input data-testid="search-max-price" aria-label={t('search.maxPrice')} placeholder={t('search.maxPrice')} type="number" defaultValue={maxPrice} onBlur={e => setParam('maxPrice', e.target.value)} onKeyDown={commitOnEnter('maxPrice')} />
        <button onClick={doSearch} data-testid="search-apply-btn">{t('search.apply')}</button>
      </div>
      {loading && (
        <div className="skeleton-grid" data-testid="search-loading" role="status" aria-label={t('home.loading')}>
          {Array.from({ length: 8 }, (_, i) => <div key={i} className="skeleton-card" />)}
        </div>
      )}
      {!loading && result && result.items.length === 0 && (
        <div className="empty-state" data-testid="search-empty">{t('search.noResults')}</div>
      )}
      <div data-testid="search-results" className="product-grid">
        {!loading && result?.items.map(p => (
          <ProductCard key={p.id} product={p} onAddToCart={currentUser ? async (p: Product) => { await addToCart(currentUser.id, p.id, 1); } : undefined} />
        ))}
      </div>
      {result && <div data-testid="search-total">{t('search.total')}: {formatNumber(result.total)}</div>}
    </div>
  );
}
