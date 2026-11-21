import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { getCart, removeFromCart, updateCartItem, saveForLater, moveSavedToCart, removeSaved, errorMessage } from '../api/client';
import type { Cart } from '../api/client';
import { usePersona } from '../context/PersonaContext';
import { useBadges } from '../context/BadgeContext';
import { useLocale } from '../i18n/LocaleContext';
import { useToast } from '../hooks/useToast';

export default function CartPage() {
  const { currentUser } = usePersona();
  const { t, formatCurrency, formatNumber } = useLocale();
  const { syncCart } = useBadges();
  const [cart, setCart] = useState<Cart | null>(null);
  const pendingQuantityItems = useRef(new Set<string>());
  const [pendingQuantityProductIds, setPendingQuantityProductIds] = useState<Set<string>>(() => new Set());
  const navigate = useNavigate();
  const { toast, showToast } = useToast();
  const [movingProductId, setMovingProductId] = useState<string | null>(null);

  useEffect(() => {
    if (currentUser) getCart(currentUser.id).then(c => { setCart(c); syncCart(c); }).catch(() => {});
  }, [currentUser, syncCart]);

  async function handleQuantity(productId: string, quantity: number) {
    if (!currentUser || quantity < 1 || pendingQuantityItems.current.has(productId)) return;
    pendingQuantityItems.current.add(productId);
    setPendingQuantityProductIds(new Set(pendingQuantityItems.current));
    try {
      const updated = await updateCartItem(currentUser.id, productId, quantity);
      setCart(updated);
      syncCart(updated);
    } catch { /* keep the current cart on failure */ }
    finally {
      pendingQuantityItems.current.delete(productId);
      setPendingQuantityProductIds(new Set(pendingQuantityItems.current));
    }
  }

  async function handleRemove(productId: string) {
    if (!currentUser) return;
    const updated = await removeFromCart(currentUser.id, productId);
    setCart(updated);
    syncCart(updated);
  }

  // Moves an item between the cart and the saved-for-later list; one move at a time.
  async function handleMove(productId: string, move: (userId: string, productId: string) => Promise<Cart>, doneKey: string) {
    if (!currentUser || movingProductId) return;
    setMovingProductId(productId);
    try {
      const updated = await move(currentUser.id, productId);
      setCart(updated);
      syncCart(updated);
      showToast(t(doneKey));
    } catch (e) { showToast(errorMessage(e)); }
    finally { setMovingProductId(null); }
  }

  if (!currentUser) return <div>{t('cart.noUser')}</div>;

  const saved = cart?.savedForLater ?? [];

  return (
    <div data-testid="cart-page">
      <h1>{t('cart.title')}</h1>
      {!cart || cart.items.length === 0 ? (
        <div className="empty-state" data-testid="cart-empty">{t('cart.empty')}</div>
      ) : (
        <>
          <div className="table-scroll">
            <table className="cart-table" data-testid="cart-items">
              <thead><tr><th>{t('cart.col.product')}</th><th>{t('cart.col.price')}</th><th>{t('cart.col.qty')}</th><th>{t('cart.col.action')}</th></tr></thead>
              <tbody>
                {cart.items.map(item => {
                  const isQuantityPending = pendingQuantityProductIds.has(item.productId);
                  return (
                    <tr key={item.productId} data-testid={`cart-item-${item.productId}`}>
                      <td>{item.name}</td>
                      <td>{formatCurrency(item.price, 'INR')}</td>
                      <td>
                        <div className="cart-qty">
                          <button
                            data-testid={`qty-decrease-${item.productId}`}
                            onClick={() => handleQuantity(item.productId, item.quantity - 1)}
                            disabled={item.quantity <= 1 || isQuantityPending}
                            aria-label={t('cart.decrease')}
                          >-</button>
                          <span data-testid={`qty-${item.productId}`}>{item.quantity}</span>
                          <button
                            data-testid={`qty-increase-${item.productId}`}
                            onClick={() => handleQuantity(item.productId, item.quantity + 1)}
                            disabled={isQuantityPending}
                            aria-label={t('cart.increase')}
                          >+</button>
                        </div>
                      </td>
                      <td>
                        <div className="row-actions">
                          <button data-testid={`remove-${item.productId}`} onClick={() => handleRemove(item.productId)}>{t('cart.remove')}</button>
                          <button
                            className="btn-secondary"
                            data-testid={`save-for-later-${item.productId}`}
                            disabled={movingProductId !== null}
                            onClick={() => handleMove(item.productId, saveForLater, 'cart.savedToast')}
                          >{t('cart.saveForLater')}</button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          <div id="cart-subtotal" data-testid="cart-subtotal">{t('cart.subtotal')}: {formatCurrency(cart.subtotal, 'INR')}</div>
          <button data-testid="checkout-btn" onClick={() => navigate('/checkout')} className="btn-primary">{t('cart.checkout')}</button>
        </>
      )}
      <div className="toast-region" role="status" aria-live="polite">
        {toast && <div className="toast" data-testid="cart-page-toast">{toast}</div>}
      </div>
      {saved.length > 0 && (
        <section className="saved-for-later" data-testid="saved-for-later" aria-labelledby="saved-for-later-title">
          <h2 id="saved-for-later-title">{t('cart.savedTitle', { count: formatNumber(saved.length) })}</h2>
          <ul className="saved-list">
            {saved.map(item => (
              <li key={item.productId} className="saved-item" data-testid={`saved-item-${item.productId}`}>
                <span className="saved-name">{item.name}</span>
                <span>{formatCurrency(item.price, 'INR')}</span>
                <button
                  className="btn-primary"
                  data-testid={`move-to-cart-${item.productId}`}
                  disabled={movingProductId !== null}
                  onClick={() => handleMove(item.productId, moveSavedToCart, 'cart.movedToast')}
                >{t('cart.moveToCart')}</button>
                <button
                  className="btn-secondary"
                  data-testid={`remove-saved-${item.productId}`}
                  disabled={movingProductId !== null}
                  onClick={() => handleMove(item.productId, removeSaved, 'cart.removedSavedToast')}
                >{t('cart.remove')}</button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
