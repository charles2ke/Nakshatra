import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { getCart, createOrder, createPayment } from '../api/client';
import type { Cart, PaymentMethod } from '../api/client';
import { usePersona } from '../context/PersonaContext';
import { useLocale } from '../i18n/LocaleContext';

export default function CheckoutPage() {
  const { currentUser } = usePersona();
  const { t, formatCurrency } = useLocale();
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

  if (!currentUser) return <div>{t('checkout.noUser')}</div>;
  if (!cart || cart.items.length === 0) return (
    <div data-testid="checkout-empty">{t('checkout.cartEmpty')} <a href="/cart">{t('checkout.goToCart')}</a></div>
  );

  return (
    <div data-testid="checkout-page">
      <h1>{t('checkout.title')}</h1>
      {error && <div className="error" data-testid="checkout-error">{error}</div>}

      {step === 'address' && (
        <div data-testid="checkout-address-step">
          <h2>{t('checkout.shippingTitle')}</h2>
          <textarea data-testid="checkout-address" value={address} onChange={e => setAddress(e.target.value)} placeholder={t('checkout.addressPlaceholder')} rows={3} />
          <div>{t('checkout.subtotal')}: <strong data-testid="checkout-subtotal">{formatCurrency(cart.subtotal, 'INR')}</strong></div>
          <button data-testid="place-order-btn" onClick={handlePlaceOrder} disabled={!address || loading}>
            {loading ? t('checkout.placing') : t('checkout.placeOrder')}
          </button>
        </div>
      )}

      {step === 'payment' && (
        <div data-testid="checkout-payment-step">
          <h2>{t('checkout.paymentTitle')}</h2>
          <select data-testid="payment-method" value={method} onChange={e => setMethod(e.target.value as PaymentMethod)}>
            <option value="Card">{t('checkout.method.card')}</option>
            <option value="UPI">{t('checkout.method.upi')}</option>
            <option value="NetBanking">{t('checkout.method.netbanking')}</option>
          </select>
          {method === 'Card' && (
            <div data-testid="card-fields">
              <input data-testid="card-number" placeholder={t('checkout.card.number')} value={cardNumber} onChange={e => setCardNumber(e.target.value)} />
              <input data-testid="card-holder" placeholder={t('checkout.card.holder')} value={cardHolder} onChange={e => setCardHolder(e.target.value)} />
              <input data-testid="card-expiry" placeholder={t('checkout.card.expiry')} value={expiry} onChange={e => setExpiry(e.target.value)} />
              <input data-testid="card-cvv" placeholder={t('checkout.card.cvv')} value={cvv} onChange={e => setCvv(e.target.value)} />
            </div>
          )}
          {method === 'UPI' && (
            <input data-testid="upi-id" placeholder={t('checkout.upi.id')} value={upiId} onChange={e => setUpiId(e.target.value)} />
          )}
          <button data-testid="pay-btn" onClick={handlePay} disabled={loading}>
            {loading ? t('checkout.paying') : t('checkout.payNow')}
          </button>
        </div>
      )}
    </div>
  );
}
