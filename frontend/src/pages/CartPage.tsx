import { useState, useEffect, useLayoutEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { getCart, removeFromCart, updateCartItem, saveForLater, moveSavedToCart, removeSaved, errorMessage } from '../api/client';
import type { Cart } from '../api/client';
import { usePersona } from '../context/PersonaContext';
import { useBadges } from '../context/BadgeContext';
import { useLocale } from '../i18n/LocaleContext';
import { useToast } from '../hooks/useToast';

export default function CartPage() {
  const { currentUser } = usePersona();
  return <CartContents key={currentUser ? `${currentUser.id}:${currentUser.persona}` : ''} />;
}

function CartContents() {
  const { currentUser } = usePersona();
  const { t, formatCurrency, formatNumber } = useLocale();
  const { syncCart } = useBadges();
  const [cart, setCart] = useState<Cart | null>(null);
  const mutationPending = useRef(false);
  const [busy, setBusy] = useState(false);
  const active = useRef(false);
  const version = useRef(0);
  const navigate = useNavigate();
  const { toast, showToast } = useToast();

  useLayoutEffect(() => {
    active.current = true;
    return () => { active.current = false; };
  }, []);

  useEffect(() => {
    const readVersion = version.current;
    let current = true;
    if (currentUser) getCart(currentUser.id).then(c => {
      if (current && version.current === readVersion) { setCart(c); syncCart(c); }
    }).catch(() => {});
    return () => { current = false; };
  }, [currentUser, syncCart]);

  async function mutate(action: (userId: string) => Promise<Cart>, doneKey?: string) {
    if (!currentUser || mutationPending.current) return;
    mutationPending.current = true;
    version.current++;
    setBusy(true);
    try {
      const updated = await action(currentUser.id);
      if (!active.current) return;
      setCart(updated);
      syncCart(updated);
      if (doneKey) showToast(t(doneKey));
    } catch (e) { if (active.current) showToast(errorMessage(e)); }
    finally {
      mutationPending.current = false;
      if (active.current) setBusy(false);
    }
  }

  function handleQuantity(productId: string, quantity: number) {
    if (quantity >= 1) void mutate(userId => updateCartItem(userId, productId, quantity));
  }

  function handleMove(productId: string, move: (userId: string, productId: string) => Promise<Cart>, doneKey: string) {
    void mutate(userId => move(userId, productId), doneKey);
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
                  return (
                    <tr key={item.productId} data-testid={`cart-item-${item.productId}`}>
                      <td>{item.name}</td>
                      <td>{formatCurrency(item.price, 'INR')}</td>
                      <td>
                        <div className="cart-qty">
                          <button
                            data-testid={`qty-decrease-${item.productId}`}
                            onClick={() => handleQuantity(item.productId, item.quantity - 1)}
                            disabled={item.quantity <= 1 || busy}
                            aria-label={t('cart.decrease')}
                          >-</button>
                          <span data-testid={`qty-${item.productId}`}>{item.quantity}</span>
                          <button
                            data-testid={`qty-increase-${item.productId}`}
                            onClick={() => handleQuantity(item.productId, item.quantity + 1)}
                            disabled={busy}
                            aria-label={t('cart.increase')}
                          >+</button>
                        </div>
                      </td>
                      <td>
                        <div className="row-actions">
                          <button data-testid={`remove-${item.productId}`} disabled={busy} onClick={() => mutate(userId => removeFromCart(userId, item.productId))}>{t('cart.remove')}</button>
                          <button
                            className="btn-secondary"
                            data-testid={`save-for-later-${item.productId}`}
                            disabled={busy}
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
          <button data-testid="checkout-btn" disabled={busy} onClick={() => navigate('/checkout')} className="btn-primary">{t('cart.checkout')}</button>
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
                  disabled={busy}
                  onClick={() => handleMove(item.productId, moveSavedToCart, 'cart.movedToast')}
                >{t('cart.moveToCart')}</button>
                <button
                  className="btn-secondary"
                  data-testid={`remove-saved-${item.productId}`}
                  disabled={busy}
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
