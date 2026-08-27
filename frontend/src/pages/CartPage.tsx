import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { getCart, removeFromCart } from '../api/client';
import type { Cart } from '../api/client';
import { usePersona } from '../context/PersonaContext';
import { useLocale } from '../i18n/LocaleContext';

export default function CartPage() {
  const { currentUser } = usePersona();
  const { t, formatCurrency } = useLocale();
  const [cart, setCart] = useState<Cart | null>(null);
  const navigate = useNavigate();

  useEffect(() => {
    if (currentUser) getCart(currentUser.id).then(setCart).catch(() => {});
  }, [currentUser]);

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
              {cart.items.map(item => (
                <tr key={item.productId} data-testid={`cart-item-${item.productId}`}>
                  <td>{item.name}</td>
                  <td>{formatCurrency(item.price, 'INR')}</td>
                  <td>{item.quantity}</td>
                  <td><button data-testid={`remove-${item.productId}`} onClick={() => handleRemove(item.productId)}>{t('cart.remove')}</button></td>
                </tr>
              ))}
            </tbody>
          </table>
          <div data-testid="cart-subtotal">{t('cart.subtotal')}: {formatCurrency(cart.subtotal, 'INR')}</div>
          <button data-testid="checkout-btn" onClick={() => navigate('/checkout')} className="btn-primary">{t('cart.checkout')}</button>
        </>
      )}
    </div>
  );
}
