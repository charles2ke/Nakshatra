import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import path from 'path';
import {
  mockAllApis, mockAddressBook, injectCustomerPersona, injectAdminPersona, injectFulfillmentPersona, setPersonaAndReload,
  ADDRESSES, DEAL_PRODUCT, ORDER, ORDER_STATUS, PRODUCTS, TRACE,
} from './helpers';

const ss = (name: string) => path.join('e2e', 'screenshots', `${name}.png`);

// Dates are asserted as text, so render them in one time zone wherever the suite runs.
test.use({ timezoneId: 'UTC' });

/** The next request for exactly this API path, e.g. `apiCall(page, 'POST', '/api/orders')`. */
const apiCall = (page: Page, method: string, pathname: string) =>
  page.waitForRequest(r => r.method() === method && new URL(r.url()).pathname === pathname);

/** Searches with `deals=true` return the deal product; other searches return the usual catalog. */
const mockDealSearch = (page: Page) =>
  page.route(url => url.pathname === '/api/search', route => {
    const items = new URL(route.request().url()).searchParams.get('deals') === 'true' ? [DEAL_PRODUCT] : PRODUCTS;
    return route.fulfill({ json: { items, total: items.length, page: 1, pageSize: 12 } });
  });

/** ORDER after delivery at `deliveredAt`; the return window runs from that moment. */
async function mockDeliveredOrder(page: Page, deliveredAt: string) {
  await page.route(`**/api/orders/${ORDER.id}`, route => route.fulfill({ json: { ...ORDER, status: 'Delivered' } }));
  await page.route(`**/api/orders/${ORDER.id}/status`, route => route.fulfill({
    json: { ...ORDER_STATUS, status: 'Delivered', history: [...ORDER_STATUS.history, { status: 'Delivered', timestamp: deliveredAt, note: '' }] },
  }));
}

test('home shows ratings, today\'s deals and recently viewed products', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await mockDealSearch(page);

  await page.goto('/');
  await expect(page.getByTestId('product-grid').getByTestId('card-rating-p1')).toContainText('2 ratings');
  const deals = page.getByTestId('deals-strip');
  await expect(deals.getByTestId('card-price-p3')).toContainText('33% off');
  await expect(deals.getByTestId('card-stock-p3')).toHaveText('Only 3 left in stock – order soon.');
  await expect(page.getByTestId('deals-see-all')).toHaveAttribute('href', '/search?deals=true');
  await expect(page.getByTestId('recently-viewed-strip')).toHaveCount(0);

  await page.goto('/product/p1');
  await expect(page.getByTestId('product-name')).toHaveText('Silk Saree');
  await page.goto('/');
  await expect(page.getByTestId('recently-viewed-strip').getByTestId('product-card-p1')).toBeVisible();
  await page.screenshot({ path: ss('home-deals-recently-viewed'), fullPage: true });
});

test('buy now checks out a single product straight from its page', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.route('**/api/products/p3', route => route.fulfill({ json: DEAL_PRODUCT }));

  await page.goto('/product/p3');
  await expect(page.getByTestId('product-price')).toContainText('33% off');
  await expect(page.getByTestId('product-stock')).toHaveText('Only 3 left in stock – order soon.');
  await page.getByTestId('product-qty').selectOption('2');
  const quote = apiCall(page, 'POST', '/api/orders/quote');
  await page.getByTestId('product-buy-now').click();

  await expect(page).toHaveURL(/\/checkout\?buyNow=p3&qty=2$/);
  expect((await quote).postDataJSON()).toEqual({ items: [{ productId: 'p3', quantity: 2 }], deliverySpeed: 'Standard', giftWrap: false, buyNow: true });
  await expect(page.getByTestId('buy-now-notice')).toBeVisible();
  await expect(page.getByTestId('checkout-line-p3')).toContainText('Brass Diya Set × 2');
  await expect(page.getByTestId('checkout-subtotal')).toHaveText('₹1,998.00');
  await expect(page.getByTestId('quote-shipping')).toHaveText('₹99.00');
  await expect(page.getByTestId('free-shipping-hint')).toHaveText('Add ₹502.00 more for FREE standard shipping.');
  await expect(page.getByTestId('quote-total')).toHaveText('₹2,457.00');
  await page.getByTestId('checkout-address').fill('12 MG Road, Mumbai');
  await page.screenshot({ path: ss('buy-now-checkout'), fullPage: true });

  const order = apiCall(page, 'POST', '/api/orders');
  await page.getByTestId('place-order-btn').click();
  expect((await order).postDataJSON()).toMatchObject({ items: [{ productId: 'p3', quantity: 2 }], buyNow: true, userId: 'u1', shippingAddress: '12 MG Road, Mumbai' });
  await expect(page.getByTestId('checkout-order-total')).toHaveText('₹2,457.00');
});

