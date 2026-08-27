import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { getCart, removeFromCart } from '../api/client';
import type { Cart } from '../api/client';
import { usePersona } from '../context/PersonaContext';

export default function CartPage() {
  const { currentUser } = usePersona();
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

  if (!currentUser) return <div>Please select a user.</div>;

  return (
    <div data-testid="cart-page">
      <h1>Your Cart</h1>
      {!cart || cart.items.length === 0 ? (
        <div data-testid="cart-empty">Your cart is empty.</div>
      ) : (
        <>
          <table className="cart-table" data-testid="cart-items">
            <thead><tr><th>Product</th><th>Price</th><th>Qty</th><th>Action</th></tr></thead>
            <tbody>
              {cart.items.map(item => (
                <tr key={item.productId} data-testid={`cart-item-${item.productId}`}>
                  <td>{item.name}</td>
                  <td>{item.price.toFixed(2)}</td>
                  <td>{item.quantity}</td>
                  <td><button data-testid={`remove-${item.productId}`} onClick={() => handleRemove(item.productId)}>Remove</button></td>
                </tr>
              ))}
            </tbody>
          </table>
          <div data-testid="cart-subtotal">Subtotal: {cart.subtotal.toFixed(2)}</div>
          <button data-testid="checkout-btn" onClick={() => navigate('/checkout')} className="btn-primary">Checkout</button>
        </>
      )}
    </div>
  );
}
