import { useState, useEffect } from 'react';
import type { FormEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import { searchProducts, addToCart } from '../api/client';
import type { SearchResult, Product } from '../api/client';
import ProductCard from '../components/ProductCard';
import SuggestInput from '../components/SuggestInput';
import { usePersona } from '../context/PersonaContext';
import { useBadges } from '../context/BadgeContext';
import { useLocale } from '../i18n/LocaleContext';
import { useToast } from '../hooks/useToast';

const PAGE_SIZE = 12;
const FILTERS = ['q', 'category', 'minPrice', 'maxPrice'] as const;

export default function SearchPage() {
  const { currentUser } = usePersona();
  const { syncCart } = useBadges();
  const { t, formatNumber } = useLocale();
  const { toast, showToast } = useToast();
  const [searchParams, setSearchParams] = useSearchParams();

  const q = searchParams.get('q') || '';
  const category = searchParams.get('category') || '';
  const minPrice = searchParams.get('minPrice') || '';
  const maxPrice = searchParams.get('maxPrice') || '';
  const page = Math.max(1, Math.floor(Number(searchParams.get('page'))) || 1);
  const searchKey = `${q}|${category}|${minPrice}|${maxPrice}|${page}`;

  // Results remember which search they answer; until the current one arrives the page is loading.
  const [loaded, setLoaded] = useState<{ key: string; result: SearchResult | null }>({ key: '', result: null });
  const loading = loaded.key !== searchKey;
  const result = loaded.result;

  // The keyword box is controlled for type-ahead; follow the URL when it changes (e.g. back/forward).
  const [keywords, setKeywords] = useState(q);
  const [keywordsFor, setKeywordsFor] = useState(q);
  if (keywordsFor !== q) {
    setKeywordsFor(q);
    setKeywords(q);
  }

  useEffect(() => {
    let current = true;
    searchProducts({
      q: q || undefined,
      category: category || undefined,
      minPrice: minPrice ? Number(minPrice) : undefined,
      maxPrice: maxPrice ? Number(maxPrice) : undefined,
      page,
      pageSize: PAGE_SIZE,
    })
      .then(r => { if (current) setLoaded({ key: searchKey, result: r }); })
      .catch(() => { if (current) setLoaded({ key: searchKey, result: null }); });
    return () => { current = false; };
  }, [q, category, minPrice, maxPrice, page, searchKey]);

  function navigate(next: URLSearchParams) {
    if (next.toString() !== searchParams.toString()) setSearchParams(next);
  }

  // Changing a filter starts again from the first page.
  function setParam(key: string, val: string) {
    const p = new URLSearchParams(searchParams);
    if (val.trim()) p.set(key, val.trim()); else p.delete(key);
    if (p.get(key) !== searchParams.get(key)) p.delete('page');
    navigate(p);
  }

  function applyFilters(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const form = new FormData(e.currentTarget);
    const p = new URLSearchParams();
    for (const key of FILTERS) {
      const val = String(form.get(key) ?? '').trim();
      if (val) p.set(key, val);
    }
    navigate(p);
  }

  function goToPage(next: number) {
    const p = new URLSearchParams(searchParams);
    if (next > 1) p.set('page', String(next)); else p.delete('page');
    navigate(p);
    window.scrollTo({ top: 0 });
  }

  async function handleAddToCart(p: Product) {
    if (!currentUser) return;
    try {
      const res = await addToCart(currentUser.id, p.id, 1);
      syncCart(res.cart);
      showToast(t('home.addedToCart', { name: p.name }));
    } catch { showToast(t('home.addToCartFailed')); }
  }

  const totalPages = result ? Math.max(1, Math.ceil(result.total / (result.pageSize || PAGE_SIZE))) : 1;

  return (
    <div data-testid="search-page">
      <h1>{t('search.title')}</h1>
      <form className="search-filters" data-testid="search-filters" role="search" onSubmit={applyFilters}>
        <SuggestInput
          testId="search-q"
          name="q"
          aria-label={t('search.keywords')}
          placeholder={t('search.keywords')}
          value={keywords}
          onChange={setKeywords}
          onSelect={s => { setKeywords(s); setParam('q', s); }}
          onBlur={e => setParam('q', e.target.value)}
        />
        <input data-testid="search-category" name="category" aria-label={t('search.category')} placeholder={t('search.category')} defaultValue={category} onBlur={e => setParam('category', e.target.value)} />
        <input data-testid="search-min-price" name="minPrice" aria-label={t('search.minPrice')} placeholder={t('search.minPrice')} type="number" defaultValue={minPrice} onBlur={e => setParam('minPrice', e.target.value)} />
        <input data-testid="search-max-price" name="maxPrice" aria-label={t('search.maxPrice')} placeholder={t('search.maxPrice')} type="number" defaultValue={maxPrice} onBlur={e => setParam('maxPrice', e.target.value)} />
        <button type="submit" data-testid="search-apply-btn">{t('search.apply')}</button>
      </form>
      <div className="toast-region" role="status" aria-live="polite">
        {toast && <div className="toast" data-testid="cart-toast">{toast}</div>}
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
          <ProductCard key={p.id} product={p} onAddToCart={currentUser ? handleAddToCart : undefined} />
        ))}
      </div>
      {result && <div data-testid="search-total">{t('search.total')}: {formatNumber(result.total)}</div>}
      {result && (totalPages > 1 || page > 1) && (
        <nav className="pagination" aria-label={t('search.pages')} data-testid="search-pagination">
          <button type="button" data-testid="search-prev" disabled={loading || page <= 1} onClick={() => goToPage(page - 1)}>
            {t('search.prev')}
          </button>
          <span data-testid="search-page-indicator">
            {t('search.pageOf', { page: formatNumber(page), total: formatNumber(totalPages) })}
          </span>
          <button type="button" data-testid="search-next" disabled={loading || page >= totalPages} onClick={() => goToPage(page + 1)}>
            {t('search.next')}
          </button>
        </nav>
      )}
    </div>
  );
}
