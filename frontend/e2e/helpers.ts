import type { Page, Route } from '@playwright/test';

export const PRODUCTS = [
  { id: 'p1', name: 'Silk Saree', description: 'Beautiful silk saree', category: 'Clothing', price: 2999, currency: 'INR', stock: 10, vendorId: 'v1', imageUrl: '', tags: ['silk', 'saree'], averageRating: 4.5, reviewCount: 2 },
  { id: 'p2', name: 'Gold Necklace', description: 'Elegant gold necklace', category: 'Jewelry', price: 15000, currency: 'INR', stock: 5, vendorId: 'v1', imageUrl: '', tags: ['gold', 'necklace'] },
];

/** On deal (its list price is above its price). Not in PRODUCTS, so pages only show a deal when a test asks for one. */
export const DEAL_PRODUCT = {
  id: 'p3', name: 'Brass Diya Set', description: 'Hand-polished brass lamps, set of four', category: 'Home', price: 999, listPrice: 1499,
  currency: 'INR', stock: 3, vendorId: 'v1', imageUrl: '', tags: ['brass', 'diya'], averageRating: 4.6, reviewCount: 128,
};

const CATALOG = [...PRODUCTS, DEAL_PRODUCT];

export const REVIEWS = [
  { id: 'r1', productId: 'p1', userId: 'u1', title: 'Lovely fabric', body: 'The silk feels premium.', rating: 5, mediaIds: [], createdAt: '2024-06-01T10:00:00Z', verifiedPurchase: true, helpfulCount: 0 },
  { id: 'r2', productId: 'p1', userId: 'u3', title: 'Good value', body: 'Colour is slightly different.', rating: 4, mediaIds: [], createdAt: '2024-05-20T10:00:00Z', verifiedPurchase: false, helpfulCount: 3 },
];

export const QUESTIONS = [
  {
    id: 'q1', productId: 'p1', userId: 'u3', userName: 'Charlie Admin', text: 'Does it come with a blouse piece?', createdAt: '2024-05-25T10:00:00Z',
    answers: [{ id: 'a1', userId: 'u2', userName: 'Bob Vendor', text: 'Yes, an unstitched blouse piece is included.', isSeller: true, createdAt: '2024-05-26T10:00:00Z' }],
  },
  { id: 'q2', productId: 'p1', userId: 'u3', userName: 'Charlie Admin', text: 'Can it be dry cleaned?', createdAt: '2024-05-28T10:00:00Z', answers: [] as { id: string; userId: string; userName: string; text: string; isSeller: boolean; createdAt: string }[] },
];

export const USERS = [
  { id: 'u1', name: 'Alice Kumar', email: 'alice@test.com', persona: 'Customer', createdAt: '2024-01-01T00:00:00Z' },
  { id: 'u2', name: 'Bob Vendor', email: 'bob@test.com', persona: 'Vendor', createdAt: '2024-01-01T00:00:00Z' },
  { id: 'u3', name: 'Charlie Admin', email: 'charlie@test.com', persona: 'Admin', createdAt: '2024-01-01T00:00:00Z' },
  { id: 'u4', name: 'Dave Fulfillment', email: 'dave@test.com', persona: 'FulfillmentAgent', createdAt: '2024-01-01T00:00:00Z' },
];

export const CART = {
  userId: 'u1',
  items: [{ productId: 'p1', name: 'Silk Saree', price: 2999, quantity: 1 }],
  subtotal: 2999,
};

export const ORDER = {
  id: 'ord-001-0000-0000-0000',
  userId: 'u1',
  items: [{ productId: 'p1', name: 'Silk Saree', price: 2999, quantity: 1 }],
  subtotal: 2999,
  tax: 540,
  total: 3539,
  currency: 'INR',
  status: 'Paid',
  shippingAddress: '123 Main St, Mumbai',
  createdAt: '2024-06-01T10:00:00Z',
};

export const ORDER_STATUS = {
  orderId: ORDER.id,
  status: 'Paid',
  history: [
    { status: 'Created', timestamp: '2024-06-01T10:00:00Z', note: 'Order placed' },
    { status: 'AwaitingPayment', timestamp: '2024-06-01T10:01:00Z', note: '' },
    { status: 'Paid', timestamp: '2024-06-01T10:05:00Z', note: 'Payment received' },
  ],
};