test('reviews can be filtered, sorted and voted helpful', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.goto('/product/p1');
  const reviews = page.locator('[data-testid="reviews-list"] > li');
  await expect(reviews).toHaveCount(2);

  await page.getByTestId('reviews-filter-4').click();
  await expect(page.getByTestId('reviews-filter-4')).toHaveAttribute('aria-pressed', 'true');
  await expect(reviews).toHaveCount(1);
  await expect(reviews.first()).toHaveAttribute('data-testid', 'review-r2');
  await page.getByTestId('reviews-filter-clear').click();
  await expect(reviews).toHaveCount(2);

  await page.getByTestId('reviews-verified-only').check();
  await expect(reviews).toHaveCount(1);
  await expect(page.getByTestId('review-verified-r1')).toHaveText('Verified Purchase');
  await page.getByTestId('reviews-verified-only').uncheck();

  await expect(reviews.first()).toHaveAttribute('data-testid', 'review-r1');
  await page.getByTestId('reviews-sort').selectOption('helpful');
  await expect(reviews.first()).toHaveAttribute('data-testid', 'review-r2');

  // r1 is the shopper's own review, so only r2 can be voted on.
  await expect(page.getByTestId('review-helpful-r1')).toHaveCount(0);
  await expect(page.getByTestId('review-helpful-count-r2')).toHaveText('3 people found this helpful');
  const vote = apiCall(page, 'POST', '/api/products/p1/reviews/r2/helpful');
  await page.getByTestId('review-helpful-r2').click();
  expect((await vote).postDataJSON()).toEqual({ userId: 'u1' });
  await expect(page.getByTestId('review-helpful-thanks-r2')).toHaveText('Thank you for your feedback.');
  await expect(page.getByTestId('review-helpful-count-r2')).toHaveText('4 people found this helpful');
  await page.getByTestId('reviews-section').screenshot({ path: ss('reviews-filter-helpful') });
});

test('shoppers ask questions and sellers answer them', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.goto('/product/p1');
  await expect(page.getByTestId('answer-seller-a1')).toHaveText('Seller');
  await expect(page.getByTestId('question-unanswered-q2')).toHaveText('No answers yet.');

  const ask = apiCall(page, 'POST', '/api/products/p1/questions');
  await page.getByTestId('question-input').fill('Is the colour true to the photos?');
  await page.getByTestId('question-submit').click();
  expect((await ask).postDataJSON()).toEqual({ userId: 'u1', userName: 'Alice Kumar', text: 'Is the colour true to the photos?' });
  await expect(page.getByTestId('question-q-new')).toContainText('Is the colour true to the photos?');
  await expect(page.getByTestId('product-toast')).toHaveText('Your question was posted.');

  await setPersonaAndReload(page, { id: 'u2', name: 'Bob Vendor', persona: 'Vendor' });
  const answer = apiCall(page, 'POST', '/api/products/p1/questions/q2/answers');
  await page.getByTestId('answer-input-q2').fill('Yes, dry cleaning is recommended.');
  await page.getByTestId('answer-submit-q2').click();
  expect((await answer).postDataJSON()).toEqual({ userId: 'u2', userName: 'Bob Vendor', text: 'Yes, dry cleaning is recommended.', persona: 'Vendor' });
  await expect(page.getByTestId('answer-seller-a-new')).toHaveText('Seller');
  await expect(page.getByTestId('question-unanswered-q2')).toHaveCount(0);
  await expect(page.getByTestId('product-toast')).toHaveText('Your answer was posted.');
  // A viewport shot, so the fixed-position toast isn't clipped as it would be in an element shot.
  await page.getByTestId('answer-seller-a-new').scrollIntoViewIfNeeded();
  await page.screenshot({ path: ss('product-questions-answers') });
});

