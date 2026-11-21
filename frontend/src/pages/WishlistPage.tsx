import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { getWishList, removeFromWishList, moveWishListItemToCart, errorMessage } from '../api/client';
import type { WishList, WishListEntry } from '../api/client';
import StockNotice from '../components/StockNotice';
import { usePersona } from '../context/PersonaContext';
import { useBadges } from '../context/BadgeContext';
import { useLocale } from '../i18n/LocaleContext';
import { useToast } from '../hooks/useToast';

export default function WishlistPage() {
  const { currentUser } = usePersona();
  return <Wishlist key={currentUser ? `${currentUser.id}:${currentUser.persona}` : ''} />;
}

function Wishlist() {
  const { currentUser } = usePersona();
  const { t, formatCurrency, formatDate } = useLocale();
  const { syncCart } = useBadges();
  const { toast, showToast } = useToast();
  // Remembers whose list it is, so switching persona never shows the previous user's list.
  const [loaded, setLoaded] = useState<{ userId: string; list: WishList | null } | null>(null);
  const [busyProductId, setBusyProductId] = useState<string | null>(null);
  const userId = currentUser?.id;
  const active = useRef(false);

  useLayoutEffect(() => {
    active.current = true;
    return () => { active.current = false; };
  }, []);

  useEffect(() => {
    if (!userId) return;
    let current = true;
    getWishList(userId)
      .then(list => { if (current) setLoaded({ userId, list }); })
      .catch(() => { if (current) setLoaded({ userId, list: null }); });
    return () => { current = false; };
  }, [userId]);

  if (!currentUser) return <div>{t('cart.noUser')}</div>;

  const ready = loaded?.userId === currentUser.id;
  const list = ready ? loaded.list : null;

  async function handleMoveToCart(item: WishListEntry) {
    if (!currentUser || busyProductId) return;
    setBusyProductId(item.productId);
    try {
      const res = await moveWishListItemToCart(currentUser.id, item.productId);
      if (!active.current) return;
      setLoaded({ userId: currentUser.id, list: res.wishList });
      syncCart(res.cart);
      showToast(t('wishlist.movedToast', { name: item.name }));
    } catch (e) { if (active.current) showToast(errorMessage(e)); }
    finally { if (active.current) setBusyProductId(null); }
  }

  async function handleRemove(item: WishListEntry) {
    if (!currentUser || busyProductId) return;
    setBusyProductId(item.productId);
    try {
      const updated = await removeFromWishList(currentUser.id, item.productId);
      if (!active.current) return;
      setLoaded({ userId: currentUser.id, list: updated });
      showToast(t('wishlist.removedToast', { name: item.name }));
    } catch (e) { if (active.current) showToast(errorMessage(e)); }
    finally { if (active.current) setBusyProductId(null); }
  }

  return (
    <div data-testid="wishlist-page">
      <h1>{t('wishlist.title')}</h1>
      <div className="toast-region" role="status" aria-live="polite">
        {toast && <div className="toast" data-testid="wishlist-toast">{toast}</div>}
      </div>
      {!ready && <div data-testid="wishlist-loading">{t('wishlist.loading')}</div>}
      {ready && !list && <div className="error" data-testid="wishlist-error">{t('wishlist.loadFailed')}</div>}
      {list && list.items.length === 0 && (
        <div className="empty-state" data-testid="wishlist-empty">
          {t('wishlist.empty')} <Link to="/search">{t('wishlist.browse')}</Link>
        </div>
      )}
      {list && list.items.length > 0 && (
        <ul className="wishlist" data-testid="wishlist-items">
          {list.items.map(item => {
            const currency = item.currency ?? 'USD';
            const outOfStock = item.stock !== null && item.stock <= 0;
            return (
              <li key={item.productId} className="wishlist-item" data-testid={`wishlist-item-${item.productId}`}>
                <img src={item.imageUrl || 'https://placehold.co/120x90'} alt="" className="wishlist-img" />
                <div className="wishlist-details">
                  <Link to={`/product/${item.productId}`} className="wishlist-name">{item.name}</Link>
                  {item.price === null ? (
                    <div className="qa-meta" data-testid={`wishlist-price-unavailable-${item.productId}`}>{t('wishlist.priceUnavailable')}</div>
                  ) : (
                    <div className="price-now" data-testid={`wishlist-price-${item.productId}`}>{formatCurrency(item.price, currency)}</div>
                  )}
                  {item.priceDropped && (
                    <div className="price-drop" data-testid={`wishlist-price-drop-${item.productId}`}>
                      {t('wishlist.priceDropped', { price: formatCurrency(item.priceWhenAdded, currency) })}
                    </div>
                  )}
                  {item.stock !== null && <StockNotice stock={item.stock} onlyWhenLow testId={`wishlist-stock-${item.productId}`} />}
                  <div className="qa-meta">{t('wishlist.addedOn', { date: formatDate(item.addedAt) })}</div>
                </div>
                <div className="row-actions">
                  <button
                    className="btn-primary"
                    data-testid={`wishlist-move-to-cart-${item.productId}`}
                    disabled={busyProductId !== null || outOfStock}
                    onClick={() => handleMoveToCart(item)}
                  >{t('wishlist.moveToCart')}</button>
                  <button
                    className="btn-secondary"
                    data-testid={`wishlist-remove-${item.productId}`}
                    disabled={busyProductId !== null}
                    onClick={() => handleRemove(item)}
                  >{t('wishlist.remove')}</button>
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
