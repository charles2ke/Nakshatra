const BASE = (import.meta.env.VITE_API_BASE_URL as string) || 'http://localhost:5000';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
    ...init,
  });
  const text = await res.text();
  if (!res.ok) throw new Error(serverError(text) ?? `HTTP ${res.status}: ${res.statusText}`);
  // 204 No Content and other empty bodies carry no JSON.
  return (text ? JSON.parse(text) : undefined) as T;
}

/** The services explain 4xx/5xx responses with a `{ "error": "..." }` body. */
function serverError(body: string): string | undefined {
  try {
    const parsed: unknown = JSON.parse(body);
    if (parsed && typeof parsed === 'object' && 'error' in parsed && typeof parsed.error === 'string') return parsed.error;
  } catch { /* not JSON */ }
  return undefined;
}

/** A message suitable for showing to the user. */
export const errorMessage = (e: unknown) => (e instanceof Error ? e.message : String(e));

export type Persona = 'Customer' | 'Vendor' | 'Admin' | 'FulfillmentAgent';
export interface User { id: string; name: string; email: string; persona: Persona; createdAt: string; }
export interface Vendor { id: string; name: string; email: string; phone: string; address: string; rating: number; active: boolean; }
export interface Product {
  id: string; name: string; description: string; category: string; price: number; currency: string; stock: number; vendorId: string; imageUrl: string; tags: string[];
  /** Was-price; the product is on deal while it is above `price`. */
  listPrice?: number | null;
  averageRating?: number;
  reviewCount?: number;
  createdAt?: string;
}
export interface CartItem { productId: string; name: string; price: number; quantity: number; }
export interface Cart { userId: string; items: CartItem[]; savedForLater?: CartItem[]; subtotal: number; }
export type OrderStatus = 'Created'|'AwaitingPayment'|'Paid'|'Packed'|'Shipped'|'Delivered'|'Cancelled'|'ReturnRequested'|'Returned';
export type DeliverySpeed = 'Standard' | 'Expedited' | 'OneDay';
export interface OrderItem { productId: string; name: string; price: number; quantity: number; }
export interface Order {
  id: string; userId: string; items: OrderItem[]; subtotal: number; tax: number; total: number; currency: string; status: OrderStatus; shippingAddress: string; createdAt: string;
  discount?: number; couponCode?: string; deliverySpeed?: DeliverySpeed; shippingFee?: number; estimatedDelivery?: string | null;
  giftWrap?: boolean; giftWrapFee?: number; giftMessage?: string; buyNow?: boolean; returnReason?: string;
}
export interface StatusHistory { status: string; timestamp: string; note: string; }
export interface OrderStatusResponse { orderId: string; status: string; history: StatusHistory[]; }
export type ShipmentStatus = 'Pending'|'Packed'|'Shipped'|'Delivered'|'Cancelled';
export interface Shipment { orderId: string; status: ShipmentStatus; carrier: string; trackingNumber: string; updatedAt: string; }
export type PaymentMethod = 'Card'|'UPI'|'NetBanking';
export type PaymentStatus = 'Authorized'|'Captured'|'Failed'|'Refunded';
export interface Payment { id: string; orderId: string; amount: number; currency: string; status: PaymentStatus; method: PaymentMethod; maskedInstrument: string; createdAt: string; }
export interface InvoiceLine { description: string; amount: number; }
export interface Invoice { id: string; orderId: string; userId: string; lines: InvoiceLine[]; subtotal: number; tax: number; total: number; currency: string; issuedAt: string; paid: boolean; }
export interface SearchResult { items: Product[]; total: number; page: number; pageSize: number; }
export type SearchSort = 'relevance' | 'price-asc' | 'price-desc' | 'rating' | 'newest';
export interface SearchParams {
  q?: string; category?: string; minPrice?: number; maxPrice?: number; page?: number; pageSize?: number;
  minRating?: number; inStock?: boolean; deals?: boolean; sort?: SearchSort;
}