test('out-of-stock products offer a back-in-stock alert', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.route('**/api/products/p1', route => route.fulfill({ json: { ...PRODUCTS[0], stock: 0 } }));
  await page.goto('/product/p1');
  await expect(page.getByTestId('product-stock')).toHaveText('Currently unavailable.');
  await expect(page.getByTestId('product-qty')).toHaveCount(0);
  await expect(page.getByTestId('product-add-cart')).toBeDisabled();
  await expect(page.getByTestId('product-buy-now')).toBeDisabled();

  const subscribe = apiCall(page, 'POST', '/api/products/p1/stock-alerts');
  await page.getByTestId('stock-alert-btn').click();
  expect((await subscribe).postDataJSON()).toEqual({ userId: 'u1' });
  await expect(page.getByTestId('stock-alert-subscribed')).toHaveText("You'll get a notification when this item is back in stock.");
  await page.screenshot({ path: ss('product-stock-alert') });

  const unsubscribe = apiCall(page, 'DELETE', '/api/products/p1/stock-alerts/u1');
  await page.getByTestId('stock-alert-cancel').click();
  await unsubscribe;
  await expect(page.getByTestId('product-toast')).toHaveText('Stock alert cancelled.');
  await expect(page.getByTestId('stock-alert-btn')).toBeVisible();
});

test('wish list shows price drops and moves items to the cart', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.goto('/product/p1');
  const add = apiCall(page, 'POST', '/api/wishlist/u1/items');
  await page.getByTestId('product-add-wishlist').click();
  expect((await add).postDataJSON()).toEqual({ productId: 'p1' });
  await expect(page.getByTestId('product-add-wishlist')).toHaveText('In your Wish List');
  await expect(page.getByTestId('product-add-wishlist')).toBeDisabled();
  await page.getByTestId('product-view-wishlist').click();

  await expect(page).toHaveURL(/\/wishlist$/);
  await expect(page.getByTestId('wishlist-item-p1')).toBeVisible();
  await expect(page.getByTestId('wishlist-price-p2')).toHaveText('₹15,000.00');
  await expect(page.getByTestId('wishlist-price-drop-p2')).toHaveText('Price dropped — was ₹16,500.00 when added');
  await expect(page.getByTestId('wishlist-stock-p2')).toHaveText('Only 5 left in stock – order soon.');
  await expect(page.getByTestId('wishlist-stock-p4')).toHaveText('Currently unavailable.');
  await expect(page.getByTestId('wishlist-move-to-cart-p4')).toBeDisabled();
  await page.screenshot({ path: ss('wishlist'), fullPage: true });

  await expect(page.getByTestId('nav-cart-badge')).toHaveText('1');
  await page.getByTestId('wishlist-move-to-cart-p2').click();
  await expect(page.getByTestId('wishlist-toast')).toHaveText('Moved "Gold Necklace" to your cart.');
  await expect(page.getByTestId('wishlist-item-p2')).toHaveCount(0);
  await expect(page.getByTestId('nav-cart-badge')).toHaveText('2');

  await page.getByTestId('wishlist-remove-p4').click();
  await expect(page.getByTestId('wishlist-toast')).toHaveText('Removed "Pashmina Shawl" from your Wish List.');
  await expect(page.getByTestId('wishlist-item-p4')).toHaveCount(0);
});

test('search refines by rating, stock, deals and sort order', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await mockDealSearch(page);
  const searchWith = (expected: Record<string, string>) => page.waitForRequest(r => {
    const url = new URL(r.url());
    return url.pathname === '/api/search' && Object.entries(expected).every(([key, value]) => url.searchParams.get(key) === value);
  });

  await page.goto('/search');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Search');
  await expect(page.getByTestId('search-category-options').locator('option')).toHaveCount(3);

  let search = searchWith({ sort: 'price-asc' });
  await page.getByTestId('search-sort').selectOption('price-asc');
  await search;
  search = searchWith({ sort: 'price-asc', minRating: '4' });
  await page.getByTestId('search-min-rating').selectOption('4');
  await search;
  search = searchWith({ sort: 'price-asc', minRating: '4', inStock: 'true' });
  await page.getByTestId('search-in-stock').click();
  await search;
  search = searchWith({ sort: 'price-asc', minRating: '4', inStock: 'true', deals: 'true' });
  await page.getByTestId('search-deals').click();
  await search;

  await expect(page).toHaveURL(/\/search\?sort=price-asc&minRating=4&inStock=true&deals=true$/);
  await expect(page.getByTestId('search-in-stock')).toBeChecked();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText("Today's deals");
  await expect(page.getByTestId('search-results').getByTestId('card-price-p3')).toContainText('33% off');
  await page.screenshot({ path: ss('search-refinements'), fullPage: true });

  // Hand-edited links with unknown values search without them rather than failing.
  const plain = apiCall(page, 'GET', '/api/search');
  await page.goto('/search?sort=bogus&minRating=9');
  const params = new URL((await plain).url()).searchParams;
  expect(params.has('sort')).toBe(false);
  expect(params.has('minRating')).toBe(false);
  await expect(page.getByTestId('search-sort')).toHaveValue('');
});