export const SHIPMENTS = [
  { orderId: ORDER.id, status: 'Packed', carrier: 'BlueDart', trackingNumber: 'BD123', updatedAt: '2024-06-02T10:00:00Z' },
];

export const INVOICES = [
  { id: 'inv-001', orderId: ORDER.id, userId: 'u1', lines: [{ description: 'Silk Saree', amount: 2999 }], subtotal: 2999, tax: 540, total: 3539, currency: 'INR', issuedAt: '2024-06-01T10:06:00Z', paid: true },
];

export const INVENTORY = [
  { id: 'p1', productId: 'p1', vendorId: 'v1', warehouse: 'main', onHand: 40, reserved: 5, onOrder: 0, leadTimeDays: 7, available: 35, updatedAt: '2024-06-01T10:00:00Z' },
  { id: 'p2', productId: 'p2', vendorId: 'v1', warehouse: 'main', onHand: 4, reserved: 1, onOrder: 0, leadTimeDays: 10, available: 3, updatedAt: '2024-06-01T10:00:00Z' },
];

export const FORECAST = {
  productId: 'p2',
  horizonDays: 30,
  averageDailyDemand: 1.4,
  demandStdDev: 0.6,
  trendPerDay: 0.02,
  forecastDemand: 42,
  safetyStock: 6,
  reorderPoint: 20,
  economicOrderQuantity: 55,
  recommendedOrderQuantity: 60,
  daysOfCoverRemaining: 2,
  reorderNow: true,
  dailyForecast: [
    { date: '2024-06-02', forecastUnits: 1.4 },
    { date: '2024-06-03', forecastUnits: 1.42 },
  ],
};

export const TRACE = {
  id: ORDER.id,
  reference: ORDER.id,
  referenceType: 'order',
  userId: 'u1',
  currentStage: 'InTransit',
  events: [
    { stage: 'OrderPlaced', description: 'Order placed', location: 'Mumbai', actor: 'portal', timestamp: '2024-06-01T10:00:00Z' },
    { stage: 'PaymentSettled', description: 'Payment captured', location: '', actor: 'payment-service', timestamp: '2024-06-01T10:05:00Z' },
    { stage: 'Packed', description: 'Packed at warehouse', location: 'Pune DC', actor: 'fulfillment', timestamp: '2024-06-02T09:00:00Z' },
    { stage: 'InTransit', description: 'In transit with BlueDart', location: 'Nashik hub', actor: 'carrier', timestamp: '2024-06-03T08:00:00Z' },
  ],
  estimatedDelivery: '2024-06-05T00:00:00Z',
  updatedAt: '2024-06-03T08:00:00Z',
};

export const NOTIFICATIONS = {
  unreadCount: 2,
  items: [
    { id: 'n1', userId: 'u1', audience: '', title: 'Order shipped', body: 'Your order is on the way.', reference: ORDER.id, topic: 'order.status', channel: 'InApp', severity: 'Info', read: false, createdAt: '2024-06-03T08:00:00Z' },
    { id: 'n2', userId: '', audience: 'Vendor', title: 'Low stock', body: 'Gold Necklace is below its reorder point.', reference: 'p2', topic: 'inventory.low', channel: 'InApp', severity: 'Warning', read: false, createdAt: '2024-06-02T08:00:00Z' },
    { id: 'n3', userId: 'u1', audience: '', title: 'Invoice issued', body: 'Invoice inv-001 is available.', reference: 'inv-001', topic: 'billing.invoice', channel: 'InApp', severity: 'Info', read: true, createdAt: '2024-06-01T10:06:00Z' },
  ],
};

type DeliverySpeed = 'Standard' | 'Expedited' | 'OneDay';
interface CheckoutBody { items: { productId: string; quantity: number }[]; couponCode?: string; deliverySpeed?: DeliverySpeed; giftWrap?: boolean }

