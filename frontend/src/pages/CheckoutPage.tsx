import { useState, useEffect } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { getCart, getAddresses, quoteOrder, createOrder, createPayment, formatAddress, errorMessage } from '../api/client';
import type { Address, Cart, CheckoutQuote, CheckoutRequest, DeliverySpeed, Order, PaymentMethod } from '../api/client';
import { usePersona } from '../context/PersonaContext';
import { useBadges } from '../context/BadgeContext';
import { useLocale } from '../i18n/LocaleContext';

const SPEEDS: DeliverySpeed[] = ['Standard', 'Expedited', 'OneDay'];
const MAX_BUY_NOW_QUANTITY = 10;
const MAX_GIFT_MESSAGE = 250;
const NEW_ADDRESS = 'new';

export default function CheckoutPage() {
  const { currentUser } = usePersona();
  const { t, formatCurrency, formatDay, formatNumber } = useLocale();
  const { refreshBadges } = useBadges();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  // "Buy Now" checks out a single product straight from its page and leaves the cart alone.
  const buyNowId = searchParams.get('buyNow');
  const buyNowQuantity = Math.min(MAX_BUY_NOW_QUANTITY, Math.max(1, Math.floor(Number(searchParams.get('qty'))) || 1));

  const [cart, setCart] = useState<Cart | null>(null);
  // Remembers whose address book it is, so switching persona never offers someone else's addresses.
  const [addressBook, setAddressBook] = useState<{ userId: string; addresses: Address[] } | null>(null);
  const [selectedAddressId, setSelectedAddressId] = useState<string | null>(null);
  const [address, setAddress] = useState('');
  const [deliverySpeed, setDeliverySpeed] = useState<DeliverySpeed>('Standard');
  const [couponInput, setCouponInput] = useState('');
  const [couponCode, setCouponCode] = useState('');
  const [giftWrap, setGiftWrap] = useState(false);
  const [giftMessage, setGiftMessage] = useState('');
  // A quote remembers which request it prices, so a slow reply never overwrites a newer one.
  const [quoteState, setQuoteState] = useState<{ key: string; quote: CheckoutQuote | null; error: string } | null>(null);

  const [method, setMethod] = useState<PaymentMethod>('Card');
  const [cardNumber, setCardNumber] = useState('');
  const [cardHolder, setCardHolder] = useState('');
  const [expiry, setExpiry] = useState('');
  const [cvv, setCvv] = useState('');
  const [upiId, setUpiId] = useState('');
  const [error, setError] = useState('');
  // Once placed, the order (not the cart) is what gets paid.
  const [order, setOrder] = useState<Order | null>(null);
  const [loading, setLoading] = useState(false);

  const userId = currentUser?.id;
  useEffect(() => {
    if (userId && !buyNowId) getCart(userId).then(setCart).catch(() => {});
  }, [userId, buyNowId]);

  useEffect(() => {
    if (!userId) return;
    let current = true;
    getAddresses(userId)
      .then(addresses => { if (current) setAddressBook({ userId, addresses }); })
      .catch(() => { if (current) setAddressBook({ userId, addresses: [] }); });
    return () => { current = false; };
  }, [userId]);

  const items = buyNowId
    ? [{ productId: buyNowId, quantity: buyNowQuantity }]
    : (cart?.items ?? []).map(i => ({ productId: i.productId, quantity: i.quantity }));
  const quoteRequest: CheckoutRequest | null = items.length
    ? { items, couponCode: couponCode || undefined, deliverySpeed, giftWrap, buyNow: buyNowId ? true : undefined }
    : null;
  const quoteKey = quoteRequest ? JSON.stringify(quoteRequest) : '';

  useEffect(() => {
    if (!quoteKey) return;
    let current = true;
    quoteOrder(JSON.parse(quoteKey) as CheckoutRequest)
      .then(quote => { if (current) setQuoteState({ key: quoteKey, quote, error: '' }); })
      .catch(e => { if (current) setQuoteState({ key: quoteKey, quote: null, error: errorMessage(e) }); });
    return () => { current = false; };
  }, [quoteKey]);

  const addresses = addressBook && addressBook.userId === userId ? addressBook.addresses : [];
  const selectedAddress = addresses.find(a => a.id === selectedAddressId)
    ?? (selectedAddressId === NEW_ADDRESS ? undefined : addresses.find(a => a.isDefault) ?? addresses[0]);
  const shippingAddress = selectedAddress ? formatAddress(selectedAddress) : address.trim();

  const quoteCurrent = quoteState?.key === quoteKey;
  // While a new quote loads, keep showing the last one (dimmed) rather than an empty summary.
  const quote = quoteState?.quote ?? null;
  const currency = quote?.currency ?? 'USD';

  function applyCoupon(e: FormEvent) {
    e.preventDefault();
    setCouponCode(couponInput.trim().toUpperCase());
  }

  function removeCoupon() {
    setCouponCode('');
    setCouponInput('');
  }

  async function handlePlaceOrder() {
    if (!currentUser || !quoteRequest) return;
    setError('');
    setLoading(true);
    // A code the current quote already rejected would only fail the order, so leave it off.
    const couponRejected = quoteCurrent && !!quote?.couponError;
    try {
      const placed = await createOrder({
        ...quoteRequest,
        couponCode: couponRejected ? undefined : quoteRequest.couponCode,
        giftMessage: giftMessage.trim() || undefined,
        userId: currentUser.id,
        shippingAddress,
      });
      setOrder(placed);
      // Placing the order removes its items from the cart on the server.
      refreshBadges();
    } catch (e) { setError(errorMessage(e)); }
    setLoading(false);
  }

  async function handlePay() {
    if (!currentUser || !order) return;
    setError('');
    setLoading(true);
    try {
      await createPayment({
        orderId: order.id,
        userId: currentUser.id,
        amount: order.total,
        currency: order.currency,
        method,
        cardNumber: method === 'Card' ? cardNumber : undefined,
        cardHolder: method === 'Card' ? cardHolder : undefined,
        expiry: method === 'Card' ? expiry : undefined,
        cvv: method === 'Card' ? cvv : undefined,
      });
      navigate(`/orders/${order.id}`);
    } catch (e) { setError(errorMessage(e)); }
    setLoading(false);
  }

  if (!currentUser) return <div>{t('checkout.noUser')}</div>;
  if (!order && !buyNowId && (!cart || cart.items.length === 0)) return (
    <div data-testid="checkout-empty">{t('checkout.cartEmpty')} <Link to="/cart" data-testid="checkout-go-to-cart">{t('checkout.goToCart')}</Link></div>
  );

  const discounted = quote ? quote.subtotal - quote.discount : 0;
  const summaryLines = quote?.items ?? cart?.items ?? [];

  return (
    <div data-testid="checkout-page">
      <h1>{t('checkout.title')}</h1>
      {error && <div className="error" role="alert" data-testid="checkout-error">{error}</div>}

      {!order && (
        <div className="checkout-layout" data-testid="checkout-address-step">
          <div className="checkout-steps">
            {buyNowId && <div className="buy-now-notice" data-testid="buy-now-notice">{t('checkout.buyNowNotice')}</div>}

            <section className="checkout-step" aria-labelledby="checkout-shipping-title">
              <h2 id="checkout-shipping-title">{t('checkout.shippingTitle')}</h2>
              {addresses.length > 0 && (
                <fieldset className="choice-list" data-testid="checkout-address-options">
                  <legend className="sr-only">{t('checkout.chooseAddress')}</legend>
                  {addresses.map(a => (
                    <label key={a.id} className="choice">
                      <input
                        type="radio"
                        name="checkout-address"
                        data-testid={`checkout-address-${a.id}`}
                        checked={selectedAddress?.id === a.id}
                        onChange={() => setSelectedAddressId(a.id)}
                      />
                      <span>
                        <strong>{a.fullName}</strong> {formatAddress({ ...a, fullName: '' })}
                        {a.isDefault && <span className="default-badge">{t('addresses.default')}</span>}
                      </span>
                    </label>
                  ))}
                  <label className="choice">
                    <input
                      type="radio"
                      name="checkout-address"
                      data-testid="address-option-new"
                      checked={!selectedAddress}
                      onChange={() => setSelectedAddressId(NEW_ADDRESS)}
                    />
                    <span>{t('checkout.newAddress')}</span>
                  </label>
                </fieldset>
              )}
              {!selectedAddress && (
                <textarea
                  data-testid="checkout-address"
                  aria-label={t('checkout.addressPlaceholder')}
                  value={address}
                  onChange={e => setAddress(e.target.value)}
                  placeholder={t('checkout.addressPlaceholder')}
                  rows={3}
                />
              )}
              <Link to="/addresses" className="section-link" data-testid="checkout-manage-addresses">{t('checkout.manageAddresses')}</Link>
            </section>

            <section className="checkout-step" aria-labelledby="checkout-delivery-title">
              <h2 id="checkout-delivery-title">{t('checkout.deliveryTitle')}</h2>
              <fieldset className="choice-list" data-testid="delivery-options">
                <legend className="sr-only">{t('checkout.deliveryTitle')}</legend>
                {SPEEDS.map(speed => {
                  const option = quote?.deliveryOptions.find(o => o.speed === speed);
                  return (
                    <label key={speed} className="choice">
                      <input
                        type="radio"
                        name="delivery-speed"
                        data-testid={`delivery-${speed}`}
                        checked={deliverySpeed === speed}
                        onChange={() => setDeliverySpeed(speed)}
                      />
                      <span>
                        <strong>{t(`checkout.delivery.${speed}`)}</strong>
                        {option && <> · {option.fee === 0 ? t('checkout.free') : formatCurrency(option.fee, currency)}</>}
                        {option && <span className="choice-note" data-testid={`delivery-eta-${speed}`}>{t('checkout.arrives', { date: formatDay(option.estimatedDelivery) })}</span>}
                      </span>
                    </label>
                  );
                })}
              </fieldset>
            </section>

            <section className="checkout-step" aria-labelledby="checkout-gift-title">
              <h2 id="checkout-gift-title">{t('checkout.giftTitle')}</h2>
              <label className="checkbox-label">
                <input type="checkbox" data-testid="gift-wrap" checked={giftWrap} onChange={e => setGiftWrap(e.target.checked)} />
                {t('checkout.giftWrap')}
              </label>
              <label className="field-label" htmlFor="gift-message">{t('checkout.giftMessage')}</label>
              <textarea
                id="gift-message"
                data-testid="gift-message"
                rows={2}
                maxLength={MAX_GIFT_MESSAGE}
                value={giftMessage}
                onChange={e => setGiftMessage(e.target.value)}
                aria-describedby="gift-message-count"
              />
              <div id="gift-message-count" className="choice-note">
                {t('checkout.giftMessageCount', { count: formatNumber(giftMessage.length), max: formatNumber(MAX_GIFT_MESSAGE) })}
              </div>
            </section>

            <section className="checkout-step" aria-labelledby="checkout-coupon-title">
              <h2 id="checkout-coupon-title">{t('checkout.couponTitle')}</h2>
              <form className="coupon-form" onSubmit={applyCoupon}>
                <input
                  data-testid="coupon-input"
                  aria-label={t('checkout.couponLabel')}
                  placeholder={t('checkout.couponLabel')}
                  value={couponInput}
                  onChange={e => setCouponInput(e.target.value)}
                />
                <button type="submit" className="btn-secondary" data-testid="coupon-apply" disabled={!couponInput.trim()}>{t('checkout.couponApply')}</button>
              </form>
              {quoteCurrent && quote?.couponError && <div className="error" role="alert" data-testid="coupon-error">{quote.couponError}</div>}
              {quoteCurrent && quote?.couponCode && (
                <div className="coupon-applied" data-testid="coupon-applied">
                  {t('checkout.couponApplied', { code: quote.couponCode })}
                  <button type="button" className="btn-link" data-testid="coupon-remove" onClick={removeCoupon}>{t('checkout.couponRemove')}</button>
                </div>
              )}
            </section>
          </div>

          <aside className={quoteCurrent ? 'checkout-summary-card' : 'checkout-summary-card stale'} data-testid="checkout-quote" aria-busy={!quoteCurrent}>
            <h2>{t('checkout.summaryTitle')}</h2>
            <ul className="summary-items">
              {summaryLines.map(i => (
                <li key={i.productId} data-testid={`checkout-line-${i.productId}`}>
                  <span>{t('checkout.lineItem', { name: i.name, qty: formatNumber(i.quantity) })}</span>
                  {quote && <span>{formatCurrency(i.price * i.quantity, currency)}</span>}
                </li>
              ))}
            </ul>
            {quoteState?.error && quoteCurrent && <div className="error" role="alert" data-testid="quote-error">{quoteState.error}</div>}
            {quote && (
              <dl className="summary-lines">
                <dt>{t('checkout.subtotal')}</dt><dd data-testid="checkout-subtotal">{formatCurrency(quote.subtotal, currency)}</dd>
                {quote.discount > 0 && <><dt>{t('checkout.discount')}</dt><dd data-testid="quote-discount">−{formatCurrency(quote.discount, currency)}</dd></>}
                <dt>{t('checkout.shipping')}</dt><dd data-testid="quote-shipping">{quote.shippingFee === 0 ? t('checkout.free') : formatCurrency(quote.shippingFee, currency)}</dd>
                {quote.giftWrapFee > 0 && <><dt>{t('checkout.giftWrapLine')}</dt><dd data-testid="quote-gift-wrap">{formatCurrency(quote.giftWrapFee, currency)}</dd></>}
                <dt>{t('checkout.tax')}</dt><dd data-testid="quote-tax">{formatCurrency(quote.tax, currency)}</dd>
                <dt className="summary-total">{t('checkout.total')}</dt><dd className="summary-total" data-testid="quote-total">{formatCurrency(quote.total, currency)}</dd>
              </dl>
            )}
            {quote && quote.deliverySpeed === 'Standard' && (
              <div className="free-shipping-hint" data-testid="free-shipping-hint">
                {quote.shippingFee === 0
                  ? t('checkout.freeShippingQualified')
                  : t('checkout.freeShippingAddMore', { amount: formatCurrency(Math.max(0, quote.freeShippingThreshold - discounted), currency) })}
              </div>
            )}
            {quote && <div className="choice-note" data-testid="checkout-eta">{t('checkout.arrives', { date: formatDay(quote.estimatedDelivery) })}</div>}
            <button data-testid="place-order-btn" className="btn-buy-now" onClick={handlePlaceOrder} disabled={!shippingAddress || loading || !quoteRequest}>
              {loading ? t('checkout.placing') : t('checkout.placeOrder')}
            </button>
          </aside>
        </div>
      )}

      {order && (
        <div data-testid="checkout-payment-step">
          <h2>{t('checkout.paymentTitle')}</h2>
          <div className="checkout-summary" data-testid="checkout-summary">
            <div>{t('checkout.subtotal')}: {formatCurrency(order.subtotal, order.currency)}</div>
            {(order.discount ?? 0) > 0 && (
              <div data-testid="checkout-order-discount">
                {t('checkout.discount')}{order.couponCode ? ` (${order.couponCode})` : ''}: −{formatCurrency(order.discount ?? 0, order.currency)}
              </div>
            )}
            {order.shippingFee !== undefined && (
              <div data-testid="checkout-order-shipping">
                {t('checkout.shipping')}: {order.shippingFee === 0 ? t('checkout.free') : formatCurrency(order.shippingFee, order.currency)}
              </div>
            )}
            {(order.giftWrapFee ?? 0) > 0 && (
              <div data-testid="checkout-order-gift-wrap">{t('checkout.giftWrapLine')}: {formatCurrency(order.giftWrapFee ?? 0, order.currency)}</div>
            )}
            <div>{t('checkout.tax')}: {formatCurrency(order.tax, order.currency)}</div>
            <div>{t('checkout.total')}: <strong data-testid="checkout-order-total">{formatCurrency(order.total, order.currency)}</strong></div>
            {order.estimatedDelivery && <div data-testid="checkout-order-eta">{t('checkout.arrives', { date: formatDay(order.estimatedDelivery) })}</div>}
          </div>
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