test('cart items can be saved for later and moved back', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.goto('/cart');
  await expect(page.getByTestId('cart-item-p1')).toBeVisible();
  await expect(page.getByTestId('saved-for-later')).toHaveCount(0);

  await page.getByTestId('save-for-later-p1').click();
  await expect(page.getByTestId('cart-page-toast')).toHaveText('Saved for later.');
  await expect(page.getByTestId('cart-empty')).toBeVisible();
  await expect(page.getByTestId('saved-item-p1')).toBeVisible();
  await expect(page.getByTestId('saved-for-later').getByRole('heading')).toHaveText('Saved for later (1)');
  await expect(page.getByTestId('nav-cart-badge')).toHaveCount(0);
  await page.screenshot({ path: ss('cart-saved-for-later') });

  await page.getByTestId('move-to-cart-p1').click();
  await expect(page.getByTestId('cart-page-toast')).toHaveText('Moved to cart.');
  await expect(page.getByTestId('cart-item-p1')).toBeVisible();
  await expect(page.getByTestId('saved-for-later')).toHaveCount(0);
  await expect(page.getByTestId('nav-cart-badge')).toHaveText('1');

  await page.getByTestId('save-for-later-p1').click();
  await expect(page.getByTestId('saved-item-p1')).toBeVisible();
  const removal = apiCall(page, 'DELETE', '/api/cart/u1/saved/p1');
  await page.getByTestId('remove-saved-p1').click();
  await removal;
  await expect(page.getByTestId('cart-page-toast')).toHaveText('Removed from Saved for later.');
  await expect(page.getByTestId('saved-for-later')).toHaveCount(0);
});

test('address book adds, edits, defaults and removes addresses', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  const fill = async (fields: Record<string, string>) => {
    for (const [testId, value] of Object.entries(fields)) await page.getByTestId(testId).fill(value);
  };
  await page.goto('/addresses');
  await expect(page.getByTestId('addresses-empty')).toBeVisible();

  // The first address becomes the default.
  await page.getByTestId('address-add').click();
  await fill({
    'address-full-name': 'Alice Kumar', 'address-line1': '12 MG Road', 'address-line2': 'Flat 4B', 'address-city': 'Mumbai',
    'address-state': 'MH', 'address-postal-code': '400001', 'address-country': 'India', 'address-phone': '+91 98200 00000',
  });
  const created = apiCall(page, 'POST', '/api/users/u1/addresses');
  await page.getByTestId('address-submit').click();
  expect((await created).postDataJSON()).toEqual({
    fullName: 'Alice Kumar', line1: '12 MG Road', line2: 'Flat 4B', city: 'Mumbai', state: 'MH',
    postalCode: '400001', country: 'India', phone: '+91 98200 00000', isDefault: false,
  });
  await expect(page.getByTestId('addresses-toast')).toHaveText('Address added.');
  await expect(page.getByTestId('address-default-addr-new-1')).toHaveText('Default');

  await page.getByTestId('address-add').click();
  await fill({
    'address-full-name': 'Ravi Kumar', 'address-line1': '7 Park Street', 'address-city': 'Kolkata',
    'address-state': 'WB', 'address-postal-code': '700016', 'address-country': 'India',
  });
  await page.getByTestId('address-submit').click();
  await expect(page.getByTestId('address-addr-new-2')).toBeVisible();
  await expect(page.getByTestId('address-default-addr-new-1')).toBeVisible();

  await page.getByTestId('address-set-default-addr-new-2').click();
  await expect(page.getByTestId('addresses-toast')).toHaveText('Default address updated.');
  await expect(page.getByTestId('address-default-addr-new-2')).toBeVisible();
  await expect(page.getByTestId('address-default-addr-new-1')).toHaveCount(0);
  await expect(page.locator('[data-testid="address-list"] > li').first()).toHaveAttribute('data-testid', 'address-addr-new-2');

  await page.getByTestId('address-edit-addr-new-1').click();
  await expect(page.getByTestId('address-full-name')).toHaveValue('Alice Kumar');
  await page.getByTestId('address-city').fill('Pune');
  const updated = apiCall(page, 'PUT', '/api/users/u1/addresses/addr-new-1');
  await page.getByTestId('address-submit').click();
  expect((await updated).postDataJSON()).toMatchObject({ fullName: 'Alice Kumar', city: 'Pune', isDefault: false });
  await expect(page.getByTestId('addresses-toast')).toHaveText('Address updated.');
  await expect(page.getByTestId('address-addr-new-1')).toContainText('Pune, MH, 400001');
  await page.screenshot({ path: ss('address-book'), fullPage: true });

  const removed = apiCall(page, 'DELETE', '/api/users/u1/addresses/addr-new-1');
  await page.getByTestId('address-delete-addr-new-1').click();
  await removed;
  await expect(page.getByTestId('addresses-toast')).toHaveText('Address removed.');
  await expect(page.getByTestId('address-addr-new-1')).toHaveCount(0);
  await expect(page.getByTestId('address-default-addr-new-2')).toBeVisible();
});