/** Checkout pricing used by the quote and order mocks; the default cart prices to exactly ORDER (2,999 + 540 tax). */
export const CHECKOUT = {
  couponCode: 'WELCOME10',
  couponPercent: 10,
  taxRate: 0.18,
  freeShippingThreshold: 2500,
  giftWrapFee: 49,
  fees: { Standard: 99, Expedited: 199, OneDay: 299 } as Record<DeliverySpeed, number>,
  arrives: { Standard: '2024-06-06T00:00:00Z', Expedited: '2024-06-03T00:00:00Z', OneDay: '2024-06-02T00:00:00Z' } as Record<DeliverySpeed, string>,
};

/** Prices a checkout the way the order service does, in whole rupees. */
export function priceCheckout(body: CheckoutBody) {
  const items = body.items.map(i => {
    const product = CATALOG.find(p => p.id === i.productId) ?? PRODUCTS[0];
    return { productId: product.id, name: product.name, price: product.price, quantity: i.quantity };
  });
  const subtotal = items.reduce((sum, i) => sum + i.price * i.quantity, 0);
  const code = (body.couponCode ?? '').trim().toUpperCase();
  const applied = code === CHECKOUT.couponCode;
  const discount = applied ? Math.round(subtotal * CHECKOUT.couponPercent / 100) : 0;
  const discounted = subtotal - discount;
  const fee = (speed: DeliverySpeed) => (speed === 'Standard' && discounted >= CHECKOUT.freeShippingThreshold ? 0 : CHECKOUT.fees[speed]);
  const speed = body.deliverySpeed ?? 'Standard';
  const shippingFee = fee(speed);
  const giftWrapFee = body.giftWrap ? CHECKOUT.giftWrapFee : 0;
  const tax = Math.round(discounted * CHECKOUT.taxRate);
  return {
    items, subtotal, discount,
    couponCode: applied ? code : '',
    couponError: code && !applied ? `'${code}' is not a valid coupon code.` : null,
    shippingFee, giftWrapFee, tax,
    total: discounted + shippingFee + giftWrapFee + tax,
    currency: 'INR',
    deliverySpeed: speed,
    estimatedDelivery: CHECKOUT.arrives[speed],
    deliveryOptions: (['Standard', 'Expedited', 'OneDay'] as DeliverySpeed[]).map(s => ({ speed: s, fee: fee(s), estimatedDelivery: CHECKOUT.arrives[s] })),
    freeShippingThreshold: CHECKOUT.freeShippingThreshold,
  };
}

export interface Address {
  id: string; userId: string; fullName: string; line1: string; line2: string; city: string; state: string;
  postalCode: string; country: string; phone: string; isDefault: boolean; createdAt: string;
}

export const ADDRESSES: Address[] = [
  { id: 'addr-1', userId: 'u1', fullName: 'Alice Kumar', line1: '12 MG Road', line2: 'Flat 4B', city: 'Mumbai', state: 'MH', postalCode: '400001', country: 'India', phone: '+91 98200 00000', isDefault: true, createdAt: '2024-05-01T10:00:00Z' },
  { id: 'addr-2', userId: 'u1', fullName: 'Ravi Kumar', line1: '7 Park Street', line2: '', city: 'Kolkata', state: 'WB', postalCode: '700016', country: 'India', phone: '', isDefault: false, createdAt: '2024-05-10T10:00:00Z' },
];

export const WISHLIST_ITEMS = [
  { productId: 'p2', name: 'Gold Necklace', priceWhenAdded: 16500, addedAt: '2024-05-01T10:00:00Z', price: 15000, currency: 'INR', stock: 5, imageUrl: '', priceDropped: true },
  { productId: 'p4', name: 'Pashmina Shawl', priceWhenAdded: 4999, addedAt: '2024-05-12T10:00:00Z', price: 4999, currency: 'INR', stock: 0, imageUrl: '', priceDropped: false },
];

export const COUPONS = [
  { id: 'WELCOME10', description: '10% off your first order', kind: 'Percent', value: 10, minSubtotal: 0, expiresAt: null as string | null, active: true },
  { id: 'SAVE5', description: '$5 off orders of $50 or more', kind: 'Fixed', value: 5, minSubtotal: 50, expiresAt: '2024-12-31T23:59:59Z' as string | null, active: false },
];

/** The URL path split on "/", so ids can be read by position ("/api/x/1" → ["", "api", "x", "1"]). */
const segments = (route: Route) => new URL(route.request().url()).pathname.split('/').map(decodeURIComponent);

