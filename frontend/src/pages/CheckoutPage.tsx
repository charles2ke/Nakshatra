import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { getCart, createOrder, createPayment } from '../api/client';
import type { Cart, PaymentMethod } from '../api/client';
import { usePersona } from '../context/PersonaContext';

export default function CheckoutPage() {
  const { currentUser } = usePersona();
  const navigate = useNavigate();
  const [cart, setCart] = useState<Cart | null>(null);
  const [address, setAddress] = useState('');
  const [method, setMethod] = useState<PaymentMethod>('Card');
  const [cardNumber, setCardNumber] = useState('');
  const [cardHolder, setCardHolder] = useState('');
  const [expiry, setExpiry] = useState('');
  const [cvv, setCvv] = useState('');
  const [upiId, setUpiId] = useState('');
  const [error, setError] = useState('');
  const [step, setStep] = useState<'address'|'payment'>('address');
  const [orderId, setOrderId] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (currentUser) getCart(currentUser.id).then(setCart).catch(() => {});
  }, [currentUser]);

  async function handlePlaceOrder() {
    if (!currentUser || !cart) return;
    setLoading(true);
    try {
      const order = await createOrder({
        userId: currentUser.id,
        items: cart.items.map(i => ({ productId: i.productId, quantity: i.quantity })),
        shippingAddress: address,
      });
      setOrderId(order.id);
      setStep('payment');
    } catch (e) { setError(String(e)); }
    setLoading(false);
  }

  async function handlePay() {
    if (!currentUser || !cart) return;
    setLoading(true);
    try {
      await createPayment({
        orderId,
        userId: currentUser.id,
        amount: cart.subtotal,
        currency: 'INR',
        method,
        cardNumber: method === 'Card' ? cardNumber : undefined,
        cardHolder: method === 'Card' ? cardHolder : undefined,
        expiry: method === 'Card' ? expiry : undefined,
        cvv: method === 'Card' ? cvv : undefined,
      });
      navigate(`/orders/${orderId}`);
    } catch (e) { setError(String(e)); }
    setLoading(false);
  }

  if (!currentUser) return <div>Please select a user.</div>;
  if (!cart || cart.items.length === 0) return <div data-testid="checkout-empty">Cart is empty. <a href="/cart">Go to cart</a></div>;

  return (
    <div data-testid="checkout-page">
      <h1>Checkout</h1>
      {error && <div className="error" data-testid="checkout-error">{error}</div>}

      {step === 'address' && (
        <div data-testid="checkout-address-step">
          <h2>Shipping Address</h2>
          <textarea data-testid="checkout-address" value={address} onChange={e => setAddress(e.target.value)} placeholder="Enter shipping address" rows={3} />
          <div>Subtotal: <strong data-testid="checkout-subtotal">{cart.subtotal.toFixed(2)}</strong></div>
          <button data-testid="place-order-btn" onClick={handlePlaceOrder} disabled={!address || loading}>
            {loading ? 'Placing…' : 'Place Order'}
          </button>
        </div>
      )}

      {step === 'payment' && (
        <div data-testid="checkout-payment-step">
          <h2>Payment</h2>
          <select data-testid="payment-method" value={method} onChange={e => setMethod(e.target.value as PaymentMethod)}>
            <option value="Card">Card</option>
            <option value="UPI">UPI</option>
            <option value="NetBanking">Net Banking</option>
          </select>
          {method === 'Card' && (
            <div data-testid="card-fields">
              <input data-testid="card-number" placeholder="Card Number" value={cardNumber} onChange={e => setCardNumber(e.target.value)} />
              <input data-testid="card-holder" placeholder="Card Holder" value={cardHolder} onChange={e => setCardHolder(e.target.value)} />
              <input data-testid="card-expiry" placeholder="MM/YY" value={expiry} onChange={e => setExpiry(e.target.value)} />
              <input data-testid="card-cvv" placeholder="CVV" value={cvv} onChange={e => setCvv(e.target.value)} />
            </div>
          )}
          {method === 'UPI' && (
            <input data-testid="upi-id" placeholder="UPI ID" value={upiId} onChange={e => setUpiId(e.target.value)} />
          )}
          <button data-testid="pay-btn" onClick={handlePay} disabled={loading}>
            {loading ? 'Processing…' : 'Pay Now'}
          </button>
        </div>
      )}
    </div>
  );
}