test('checkout ships to a saved address', async ({ page }) => {
  await mockAllApis(page);
  await mockAddressBook(page, ADDRESSES);
  await injectCustomerPersona(page);
  await page.goto('/checkout');

  // The default address is chosen for the shopper, so there is nothing to type.
  await expect(page.getByTestId('checkout-address-addr-1')).toBeChecked();
  await expect(page.getByTestId('checkout-address')).toHaveCount(0);
  await page.getByTestId('address-option-new').check();
  await expect(page.getByTestId('checkout-address')).toBeVisible();
  await page.getByTestId('checkout-address-addr-2').check();
  await expect(page.getByTestId('checkout-address')).toHaveCount(0);
  await page.screenshot({ path: ss('checkout-saved-address'), fullPage: true });

  const order = apiCall(page, 'POST', '/api/orders');
  await page.getByTestId('place-order-btn').click();
  expect((await order).postDataJSON()).toMatchObject({ shippingAddress: 'Ravi Kumar, 7 Park Street, Kolkata, WB 700016, India' });
  await expect(page.getByTestId('checkout-payment-step')).toBeVisible();
});

test('checkout applies a coupon, faster delivery and gift wrap', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.goto('/checkout');
  await expect(page.getByTestId('quote-total')).toHaveText('₹3,539.00');
  await expect(page.getByTestId('quote-shipping')).toHaveText('FREE');
  await expect(page.getByTestId('free-shipping-hint')).toHaveText('Your order qualifies for FREE standard shipping.');
  await expect(page.getByTestId('delivery-eta-Standard')).toHaveText('Arrives by Jun 6, 2024');

  await page.getByTestId('coupon-input').fill('BOGUS');
  await page.getByTestId('coupon-apply').click();
  await expect(page.getByTestId('coupon-error')).toHaveText("'BOGUS' is not a valid coupon code.");
  await page.getByTestId('coupon-input').fill('welcome10');
  await page.getByTestId('coupon-apply').click();
  await expect(page.getByTestId('coupon-applied')).toContainText('Coupon WELCOME10 applied.');
  await expect(page.getByTestId('coupon-error')).toHaveCount(0);
  await expect(page.getByTestId('quote-discount')).toHaveText('\u2212₹300.00');

  await page.getByTestId('delivery-Expedited').check();
  await expect(page.getByTestId('quote-shipping')).toHaveText('₹199.00');
  await expect(page.getByTestId('free-shipping-hint')).toHaveCount(0);
  await page.getByTestId('gift-wrap').check();
  await expect(page.getByTestId('quote-gift-wrap')).toHaveText('₹49.00');
  await page.getByTestId('gift-message').fill('Happy Diwali!');
  await expect(page.getByTestId('checkout-quote')).toHaveAttribute('aria-busy', 'false');
  // 2,999 − 300 + 199 shipping + 49 gift wrap + 486 tax
  await expect(page.getByTestId('quote-total')).toHaveText('₹3,433.00');
  await page.getByTestId('checkout-address').fill('12 MG Road, Mumbai');
  await page.screenshot({ path: ss('checkout-options'), fullPage: true });

  const order = apiCall(page, 'POST', '/api/orders');
  await page.getByTestId('place-order-btn').click();
  expect((await order).postDataJSON()).toEqual({
    items: [{ productId: 'p1', quantity: 1 }], couponCode: 'WELCOME10', deliverySpeed: 'Expedited', giftWrap: true,
    giftMessage: 'Happy Diwali!', userId: 'u1', shippingAddress: '12 MG Road, Mumbai',
  });
  await expect(page.getByTestId('checkout-order-discount')).toHaveText('Discount (WELCOME10): \u2212₹300.00');
  await expect(page.getByTestId('checkout-order-shipping')).toHaveText('Shipping: ₹199.00');
  await expect(page.getByTestId('checkout-order-gift-wrap')).toHaveText('Gift wrap: ₹49.00');
  await expect(page.getByTestId('checkout-order-total')).toHaveText('₹3,433.00');
  await expect(page.getByTestId('checkout-order-eta')).toHaveText('Arrives by Jun 3, 2024');
});