export const getUsers = () => request<User[]>('/api/users');
export const getUser = (id: string) => request<User>(`/api/users/${id}`);
export const createUser = (body: { name: string; email: string; persona: Persona }) => request<User>('/api/users', { method: 'POST', body: JSON.stringify(body) });
export const updateUser = (id: string, body: Partial<User>) => request<User>(`/api/users/${id}`, { method: 'PUT', body: JSON.stringify(body) });

export const getVendors = () => request<Vendor[]>('/api/vendors');
export const getVendor = (id: string) => request<Vendor>(`/api/vendors/${id}`);
export const createVendor = (body: Omit<Vendor, 'id'>) => request<Vendor>('/api/vendors', { method: 'POST', body: JSON.stringify(body) });
export const updateVendor = (id: string, body: Partial<Vendor>) => request<Vendor>(`/api/vendors/${id}`, { method: 'PUT', body: JSON.stringify(body) });

export const getProducts = () => request<Product[]>('/api/products');
export const getProduct = (id: string) => request<Product>(`/api/products/${id}`);
export const createProduct = (body: Omit<Product, 'id'>) => request<Product>('/api/products', { method: 'POST', body: JSON.stringify(body) });
export const updateProduct = (id: string, body: Partial<Product>) => request<Product>(`/api/products/${id}`, { method: 'PUT', body: JSON.stringify(body) });
export const deleteProduct = (id: string) => request<void>(`/api/products/${id}`, { method: 'DELETE' });
export const getCategories = () => request<string[]>('/api/products/categories');
/** Matches the catalog service: a product is on deal while its list price is above its price. */
export const isOnDeal = (p: Pick<Product, 'price' | 'listPrice'>) => p.listPrice != null && p.listPrice > p.price;
export const dealPercent = (p: Pick<Product, 'price' | 'listPrice'>) =>
  p.listPrice != null && p.listPrice > p.price ? Math.round((1 - p.price / p.listPrice) * 100) : 0;
/** At or below this many units the portal warns that a product is nearly sold out. */
export const LOW_STOCK_THRESHOLD = 5;

export const searchProducts = (params: SearchParams) => {
  const qs = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => v !== undefined && qs.set(k, String(v)));
  return request<SearchResult>(`/api/search?${qs}`);
};
export const searchSuggest = (q: string) => request<string[]>(`/api/search/suggest?q=${encodeURIComponent(q)}`);

export const getCart = (userId: string) => request<Cart>(`/api/cart/${userId}`);
export const addToCart = (userId: string, productId: string, quantity: number) =>
  request<{ cart: Cart; recommendations: Product[] }>(`/api/cart/${userId}/items`, { method: 'POST', body: JSON.stringify({ productId, quantity }) });
export const updateCartItem = (userId: string, productId: string, quantity: number) =>
  request<Cart>(`/api/cart/${userId}/items/${productId}`, { method: 'PUT', body: JSON.stringify({ productId, quantity }) });
export const removeFromCart = (userId: string, productId: string) => request<Cart>(`/api/cart/${userId}/items/${productId}`, { method: 'DELETE' });
export const clearCart = (userId: string) => request<Cart>(`/api/cart/${userId}`, { method: 'DELETE' });
export const saveForLater = (userId: string, productId: string) =>
  request<Cart>(`/api/cart/${userId}/items/${productId}/save-for-later`, { method: 'POST' });
export const moveSavedToCart = (userId: string, productId: string) =>
  request<Cart>(`/api/cart/${userId}/saved/${productId}/move-to-cart`, { method: 'POST' });
export const removeSaved = (userId: string, productId: string) => request<Cart>(`/api/cart/${userId}/saved/${productId}`, { method: 'DELETE' });

