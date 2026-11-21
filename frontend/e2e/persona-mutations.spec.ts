import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { ADDRESSES, CART, injectCustomerPersona, mockAllApis } from './helpers';

function gate() {
  let release!: () => void;
  const wait = new Promise<void>(resolve => { release = resolve; });
  return { wait, release };
}

async function switchTo(page: Page, id: string) {
  await page.getByTestId('persona-switcher-btn').click();
  await page.getByTestId(`persona-option-${id}`).click();
}

test('address form and pending mutation are isolated by user', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  const pending = gate();
  await page.route('**/api/users/*/addresses', route =>
    route.fulfill({ json: route.request().url().includes('/u1/') ? ADDRESSES : [] }));
  await page.route('**/api/users/u1/addresses/addr-1', async route => {
    await pending.wait;
    await route.fulfill({ json: ADDRESSES[0] });
  });
  await page.goto('/addresses');
  await page.getByTestId('address-edit-addr-1').click();
  await page.getByTestId('address-city').fill('Private draft');
  await page.getByTestId('address-submit').click();
  await switchTo(page, 'u3');
  await expect(page.getByTestId('addresses-empty')).toBeVisible();
  await expect(page.getByTestId('address-form')).toHaveCount(0);
  await page.getByTestId('address-add').click();
  await expect(page.getByTestId('address-city')).toHaveValue('');
  pending.release();
  await expect(page.getByTestId('address-form')).toBeVisible();
  await expect(page.getByTestId('addresses-toast')).toHaveCount(0);
  await expect(page.getByTestId('address-submit')).toBeEnabled();
  await switchTo(page, 'u1');
  await expect(page.getByTestId('address-form')).toHaveCount(0);
  await expect(page.getByTestId('address-addr-1')).toBeVisible();
});

test('cart mutations block all conflicting controls until the whole cart response arrives', async ({ page }) => {
  await mockAllApis(page);
  await injectCustomerPersona(page);
  const pending = gate();
  const cart = { ...CART, items: [...CART.items, { productId: 'p2', name: 'Gold Necklace', price: 15000, quantity: 1 }],
    subtotal: 17999, savedForLater: [{ productId: 'p3', name: 'Diya', price: 999, quantity: 1 }] };
  await page.route('**/api/cart/u1', route => route.fulfill({ json: cart }));
  await page.route('**/api/cart/u1/items/p1', async route => {
    await pending.wait;
    await route.fulfill({ json: { ...cart, items: [{ ...cart.items[0], quantity: 2 }, cart.items[1]], subtotal: 20998 } });
  });
  await page.route('**/api/cart/u1/items/p2', route =>
    route.fulfill({ json: { ...cart, items: [{ ...cart.items[0], quantity: 2 }], subtotal: 5998 } }));
  await page.goto('/cart');
  await page.getByTestId('qty-increase-p1').click();
  for (const id of ['qty-increase-p1', 'qty-increase-p2', 'remove-p1', 'remove-p2', 'save-for-later-p1',
    'save-for-later-p2', 'move-to-cart-p3', 'remove-saved-p3', 'checkout-btn']) {
    await expect(page.getByTestId(id)).toBeDisabled();
  }
  await page.screenshot({ path: 'e2e/screenshots/cart-mutation-pending.png', fullPage: true });
  pending.release();
  await expect(page.getByTestId('qty-p1')).toHaveText('2');
  await expect(page.getByTestId('nav-cart-badge')).toHaveText('3');
  await expect(page.getByTestId('remove-p2')).toBeEnabled();
  await page.getByTestId('remove-p2').click();
  await expect(page.getByTestId('cart-item-p2')).toHaveCount(0);
  await expect(page.getByTestId('qty-p1')).toHaveText('2');
  await expect(page.getByTestId('nav-cart-badge')).toHaveText('2');
});

for (const kind of ['move', 'remove'] as const) {
  for (const fails of [false, true]) {
    test(`stale wishlist ${kind} ${fails ? 'failure' : 'success'} cannot affect the new persona`, async ({ page }) => {
      await mockAllApis(page);
      await injectCustomerPersona(page);
      const old = gate();
      const current = gate();
      const oldDone = gate();
      const endpoint = kind === 'move' ? 'move-to-cart' : '';
      await page.route(`**/api/wishlist/*/items/p2${endpoint ? `/${endpoint}` : ''}`, async route => {
        const isOld = route.request().url().includes('/u1/');
        await (isOld ? old.wait : current.wait);
        const list = { id: isOld ? 'u1' : 'u3', userId: isOld ? 'u1' : 'u3', items: [] };
        await route.fulfill(fails && isOld ? { status: 500, json: { error: 'Old failure' } } :
          { json: kind === 'move' ? { wishList: list, cart: { ...CART, items: [...CART.items, CART.items[0]] } } : list });
        if (isOld) oldDone.release();
      });
      await page.goto('/wishlist');
      await page.getByTestId(`wishlist-${kind === 'move' ? 'move-to-cart' : 'remove'}-p2`).click();
      await switchTo(page, 'u3');
      await expect(page.getByTestId('wishlist-item-p2')).toBeVisible();
      await expect(page.getByTestId('nav-cart-badge')).toHaveText('1');
      await page.getByTestId(`wishlist-${kind === 'move' ? 'move-to-cart' : 'remove'}-p2`).click();
      await expect(page.getByTestId('wishlist-remove-p2')).toBeDisabled();
      const oldResponse = page.waitForResponse(response => response.url().includes('/api/wishlist/u1/items/p2'));
      old.release();
      await oldDone.wait;
      await (await oldResponse).finished();
      await page.evaluate(() => new Promise(resolve => setTimeout(resolve, 0)));
      await expect(page.getByTestId('wishlist-item-p2')).toBeVisible();
      await expect(page.getByTestId('wishlist-toast')).toHaveCount(0);
      await expect(page.getByTestId('nav-cart-badge')).toHaveText('1');
      await expect(page.getByTestId('wishlist-remove-p2')).toBeDisabled();
      current.release();
      await expect(page.getByTestId('wishlist-empty')).toBeVisible();
      await expect(page.getByTestId('wishlist-toast')).toBeVisible();
    });
  }
}