test('orders can be searched, filtered and bought again', async ({ page }) => {
  await page.clock.setFixedTime(new Date('2024-06-10T12:00:00Z'));
  await mockAllApis(page);
  await injectCustomerPersona(page);
  const necklace = { productId: 'p2', name: 'Gold Necklace', price: 15000, quantity: 1 };
  const delivered = { ...ORDER, id: 'ord-002-0000-0000-0000', status: 'Delivered', createdAt: '2024-02-15T12:00:00Z', items: [necklace], subtotal: 15000, tax: 2700, total: 17700 };
  const cancelled = { ...ORDER, id: 'ord-003-0000-0000-0000', status: 'Cancelled', createdAt: '2023-11-20T12:00:00Z' };
  await page.route('**/api/orders/user/**', route => route.fulfill({
    json: [{ ...ORDER, estimatedDelivery: '2024-06-06T00:00:00Z' }, delivered, cancelled],
  }));
  await page.goto('/orders');
  const cards = page.locator('[data-testid^="order-card-"]');
  await expect(cards).toHaveCount(3);
  await expect(page.getByTestId(`order-eta-${ORDER.id}`)).toHaveText('Arriving by Jun 6, 2024');

  await page.getByTestId('orders-search').fill('necklace');
  await expect(cards).toHaveCount(1);
  await expect(page.getByTestId(`order-card-${delivered.id}`)).toBeVisible();
  await page.getByTestId('orders-search').fill('');
  await page.getByTestId('orders-status-filter').selectOption('cancelled');
  await expect(cards).toHaveCount(1);
  await expect(page.getByTestId(`order-card-${cancelled.id}`)).toBeVisible();
  await page.getByTestId('orders-status-filter').selectOption('');

  await page.getByTestId('orders-time-filter').selectOption('30d');
  await expect(cards).toHaveCount(1);
  await page.getByTestId('orders-time-filter').selectOption('6m');
  await expect(cards).toHaveCount(2);
  await page.getByTestId('orders-time-filter').selectOption('2023');
  await expect(cards).toHaveCount(1);
  await page.getByTestId('orders-status-filter').selectOption('delivered');
  await expect(page.getByTestId('orders-filter-empty')).toHaveText('No orders match these filters.');
  await page.getByTestId('orders-time-filter').selectOption('');
  await expect(cards).toHaveCount(1);

  const buyAgain = apiCall(page, 'POST', '/api/cart/u1/items');
  await page.getByTestId(`buy-again-${delivered.id}-p2`).click();
  expect((await buyAgain).postDataJSON()).toEqual({ productId: 'p2', quantity: 1 });
  await expect(page.getByTestId('orders-toast')).toHaveText('Added "Gold Necklace" to cart');
  await page.screenshot({ path: ss('orders-filters-buy-again') });
});

test('a delivered order can be returned within 30 days', async ({ page }) => {
  await page.clock.setFixedTime(new Date('2024-06-10T12:00:00Z'));
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await mockDeliveredOrder(page, '2024-06-05T12:00:00Z');
  await page.goto(`/orders/${ORDER.id}`);
  await expect(page.getByTestId('return-window')).toHaveText('Eligible for return until Jul 5, 2024');
  await expect(page.getByTestId('cancel-order-btn')).toHaveCount(0);

  await page.getByTestId('return-order-btn').click();
  await page.getByTestId('return-reason').selectOption('damaged');
  await page.getByTestId('return-comments').fill('The border is torn.');
  const request = apiCall(page, 'POST', `/api/orders/${ORDER.id}/return`);
  await page.getByTestId('return-submit').click();
  expect((await request).postDataJSON()).toEqual({ reason: 'Item arrived damaged: The border is torn.' });
  await expect(page.getByTestId('order-cancel-notice')).toHaveText("Return requested. We'll refund you once the item is received.");
  await expect(page.getByTestId('order-status-badge')).toHaveText('ReturnRequested');
  await expect(page.getByTestId('return-status')).toContainText('Return in progress — awaiting the item.');
  await expect(page.getByTestId('return-reason-text')).toHaveText('Reason: Item arrived damaged: The border is torn.');
  await expect(page.getByTestId('return-order-btn')).toHaveCount(0);
  await page.screenshot({ path: ss('order-return-requested') });
});