/**
 * A per-page address book that follows the user service's rules: newest first with the default on top,
 * the first address becomes the default, and deleting the default promotes the newest remaining address.
 */
export async function mockAddressBook(page: Page, initial: Address[] = []) {
  const book = initial.map(a => ({ ...a }));
  let created = 0;
  const listed = () => [...book].sort((a, b) => Number(b.isDefault) - Number(a.isDefault) || b.createdAt.localeCompare(a.createdAt));
  const makeDefault = (id: string) => book.forEach(a => { a.isDefault = a.id === id; });

  await page.route('**/api/users/*/addresses', route => {
    const request = route.request();
    if (request.method() === 'GET') return route.fulfill({ json: listed() });
    const { isDefault, ...fields } = request.postDataJSON() as Omit<Address, 'id' | 'userId' | 'createdAt'>;
    created += 1;
    const address: Address = {
      ...fields, id: `addr-new-${created}`, userId: segments(route)[3], isDefault: false,
      createdAt: new Date(Date.UTC(2024, 5, 1) + created * 60_000).toISOString(),
    };
    book.push(address);
    if (book.length === 1 || isDefault) makeDefault(address.id);
    return route.fulfill({ status: 201, json: address });
  });
  await page.route('**/api/users/*/addresses/*', route => {
    const request = route.request();
    const address = book.find(a => a.id === segments(route)[5]);
    if (!address) return route.fulfill({ status: 404, json: { error: 'Address not found.' } });
    if (request.method() === 'DELETE') {
      book.splice(book.indexOf(address), 1);
      const newest = listed()[0];
      if (address.isDefault && newest) makeDefault(newest.id);
      return route.fulfill({ status: 204, body: '' });
    }
    // The default can move to another address but is never simply switched off.
    const { isDefault, ...fields } = request.postDataJSON() as Omit<Address, 'id' | 'userId' | 'createdAt'>;
    Object.assign(address, fields);
    if (isDefault) makeDefault(address.id);
    return route.fulfill({ json: address });
  });
  await page.route('**/api/users/*/addresses/*/default', route => {
    const address = book.find(a => a.id === segments(route)[5]);
    if (!address) return route.fulfill({ status: 404, json: { error: 'Address not found.' } });
    makeDefault(address.id);
    return route.fulfill({ json: address });
  });
  return book;
}

/** A per-page wish list; adding a product already on the list leaves it unchanged, as the service does. */
export async function mockWishList(page: Page, initial = WISHLIST_ITEMS) {
  const items = initial.map(i => ({ ...i }));
  const list = (userId: string) => ({ id: userId, userId, items: items.map(i => ({ ...i })) });
  const remove = (productId: string) => {
    const index = items.findIndex(i => i.productId === productId);
    return index < 0 ? undefined : items.splice(index, 1)[0];
  };

  await page.route('**/api/wishlist/*', route => route.fulfill({ json: list(segments(route)[3]) }));
  await page.route('**/api/wishlist/*/items', route => {
    const { productId } = route.request().postDataJSON() as { productId: string };
    const product = CATALOG.find(p => p.id === productId);
    if (!product) return route.fulfill({ status: 404, json: { error: `Product '${productId}' was not found.` } });
    if (!items.some(i => i.productId === productId)) {
      items.push({
        productId, name: product.name, priceWhenAdded: product.price, addedAt: '2024-06-01T10:00:00Z',
        price: product.price, currency: product.currency, stock: product.stock, imageUrl: product.imageUrl, priceDropped: false,
      });
    }
    return route.fulfill({ json: list(segments(route)[3]) });
  });
  await page.route('**/api/wishlist/*/items/*', route => {
    remove(segments(route)[5]);
    return route.fulfill({ json: list(segments(route)[3]) });
  });
  await page.route('**/api/wishlist/*/items/*/move-to-cart', route => {
    const item = remove(segments(route)[5]);
    if (!item) return route.fulfill({ status: 404, json: { error: 'That product is not on the wish list.' } });
    const line = { productId: item.productId, name: item.name, price: item.price, quantity: 1 };
    return route.fulfill({
      json: { wishList: list(segments(route)[3]), cart: { ...CART, items: [...CART.items, line], subtotal: CART.subtotal + line.price } },
    });
  });
  return items;
}

