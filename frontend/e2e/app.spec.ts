import { test, expect } from '@playwright/test';
import { mockAllApis, injectCustomerPersona, injectAdminPersona, injectFulfillmentPersona, setPersonaAndReload, ORDER } from './helpers';
import path from 'path';

const ss = (name: string) => path.join('e2e', 'screenshots', `${name}.png`);

test('home page renders product grid and search', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.goto('/');
  await page.waitForSelector('[data-testid="home-page"]');
  await expect(page.locator('[data-testid="home-search-input"]')).toBeVisible();
  await expect(page.locator('[data-testid="product-grid"]')).toBeVisible();
  const cards = page.locator('[data-testid^="product-card-"]');
  await expect(cards).toHaveCount(2);
  await page.screenshot({ path: ss('home-page') });
});

test('home search shows results', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.goto('/');
  await page.fill('[data-testid="home-search-input"]', 'Silk');
  await page.click('[data-testid="home-search-btn"]');
  await expect(page.locator('[data-testid="product-grid"]')).toBeVisible();
  await page.screenshot({ path: ss('home-search') });
});

test('add to cart shows recommendations', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.goto('/');
  await page.waitForSelector('[data-testid^="add-to-cart-"]');
  await page.click('[data-testid="add-to-cart-p1"]');
  await expect(page.locator('[data-testid="recommendation-strip"]')).toBeVisible();
  await page.screenshot({ path: ss('add-to-cart-recs') });
});

test('search page renders with filters', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.goto('/search');
  await expect(page.locator('[data-testid="search-page"]')).toBeVisible();
  await expect(page.locator('[data-testid="search-filters"]')).toBeVisible();
  await expect(page.locator('[data-testid="search-results"]')).toBeVisible();
  await page.screenshot({ path: ss('search-page') });
});

test('checkout and payment flow', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.goto('/checkout');
  await expect(page.locator('[data-testid="checkout-page"]')).toBeVisible();
  await page.fill('[data-testid="checkout-address"]', '123 Main St, Mumbai');
  await page.screenshot({ path: ss('checkout-address') });
  await page.click('[data-testid="place-order-btn"]');
  await expect(page.locator('[data-testid="checkout-payment-step"]')).toBeVisible();
  await page.fill('[data-testid="card-number"]', '4111111111111111');
  await page.fill('[data-testid="card-holder"]', 'Alice Kumar');
  await page.fill('[data-testid="card-expiry"]', '12/26');
  await page.fill('[data-testid="card-cvv"]', '123');
  await page.screenshot({ path: ss('checkout-payment') });
  await page.click('[data-testid="pay-btn"]');
  await page.waitForURL(/\/orders\//);
  await page.screenshot({ path: ss('order-after-payment') });
});

test('order status page shows timeline', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.goto(`/orders/${ORDER.id}`);
  await expect(page.locator('[data-testid="order-detail-page"]')).toBeVisible();
  await expect(page.locator('[data-testid="order-timeline"]')).toBeVisible();
  await expect(page.locator('[data-testid="timeline-item-0"]')).toBeVisible();
  await page.screenshot({ path: ss('order-status-timeline') });
});

test('orders list page', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.goto('/orders');
  await expect(page.locator('[data-testid="orders-page"]')).toBeVisible();
  await page.screenshot({ path: ss('orders-list') });
});

test('persona switching changes nav', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.goto('/');
  await expect(page.locator('[data-testid="nav-cart"]')).toBeVisible();
  await expect(page.locator('[data-testid="nav-fulfillment"]')).not.toBeVisible();
  await page.screenshot({ path: ss('persona-customer-nav') });

  await setPersonaAndReload(page, { id: 'u4', name: 'Dave Fulfillment', persona: 'FulfillmentAgent' });
  await expect(page.locator('[data-testid="nav-fulfillment"]')).toBeVisible();
  await expect(page.locator('[data-testid="nav-cart"]')).not.toBeVisible();
  await page.screenshot({ path: ss('persona-fulfillment-nav') });
});

test('setup products CRUD form renders', async ({ page }) => {
  await mockAllApis(page);
  await injectAdminPersona(page);
  await page.goto('/setup/products');
  await expect(page.locator('[data-testid="setup-products-page"]')).toBeVisible();
  await expect(page.locator('[data-testid="product-form"]')).toBeVisible();
  await expect(page.locator('[data-testid="products-table"]')).toBeVisible();
  await page.fill('[data-testid="product-form-name"]', 'Test Product');
  await page.fill('[data-testid="product-form-category"]', 'Clothing');
  await page.fill('[data-testid="product-form-price"]', '999');
  await page.screenshot({ path: ss('setup-products') });
});

test('fulfillment page shows shipments board', async ({ page }) => {
  await mockAllApis(page);
  await injectFulfillmentPersona(page);
  await page.goto('/fulfillment');
  await expect(page.locator('[data-testid="fulfillment-page"]')).toBeVisible();
  await expect(page.locator('[data-testid="shipments-list"]')).toBeVisible();
  await page.screenshot({ path: ss('fulfillment-board') });
});

test('persona switcher dropdown works', async ({ page }) => {
  await mockAllApis(page);
  await page.goto('/');
  await page.click('[data-testid="persona-switcher-btn"]');
  await expect(page.locator('[data-testid="persona-dropdown"]')).toBeVisible();
  await page.screenshot({ path: ss('persona-switcher') });
});

test('access restricted for wrong persona', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  await page.goto('/fulfillment');
  await expect(page.locator('[data-testid="access-restricted"]')).toBeVisible();
  await page.screenshot({ path: ss('access-restricted') });
});