test('the return option closes 30 days after delivery', async ({ page }) => {
  await page.clock.setFixedTime(new Date('2024-06-10T12:00:00Z'));
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await mockDeliveredOrder(page, '2024-05-01T12:00:00Z');
  await page.goto(`/orders/${ORDER.id}`);
  await expect(page.getByTestId('return-window')).toHaveText('Return window closed on May 31, 2024');
  await expect(page.getByTestId('return-order-btn')).toHaveCount(0);
  await page.screenshot({ path: ss('order-return-window-closed') });
});

test('fulfillment receives returned items and issues the refund', async ({ page }) => {
  await mockAllApis(page);
  await injectFulfillmentPersona(page);
  const awaiting = { ...ORDER, status: 'ReturnRequested', returnReason: 'Item arrived damaged' };
  await page.route('**/api/orders', route => route.fulfill({ json: [awaiting, { ...ORDER, id: 'ord-002-0000-0000-0000' }] }));
  await page.goto('/fulfillment');
  await expect(page.locator('[data-testid^="return-to-receive-"]')).toHaveCount(1);
  await expect(page.getByTestId(`return-to-receive-${ORDER.id}`)).toContainText('Silk Saree × 1');
  await expect(page.getByTestId(`return-to-receive-${ORDER.id}`)).toContainText('Reason: Item arrived damaged');
  await page.screenshot({ path: ss('fulfillment-returns'), fullPage: true });

  const received = apiCall(page, 'POST', `/api/orders/${ORDER.id}/return/complete`);
  await page.getByTestId(`receive-return-${ORDER.id}`).click();
  await received;
  await expect(page.getByTestId('fulfillment-toast')).toHaveText('Return for order ord-001- received; refund issued.');
  await expect(page.getByTestId('returns-empty')).toBeVisible();

  // The order page offers the same step to fulfillment agents.
  await page.route(`**/api/orders/${ORDER.id}`, route => route.fulfill({ json: awaiting }));
  await page.goto(`/orders/${ORDER.id}`);
  await page.getByTestId('complete-return-btn').click();
  await expect(page.getByTestId('order-status-badge')).toHaveText('Returned');
  await expect(page.getByTestId('return-status')).toContainText('Returned and refunded.');
  await expect(page.getByTestId('order-cancel-notice')).toHaveText('Return received; the refund has been issued.');
});