/** Per-page coupons that follow the order service's rules: codes are unique and stored in upper case. */
export async function mockCoupons(page: Page, initial = COUPONS) {
  const coupons = initial.map(c => ({ ...c }));
  await page.route('**/api/coupons', route => {
    const request = route.request();
    if (request.method() === 'GET') return route.fulfill({ json: [...coupons].sort((a, b) => a.id.localeCompare(b.id)) });
    const { code, ...fields } = request.postDataJSON() as (typeof COUPONS)[number] & { code: string };
    const id = code.toUpperCase();
    if (coupons.some(c => c.id === id)) return route.fulfill({ status: 409, json: { error: `Coupon '${id}' already exists.` } });
    const coupon = { ...fields, id };
    coupons.push(coupon);
    return route.fulfill({ status: 201, json: coupon });
  });
  await page.route('**/api/coupons/*', route => {
    const request = route.request();
    const id = segments(route)[3];
    const index = coupons.findIndex(c => c.id === id);
    if (index < 0) return route.fulfill({ status: 404, json: { error: `Coupon '${id}' was not found.` } });
    if (request.method() === 'DELETE') {
      coupons.splice(index, 1);
      return route.fulfill({ status: 204, body: '' });
    }
    coupons[index] = { ...(request.postDataJSON() as (typeof COUPONS)[number]), id };
    return route.fulfill({ json: coupons[index] });
  });
  return coupons;
}