/** A wish list entry with the product's live price and stock; those are null while the catalog is unreachable. */
export interface WishListEntry {
  productId: string; name: string; priceWhenAdded: number; addedAt: string;
  price: number | null; currency: string | null; stock: number | null; imageUrl: string; priceDropped: boolean;
}
export interface WishList { id: string; userId: string; items: WishListEntry[]; }
export const getWishList = (userId: string) => request<WishList>(`/api/wishlist/${userId}`);
export const addToWishList = (userId: string, productId: string) =>
  request<WishList>(`/api/wishlist/${userId}/items`, { method: 'POST', body: JSON.stringify({ productId }) });
export const removeFromWishList = (userId: string, productId: string) =>
  request<WishList>(`/api/wishlist/${userId}/items/${productId}`, { method: 'DELETE' });
export const moveWishListItemToCart = (userId: string, productId: string) =>
  request<{ wishList: WishList; cart: Cart }>(`/api/wishlist/${userId}/items/${productId}/move-to-cart`, { method: 'POST' });

export interface Address {
  id: string; userId: string; fullName: string; line1: string; line2: string; city: string; state: string;
  postalCode: string; country: string; phone: string; isDefault: boolean; createdAt: string;
}
export type AddressInput = Omit<Address, 'id' | 'userId' | 'createdAt'>;
export const getAddresses = (userId: string) => request<Address[]>(`/api/users/${userId}/addresses`);
export const createAddress = (userId: string, body: AddressInput) =>
  request<Address>(`/api/users/${userId}/addresses`, { method: 'POST', body: JSON.stringify(body) });
export const updateAddress = (userId: string, id: string, body: AddressInput) =>
  request<Address>(`/api/users/${userId}/addresses/${id}`, { method: 'PUT', body: JSON.stringify(body) });
export const setDefaultAddress = (userId: string, id: string) =>
  request<Address>(`/api/users/${userId}/addresses/${id}/default`, { method: 'POST' });
export const deleteAddress = (userId: string, id: string) => request<void>(`/api/users/${userId}/addresses/${id}`, { method: 'DELETE' });
/** One-line form of an address, as stored on orders. */
export const formatAddress = (a: AddressInput) =>
  [a.fullName, a.line1, a.line2, a.city, [a.state, a.postalCode].filter(Boolean).join(' '), a.country]
    .map(part => part.trim())
    .filter(Boolean)
    .join(', ');

export const getAlsoPurchased = (productId: string, limit = 5) => request<Product[]>(`/api/recommendations/also-purchased/${productId}?limit=${limit}`);

export interface Review {
  id: string; productId: string; userId: string; title: string; body: string; rating: number; mediaIds: string[]; createdAt: string;
  verifiedPurchase?: boolean; helpfulCount?: number;
}
export const getReviews = (productId: string) => request<Review[]>(`/api/products/${productId}/reviews`);
export const createReview = (productId: string, body: { userId: string; title: string; body: string; rating: number }) =>
  request<Review>(`/api/products/${productId}/reviews`, { method: 'POST', body: JSON.stringify({ ...body, mediaIds: [] }) });
export const voteReviewHelpful = (productId: string, reviewId: string, userId: string) =>
  request<Review>(`/api/products/${productId}/reviews/${reviewId}/helpful`, { method: 'POST', body: JSON.stringify({ userId }) });

export interface ProductAnswer { id: string; userId: string; userName: string; text: string; isSeller: boolean; createdAt: string; }
export interface ProductQuestion { id: string; productId: string; userId: string; userName: string; text: string; createdAt: string; answers: ProductAnswer[]; }
export const getQuestions = (productId: string) => request<ProductQuestion[]>(`/api/products/${productId}/questions`);
export const askQuestion = (productId: string, body: { userId: string; userName: string; text: string }) =>
  request<ProductQuestion>(`/api/products/${productId}/questions`, { method: 'POST', body: JSON.stringify(body) });
export const answerQuestion = (productId: string, questionId: string, body: { userId: string; userName: string; text: string; persona: Persona }) =>
  request<ProductQuestion>(`/api/products/${productId}/questions/${questionId}/answers`, { method: 'POST', body: JSON.stringify(body) });

export const getStockAlert = (productId: string, userId: string) =>
  request<{ subscribed: boolean }>(`/api/products/${productId}/stock-alerts/${userId}`);
