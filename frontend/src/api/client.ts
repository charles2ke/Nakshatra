const BASE = (import.meta.env.VITE_API_BASE_URL as string) || 'http://localhost:5000';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
    ...init,
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}: ${res.statusText}`);
  return res.json() as Promise<T>;
}

export type Persona = 'Customer' | 'Vendor' | 'Admin' | 'FulfillmentAgent';
export interface User { id: string; name: string; email: string; persona: Persona; createdAt: string; }
export interface Vendor { id: string; name: string; email: string; phone: string; address: string; rating: number; active: boolean; }
export interface Product { id: string; name: string; description: string; category: string; price: number; currency: string; stock: number; vendorId: string; imageUrl: string; tags: string[]; }
export interface CartItem { productId: string; name: string; price: number; quantity: number; }
export interface Cart { userId: string; items: CartItem[]; subtotal: number; }
export type OrderStatus = 'Created'|'AwaitingPayment'|'Paid'|'Packed'|'Shipped'|'Delivered'|'Cancelled';
export interface OrderItem { productId: string; name: string; price: number; quantity: number; }
export interface Order { id: string; userId: string; items: OrderItem[]; subtotal: number; tax: number; total: number; currency: string; status: OrderStatus; shippingAddress: string; createdAt: string; }
export interface StatusHistory { status: string; timestamp: string; note: string; }
export interface OrderStatusResponse { orderId: string; status: string; history: StatusHistory[]; }
export type ShipmentStatus = 'Pending'|'Packed'|'Shipped'|'Delivered';
export interface Shipment { orderId: string; status: ShipmentStatus; carrier: string; trackingNumber: string; updatedAt: string; }
export type PaymentMethod = 'Card'|'UPI'|'NetBanking';
export type PaymentStatus = 'Authorized'|'Captured'|'Failed'|'Refunded';
export interface Payment { id: string; orderId: string; amount: number; currency: string; status: PaymentStatus; method: PaymentMethod; maskedInstrument: string; createdAt: string; }
export interface InvoiceLine { description: string; amount: number; }
export interface Invoice { id: string; orderId: string; userId: string; lines: InvoiceLine[]; subtotal: number; tax: number; total: number; currency: string; issuedAt: string; paid: boolean; }
export interface SearchResult { items: Product[]; total: number; page: number; pageSize: number; }
export interface SearchParams { q?: string; category?: string; minPrice?: number; maxPrice?: number; page?: number; pageSize?: number; }

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

export const searchProducts = (params: SearchParams) => {
  const qs = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => v !== undefined && qs.set(k, String(v)));
  return request<SearchResult>(`/api/search?${qs}`);
};
export const searchSuggest = (q: string) => request<string[]>(`/api/search/suggest?q=${encodeURIComponent(q)}`);

export const getCart = (userId: string) => request<Cart>(`/api/cart/${userId}`);
export const addToCart = (userId: string, productId: string, quantity: number) =>
  request<{ cart: Cart; recommendations: Product[] }>(`/api/cart/${userId}/items`, { method: 'POST', body: JSON.stringify({ productId, quantity }) });
export const removeFromCart = (userId: string, productId: string) => request<Cart>(`/api/cart/${userId}/items/${productId}`, { method: 'DELETE' });
export const clearCart = (userId: string) => request<Cart>(`/api/cart/${userId}`, { method: 'DELETE' });

export const getAlsoPurchased = (productId: string, limit = 5) => request<Product[]>(`/api/recommendations/also-purchased/${productId}?limit=${limit}`);

export const createOrder = (body: { userId: string; items: { productId: string; quantity: number }[]; shippingAddress: string }) =>
  request<Order>('/api/orders', { method: 'POST', body: JSON.stringify(body) });
export const getOrdersByUser = (userId: string) => request<Order[]>(`/api/orders/user/${userId}`);
export const getOrder = (id: string) => request<Order>(`/api/orders/${id}`);
export const getOrderStatus = (id: string) => request<OrderStatusResponse>(`/api/orders/${id}/status`);

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
  | 'Picked' | 'Packed' | 'HandedToCarrier' | 'InTransit' | 'OutForDelivery' | 'Delivered' | 'Exception';
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