// Playwright routes: LAST registered wins. Register generic first, specific last.
export async function mockAllApis(page: Page) {
  // Users
  await page.route('**/api/users', route => {
    if (route.request().method() === 'GET') route.fulfill({ json: USERS });
    else route.fulfill({ json: { ...USERS[0], id: 'new-user' } });
  });
  await page.route('**/api/users/**', route => route.fulfill({ json: USERS[0] }));

  // Vendors
  await page.route('**/api/vendors', route => route.fulfill({ json: [] }));
  await page.route('**/api/vendors/**', route => route.fulfill({ json: { id: 'v1', name: 'Test Vendor', email: 'v@v.com', phone: '1234', address: 'Addr', rating: 4.5, active: true } }));

  // Products - generic first, specific delete/get last
  await page.route('**/api/products', route => {
    if (route.request().method() === 'GET') route.fulfill({ json: PRODUCTS });
    else route.fulfill({ json: { ...PRODUCTS[0], id: 'p-new' } });
  });
  await page.route('**/api/products/**', route => {
    if (route.request().method() === 'DELETE') route.fulfill({ status: 204, body: '' });
    else route.fulfill({ json: PRODUCTS[0] });
  });

  await page.route('**/api/products/*/reviews', route => {
    if (route.request().method() === 'GET') route.fulfill({ json: REVIEWS });
    else route.fulfill({ status: 201, json: { ...REVIEWS[0], id: 'r-new', title: 'Great buy' } });
  });
  await page.route('**/api/products/*/reviews/*/helpful', route => {
    const review = REVIEWS.find(r => r.id === segments(route)[5]) ?? REVIEWS[0];
    route.fulfill({ json: { ...review, helpfulCount: review.helpfulCount + 1 } });
  });
  await page.route('**/api/products/categories', route => route.fulfill({ json: ['Clothing', 'Home', 'Jewelry'] }));

  // Questions & answers: a new answer is marked as the seller's when a vendor writes it.
  await page.route('**/api/products/*/questions', route => {
    const request = route.request();
    if (request.method() === 'GET') return route.fulfill({ json: QUESTIONS });
    const body = request.postDataJSON() as { userId: string; userName: string; text: string };
    return route.fulfill({ status: 201, json: { ...body, id: 'q-new', productId: segments(route)[3], createdAt: '2024-06-02T10:00:00Z', answers: [] } });
  });
  await page.route('**/api/products/*/questions/*/answers', route => {
    const { persona, ...body } = route.request().postDataJSON() as { userId: string; userName: string; text: string; persona: string };
    const question = QUESTIONS.find(q => q.id === segments(route)[5]) ?? QUESTIONS[0];
    const answer = { ...body, id: 'a-new', isSeller: persona === 'Vendor', createdAt: '2024-06-02T11:00:00Z' };
    route.fulfill({ status: 201, json: { ...question, answers: [...question.answers, answer] } });
  });

  // Back-in-stock alerts
  await page.route('**/api/products/*/stock-alerts', route => {
    const { userId } = route.request().postDataJSON() as { userId: string };
    route.fulfill({ status: 201, json: { productId: segments(route)[3], userId, createdAt: '2024-06-02T10:00:00Z' } });
  });
  await page.route('**/api/products/*/stock-alerts/*', route => {
    if (route.request().method() === 'DELETE') route.fulfill({ status: 204, body: '' });
    else route.fulfill({ json: { subscribed: false } });
  });

  // Search: no fixture product is on deal, so the home page's deals strip stays hidden unless a test adds one.
  await page.route('**/api/search**', route => {
    const deals = new URL(route.request().url()).searchParams.get('deals') === 'true';
    const items = deals ? [] : PRODUCTS;
    route.fulfill({ json: { items, total: items.length, page: 1, pageSize: 12 } });
  });
  await page.route('**/api/search/suggest**', route => route.fulfill({ json: ['Silk', 'Gold'] }));

  // Cart - generic first, specific last
  await page.route('**/api/cart/**', route => route.fulfill({ json: CART }));
  await page.route('**/api/cart/**/items/**', route => {
    if (route.request().method() === 'PUT') {
      const quantity = (route.request().postDataJSON() as { quantity: number }).quantity;
      route.fulfill({ json: { ...CART, items: [{ ...CART.items[0], quantity }], subtotal: CART.items[0].price * quantity } });
    } else {
      route.fulfill({ json: { ...CART, items: [] } });
    }
  });
  await page.route('**/api/cart/**/items', route => route.fulfill({ json: { cart: CART, recommendations: [PRODUCTS[1]] } }));
  // Saved for later
  await page.route('**/api/cart/*/items/*/save-for-later', route => route.fulfill({ json: { ...CART, items: [], subtotal: 0, savedForLater: CART.items } }));
  await page.route('**/api/cart/*/saved/*', route => route.fulfill({ json: { ...CART, items: [], subtotal: 0, savedForLater: [] } }));
  await page.route('**/api/cart/*/saved/*/move-to-cart', route => route.fulfill({ json: { ...CART, savedForLater: [] } }));

  // Recommendations
  await page.route('**/api/recommendations/**', route => route.fulfill({ json: [PRODUCTS[1]] }));

  // Orders - generic first, specific last
  await page.route('**/api/orders', route => {
    const request = route.request();
    if (request.method() === 'GET') return route.fulfill({ json: [ORDER] });
    // A placed order carries the checkout's pricing, so the default cart places exactly ORDER.
    const body = request.postDataJSON() as CheckoutBody & { userId: string; shippingAddress: string; giftMessage?: string; buyNow?: boolean };
    const { items, subtotal, discount, couponCode, shippingFee, giftWrapFee, tax, total, deliverySpeed, estimatedDelivery } = priceCheckout(body);
    return route.fulfill({
      status: 201,
      json: {
        ...ORDER, userId: body.userId, shippingAddress: body.shippingAddress, items, subtotal, discount, couponCode, tax, total,
        deliverySpeed, shippingFee, estimatedDelivery, giftWrap: !!body.giftWrap, giftWrapFee, giftMessage: body.giftMessage ?? '', buyNow: !!body.buyNow,
      },
    });
  });
  await page.route('**/api/orders/**', route => route.fulfill({ json: ORDER_STATUS }));
  await page.route('**/api/orders/user/**', route => route.fulfill({ json: [ORDER] }));
  await page.route(`**/api/orders/${ORDER.id}/status`, route => route.fulfill({ json: ORDER_STATUS }));
  await page.route(`**/api/orders/${ORDER.id}`, route => route.fulfill({ json: ORDER }));
  await page.route('**/api/orders/*/cancel', route => route.fulfill({ json: { ...ORDER, status: 'Cancelled' } }));
  await page.route('**/api/orders/quote', route => route.fulfill({ json: priceCheckout(route.request().postDataJSON() as CheckoutBody) }));
  await page.route('**/api/orders/*/return', route => {
    const { reason } = route.request().postDataJSON() as { reason: string };
    route.fulfill({ json: { ...ORDER, id: segments(route)[3], status: 'ReturnRequested', returnReason: reason } });
  });
  await page.route('**/api/orders/*/return/complete', route => route.fulfill({ json: { ...ORDER, id: segments(route)[3], status: 'Returned' } }));

  // Fulfillment - generic first, specific last
  await page.route('**/api/fulfillment/shipments', route => route.fulfill({ json: SHIPMENTS }));
  await page.route('**/api/fulfillment/shipments/**/advance', route => route.fulfill({ json: { ...SHIPMENTS[0], status: 'Shipped' } }));

  // Payments
  await page.route('**/api/payments', route => route.fulfill({ json: { id: 'pay-001', orderId: ORDER.id, amount: 3539, currency: 'INR', status: 'Captured', method: 'Card', maskedInstrument: '**** 1234', createdAt: '2024-06-01T10:05:00Z' } }));
  await page.route('**/api/payments/**', route => route.fulfill({ json: { id: 'pay-001', orderId: ORDER.id, amount: 3539, currency: 'INR', status: 'Captured', method: 'Card', maskedInstrument: '**** 1234', createdAt: '2024-06-01T10:05:00Z' } }));

  // Billing
  await page.route('**/api/billing/invoices**', route => route.fulfill({ json: INVOICES }));
  await page.route('**/api/billing/invoices/**', route => route.fulfill({ json: INVOICES[0] }));

  // Inventory - generic first, specific last
  await page.route('**/api/inventory**', route => route.fulfill({ json: INVENTORY }));
  await page.route('**/api/inventory/reorder-suggestions**', route => route.fulfill({ json: [FORECAST] }));
  await page.route('**/api/inventory/*/forecast**', route => route.fulfill({ json: FORECAST }));
  await page.route('**/api/inventory/*/adjust', route => route.fulfill({ json: { ...INVENTORY[1], onHand: INVENTORY[1].onHand + 10, available: INVENTORY[1].available + 10 } }));
  await page.route('**/api/inventory/*/replenish', route => route.fulfill({ json: { ...INVENTORY[1], onOrder: 60 } }));

  // Supply chain
  await page.route('**/api/supplychain/traces**', route => route.fulfill({ json: TRACE }));

  // Notifications - generic first, specific last
  await page.route('**/api/notifications**', route => route.fulfill({ json: NOTIFICATIONS }));
  await page.route('**/api/notifications/read-all', route => route.fulfill({ json: { updated: 2 } }));
  await page.route('**/api/notifications/*/read', route => route.fulfill({ json: { ...NOTIFICATIONS.items[0], read: true } }));

  // Stateful per page; registered after the generic users routes so they win.
  await mockAddressBook(page);
  await mockWishList(page);
  await mockCoupons(page);
}

/** Call BEFORE page.goto() — injects localStorage before page scripts run */
export function injectPersona(page: Page, persona: { id: string; name: string; persona: string }) {
  return page.addInitScript((p) => {
    localStorage.setItem('nakshatra_persona', JSON.stringify(p));
  }, persona);
}

export function injectCustomerPersona(page: Page) {
  return injectPersona(page, { id: 'u1', name: 'Alice Kumar', persona: 'Customer' });
}

export function injectAdminPersona(page: Page) {
  return injectPersona(page, { id: 'u3', name: 'Charlie Admin', persona: 'Admin' });
}

export function injectFulfillmentPersona(page: Page) {
  return injectPersona(page, { id: 'u4', name: 'Dave Fulfillment', persona: 'FulfillmentAgent' });
}

/** Use after navigation to switch persona then reload.
 *  Registers a new addInitScript (last wins) so the previous persona inject is overridden. */
export async function setPersonaAndReload(page: Page, persona: { id: string; name: string; persona: string }) {
  // Register a new initScript AFTER the existing one — last registered runs last and wins.
  await page.addInitScript((p) => {
    localStorage.setItem('nakshatra_persona', JSON.stringify(p));
  }, persona);
  await page.reload();
  await page.waitForLoadState('networkidle');
}