export const subscribeStockAlert = (productId: string, userId: string) =>
  request<unknown>(`/api/products/${productId}/stock-alerts`, { method: 'POST', body: JSON.stringify({ userId }) });
export const unsubscribeStockAlert = (productId: string, userId: string) =>
  request<void>(`/api/products/${productId}/stock-alerts/${userId}`, { method: 'DELETE' });

export interface CheckoutRequest {
  items: { productId: string; quantity: number }[];
  couponCode?: string;
  deliverySpeed?: DeliverySpeed;
  giftWrap?: boolean;
  giftMessage?: string;
  /** Bought straight from the product page, so the cart is left as it is. */
  buyNow?: boolean;
}
export interface DeliveryOption { speed: DeliverySpeed; fee: number; estimatedDelivery: string; }
export interface CheckoutQuote {
  items: OrderItem[]; subtotal: number; discount: number; couponCode: string; couponError: string | null;
  shippingFee: number; giftWrapFee: number; tax: number; total: number; currency: string;
  deliverySpeed: DeliverySpeed; estimatedDelivery: string; deliveryOptions: DeliveryOption[]; freeShippingThreshold: number;
}
export const quoteOrder = (body: CheckoutRequest) => request<CheckoutQuote>('/api/orders/quote', { method: 'POST', body: JSON.stringify(body) });
export const createOrder = (body: CheckoutRequest & { userId: string; shippingAddress: string }) =>
  request<Order>('/api/orders', { method: 'POST', body: JSON.stringify(body) });
export const getAllOrders = () => request<Order[]>('/api/orders');
export const getOrdersByUser = (userId: string) => request<Order[]>(`/api/orders/user/${userId}`);
export const getOrder = (id: string) => request<Order>(`/api/orders/${id}`);
export const getOrderStatus = (id: string) => request<OrderStatusResponse>(`/api/orders/${id}/status`);
export const cancelOrder = (id: string) => request<Order>(`/api/orders/${id}/cancel`, { method: 'POST' });
export const requestReturn = (id: string, reason: string) =>
  request<Order>(`/api/orders/${id}/return`, { method: 'POST', body: JSON.stringify({ reason }) });
export const completeReturn = (id: string) => request<Order>(`/api/orders/${id}/return/complete`, { method: 'POST' });
/** Matches the order service: delivered orders can be returned for this many days. */
export const RETURN_WINDOW_DAYS = 30;

export type CouponKind = 'Percent' | 'Fixed';
/** A promotion code; the id is the code in upper case. */
export interface Coupon { id: string; description: string; kind: CouponKind; value: number; minSubtotal: number; expiresAt: string | null; active: boolean; }
export interface CouponInput { code?: string; description: string; kind: CouponKind; value: number; minSubtotal: number; expiresAt: string | null; active: boolean; }
export const getCoupons = () => request<Coupon[]>('/api/coupons');
export const createCoupon = (body: CouponInput) => request<Coupon>('/api/coupons', { method: 'POST', body: JSON.stringify(body) });
export const updateCoupon = (code: string, body: CouponInput) =>
  request<Coupon>(`/api/coupons/${encodeURIComponent(code)}`, { method: 'PUT', body: JSON.stringify(body) });
export const deleteCoupon = (code: string) => request<void>(`/api/coupons/${encodeURIComponent(code)}`, { method: 'DELETE' });

export const getShipments = () => request<Shipment[]>('/api/fulfillment/shipments');
export const advanceShipment = (orderId: string) => request<Shipment>(`/api/fulfillment/shipments/${orderId}/advance`, { method: 'POST' });

export interface PaymentPayload { orderId: string; userId: string; amount: number; currency: string; method: PaymentMethod; cardNumber?: string; cardHolder?: string; expiry?: string; cvv?: string; }
export const createPayment = (body: PaymentPayload) => request<Payment>('/api/payments', { method: 'POST', body: JSON.stringify(body) });
export const getPayment = (id: string) => request<Payment>(`/api/payments/${id}`);