test('admins create, edit, switch off and delete coupons', async ({ page }) => {
  await mockAllApis(page);
  await injectAdminPersona(page);
  await page.goto('/');
  await page.getByTestId('nav-coupons-setup').click();
  await expect(page).toHaveURL(/\/setup\/coupons$/);
  await expect(page.getByTestId('coupon-status-WELCOME10')).toHaveText('Active');
  await expect(page.getByTestId('coupon-status-SAVE5')).toHaveText('Inactive');

  await page.getByTestId('coupon-form-code').fill('festive50');
  await page.getByTestId('coupon-form-description').fill('$50 off festive orders');
  await page.getByTestId('coupon-form-kind').selectOption('Fixed');
  await page.getByTestId('coupon-form-value').fill('50');
  await page.getByTestId('coupon-form-min-subtotal').fill('200');
  await page.getByTestId('coupon-form-expires').fill('2024-12-31');
  const created = apiCall(page, 'POST', '/api/coupons');
  await page.getByTestId('coupon-form-submit').click();
  expect((await created).postDataJSON()).toEqual({
    code: 'FESTIVE50', description: '$50 off festive orders', kind: 'Fixed', value: 50, minSubtotal: 200, expiresAt: '2024-12-31T23:59:59Z', active: true,
  });
  await expect(page.getByTestId('coupon-row-FESTIVE50')).toContainText('$50.00');
  await expect(page.getByTestId('coupon-row-FESTIVE50')).toContainText('Dec 31, 2024');
  await expect(page.getByTestId('coupon-form-code')).toHaveValue('');

  await page.getByTestId('coupon-form-code').fill('welcome10');
  await page.getByTestId('coupon-form-value').fill('5');
  await page.getByTestId('coupon-form-submit').click();
  await expect(page.getByTestId('coupon-form-error')).toHaveText("Coupon 'WELCOME10' already exists.");

  const toggled = apiCall(page, 'PUT', '/api/coupons/WELCOME10');
  await page.getByTestId('coupon-toggle-WELCOME10').click();
  expect((await toggled).postDataJSON()).toMatchObject({ kind: 'Percent', value: 10, active: false });
  await expect(page.getByTestId('coupon-status-WELCOME10')).toHaveText('Inactive');
  await expect(page.getByTestId('coupon-form-error')).toHaveCount(0);

  await page.getByTestId('coupon-edit-SAVE5').click();
  await expect(page.getByTestId('coupon-form-code')).toHaveValue('SAVE5');
  await expect(page.getByTestId('coupon-form-code')).toBeDisabled();
  await page.getByTestId('coupon-form-value').fill('7');
  await page.getByTestId('coupon-form-active').check();
  const edited = apiCall(page, 'PUT', '/api/coupons/SAVE5');
  await page.getByTestId('coupon-form-submit').click();
  expect((await edited).postDataJSON()).toEqual({
    description: '$5 off orders of $50 or more', kind: 'Fixed', value: 7, minSubtotal: 50, expiresAt: '2024-12-31T23:59:59Z', active: true,
  });
  await expect(page.getByTestId('coupon-status-SAVE5')).toHaveText('Active');
  await page.screenshot({ path: ss('setup-coupons'), fullPage: true });

  const deleted = apiCall(page, 'DELETE', '/api/coupons/FESTIVE50');
  await page.getByTestId('coupon-delete-FESTIVE50').click();
  await deleted;
  await expect(page.getByTestId('coupon-row-FESTIVE50')).toHaveCount(0);
});

// Delivery estimates and coupon expiry are calendar days sent as UTC, so they must read the same in every time zone.
test.describe('west of UTC', () => {
  test.use({ timezoneId: 'America/New_York' });

  test('delivery estimates show the promised day, not the evening before', async ({ page }) => {
    await mockAllApis(page);
    await injectCustomerPersona(page);
    const shipping = { ...ORDER, estimatedDelivery: '2024-06-06T00:00:00Z' };
    await page.route('**/api/orders/user/**', route => route.fulfill({ json: [shipping] }));
    await page.route(`**/api/orders/${ORDER.id}`, route => route.fulfill({ json: shipping }));

    await page.goto('/checkout');
    await expect(page.getByTestId('delivery-eta-OneDay')).toHaveText('Arrives by Jun 2, 2024');
    await expect(page.getByTestId('checkout-eta')).toHaveText('Arrives by Jun 6, 2024');
    await page.goto('/orders');
    await expect(page.getByTestId(`order-eta-${ORDER.id}`)).toHaveText('Arriving by Jun 6, 2024');
    await page.goto(`/orders/${ORDER.id}`);
    await expect(page.getByTestId('order-eta')).toHaveText('Arriving by Jun 6, 2024');
    await expect(page.getByTestId('trace-eta')).toHaveText('Estimated delivery: Jun 5, 2024');
    await page.screenshot({ path: ss('order-eta-west-of-utc') });
  });
});

test.describe('east of UTC', () => {
  test.use({ timezoneId: 'Asia/Kolkata' });

  test('coupon expiry shows the chosen day, not the morning after', async ({ page }) => {
    await mockAllApis(page);
    await injectAdminPersona(page);
    await page.goto('/setup/coupons');
    await expect(page.getByTestId('coupon-row-SAVE5')).toContainText('Dec 31, 2024');
    await page.getByTestId('coupon-edit-SAVE5').click();
    await expect(page.getByTestId('coupon-form-expires')).toHaveValue('2024-12-31');
    await page.getByTestId('coupons-table').screenshot({ path: ss('coupon-expiry-east-of-utc') });
  });

  test('a delivery estimate without a time zone is read as a UTC day', async ({ page }) => {
    await mockAllApis(page);
    await injectCustomerPersona(page);
    await page.route('**/api/supplychain/traces**', route => route.fulfill({ json: { ...TRACE, estimatedDelivery: '2024-06-05T00:00:00' } }));
    await page.goto(`/orders/${ORDER.id}`);
    await expect(page.getByTestId('trace-eta')).toHaveText('Estimated delivery: Jun 5, 2024');
    await page.getByTestId('supplychain-trace').screenshot({ path: ss('trace-eta-east-of-utc') });
  });
});
