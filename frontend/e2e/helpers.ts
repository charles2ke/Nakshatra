import type { Page } from '@playwright/test';

export const PRODUCTS = [
  { id: 'p1', name: 'Silk Saree', description: 'Beautiful silk saree', category: 'Clothing', price: 2999, currency: 'INR', stock: 10, vendorId: 'v1', imageUrl: '', tags: ['silk', 'saree'] },
  { id: 'p2', name: 'Gold Necklace', description: 'Elegant gold necklace', category: 'Jewelry', price: 15000, currency: 'INR', stock: 5, vendorId: 'v1', imageUrl: '', tags: ['gold', 'necklace'] },
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

  // Search
  await page.route('**/api/search**', route => route.fulfill({ json: { items: PRODUCTS, total: 2, page: 1, pageSize: 12 } }));
  await page.route('**/api/search/suggest**', route => route.fulfill({ json: ['Silk', 'Gold'] }));

  // Cart - generic first, specific last
  await page.route('**/api/cart/**', route => route.fulfill({ json: CART }));
  await page.route('**/api/cart/**/items/**', route => route.fulfill({ json: { ...CART, items: [] } }));
  await page.route('**/api/cart/**/items', route => route.fulfill({ json: { cart: CART, recommendations: [PRODUCTS[1]] } }));

  // Recommendations
  await page.route('**/api/recommendations/**', route => route.fulfill({ json: [PRODUCTS[1]] }));

  // Orders - generic first, specific last
  await page.route('**/api/orders', route => route.fulfill({ json: ORDER }));
  await page.route('**/api/orders/**', route => route.fulfill({ json: ORDER_STATUS }));
  await page.route('**/api/orders/user/**', route => route.fulfill({ json: [ORDER] }));
  await page.route(`**/api/orders/${ORDER.id}/status`, route => route.fulfill({ json: ORDER_STATUS }));
  await page.route(`**/api/orders/${ORDER.id}`, route => route.fulfill({ json: ORDER }));

  // Fulfillment - generic first, specific last
  await page.route('**/api/fulfillment/shipments', route => route.fulfill({ json: SHIPMENTS }));
  await page.route('**/api/fulfillment/shipments/**/advance', route => route.fulfill({ json: { ...SHIPMENTS[0], status: 'Shipped' } }));

  // Payments
  await page.route('**/api/payments', route => route.fulfill({ json: { id: 'pay-001', orderId: ORDER.id, amount: 3539, currency: 'INR', status: 'Captured', method: 'Card', maskedInstrument: '**** 1234', createdAt: '2024-06-01T10:05:00Z' } }));
  await page.route('**/api/payments/**', route => route.fulfill({ json: { id: 'pay-001', orderId: ORDER.id, amount: 3539, currency: 'INR', status: 'Captured', method: 'Card', maskedInstrument: '**** 1234', createdAt: '2024-06-01T10:05:00Z' } }));

  // Billing
  await page.route('**/api/billing/invoices**', route => route.fulfill({ json: INVOICES }));
  await page.route('**/api/billing/invoices/**', route => route.fulfill({ json: INVOICES[0] }));
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
