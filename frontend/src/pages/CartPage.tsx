import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { getCart, removeFromCart, updateCartItem } from '../api/client';
import type { Cart } from '../api/client';
import { usePersona } from '../context/PersonaContext';
import { useLocale } from '../i18n/LocaleContext';

export default function CartPage() {
  const { currentUser } = usePersona();
  const { t, formatCurrency } = useLocale();
  const [cart, setCart] = useState<Cart | null>(null);
  const pendingQuantityItems = useRef(new Set<string>());
  const [pendingQuantityProductIds, setPendingQuantityProductIds] = useState<Set<string>>(() => new Set());
  const navigate = useNavigate();

  useEffect(() => {
    if (currentUser) getCart(currentUser.id).then(setCart).catch(() => {});
  }, [currentUser]);

  async function handleQuantity(productId: string, quantity: number) {
    if (!currentUser || quantity < 1 || pendingQuantityItems.current.has(productId)) return;
    pendingQuantityItems.current.add(productId);
    setPendingQuantityProductIds(new Set(pendingQuantityItems.current));
    try {
      const updated = await updateCartItem(currentUser.id, productId, quantity);
      setCart(updated);
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
  }

  if (!currentUser) return <div>{t('cart.noUser')}</div>;

  return (
    <div data-testid="cart-page">
      <h1>{t('cart.title')}</h1>
      {!cart || cart.items.length === 0 ? (
        <div data-testid="cart-empty">{t('cart.empty')}</div>
      ) : (
        <>
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
                    </td>
                    <td><button data-testid={`remove-${item.productId}`} onClick={() => handleRemove(item.productId)}>{t('cart.remove')}</button></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          <div data-testid="cart-subtotal">{t('cart.subtotal')}: {formatCurrency(cart.subtotal, 'INR')}</div>
          <button data-testid="checkout-btn" onClick={() => navigate('/checkout')} className="btn-primary">{t('cart.checkout')}</button>
        </>
      )}
    </div>
  );
}