export const getInvoices = (userId: string) => request<Invoice[]>(`/api/billing/invoices?userId=${userId}`);
export const getInvoice = (orderId: string) => request<Invoice>(`/api/billing/invoices/${orderId}`);

export interface InventoryItem { id: string; productId: string; vendorId: string; warehouse: string; onHand: number; reserved: number; onOrder: number; leadTimeDays: number; available: number; updatedAt: string; }
export interface ForecastPoint { date: string; forecastUnits: number; }
export interface InventoryForecast {
  productId: string; horizonDays: number; averageDailyDemand: number; demandStdDev: number; trendPerDay: number;
  forecastDemand: number; safetyStock: number; reorderPoint: number; economicOrderQuantity: number;
  recommendedOrderQuantity: number; daysOfCoverRemaining: number; reorderNow: boolean; dailyForecast: ForecastPoint[];
}
export const getInventory = (vendorId?: string) =>
  request<InventoryItem[]>(`/api/inventory${vendorId ? `?vendorId=${encodeURIComponent(vendorId)}` : ''}`);
export const getInventoryForecast = (productId: string, horizonDays = 30) =>
  request<InventoryForecast>(`/api/inventory/${productId}/forecast?horizonDays=${horizonDays}`);
export const getReorderSuggestions = (horizonDays = 30) =>
  request<InventoryForecast[]>(`/api/inventory/reorder-suggestions?horizonDays=${horizonDays}`);
export const adjustInventory = (productId: string, delta: number, reason: string) =>
  request<InventoryItem>(`/api/inventory/${productId}/adjust`, { method: 'POST', body: JSON.stringify({ delta, reason }) });
export const replenishInventory = (productId: string, quantity: number) =>
  request<InventoryItem>(`/api/inventory/${productId}/replenish`, { method: 'POST', body: JSON.stringify({ quantity }) });

export type SupplyChainStage =
  | 'Sourcing' | 'ReplenishmentOrdered' | 'GoodsReceived' | 'OrderPlaced' | 'PaymentSettled'
  | 'Picked' | 'Packed' | 'HandedToCarrier' | 'InTransit' | 'OutForDelivery' | 'Delivered' | 'Exception'
  | 'ReturnRequested' | 'Returned';
export interface SupplyChainEvent { stage: SupplyChainStage; description: string; location: string; actor: string; timestamp: string; }
export interface SupplyChainTrace { id: string; reference: string; referenceType: string; userId: string; currentStage: SupplyChainStage; events: SupplyChainEvent[]; estimatedDelivery: string | null; updatedAt: string; }
export const getTrace = (reference: string) => request<SupplyChainTrace>(`/api/supplychain/traces/${reference}`);
export const getTraces = (params: { userId?: string; referenceType?: string } = {}) => {
  const qs = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => v !== undefined && qs.set(k, v));
  const query = qs.toString();
  return request<SupplyChainTrace[]>(`/api/supplychain/traces${query ? `?${query}` : ''}`);
};

export type NotificationSeverity = 'Info' | 'Warning' | 'Critical';
export interface AppNotification { id: string; userId: string; audience: string; title: string; body: string; reference: string; topic: string; channel: string; severity: NotificationSeverity; read: boolean; createdAt: string; }
export interface NotificationFeed { unreadCount: number; items: AppNotification[]; }
export const getNotifications = (params: { userId?: string; audience?: string; unreadOnly?: boolean } = {}) => {
  const qs = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => v !== undefined && qs.set(k, String(v)));
  const query = qs.toString();
  return request<NotificationFeed>(`/api/notifications${query ? `?${query}` : ''}`);
};
export const markNotificationRead = (id: string) => request<AppNotification>(`/api/notifications/${id}/read`, { method: 'POST' });
export const markAllNotificationsRead = (body: { userId?: string; audience?: string }) =>
  request<{ updated: number }>('/api/notifications/read-all', { method: 'POST', body: JSON.stringify(body) });
