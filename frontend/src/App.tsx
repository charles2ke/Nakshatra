import { BrowserRouter, Routes, Route } from 'react-router-dom';
import Layout from './components/Layout';
import HomePage from './pages/HomePage';
import SearchPage from './pages/SearchPage';
import ProductPage from './pages/ProductPage';
import CartPage from './pages/CartPage';
import CheckoutPage from './pages/CheckoutPage';
import OrdersPage from './pages/OrdersPage';
import OrderDetailPage from './pages/OrderDetailPage';
import BillingPage from './pages/BillingPage';
import FulfillmentPage from './pages/FulfillmentPage';
import InventoryPage from './pages/InventoryPage';
import NotificationsPage from './pages/NotificationsPage';
import SetupProductsPage from './pages/setup/SetupProductsPage';
import SetupVendorsPage from './pages/setup/SetupVendorsPage';
import SetupUsersPage from './pages/setup/SetupUsersPage';
import SetupCouponsPage from './pages/setup/SetupCouponsPage';
import WishlistPage from './pages/WishlistPage';
import AddressesPage from './pages/AddressesPage';
import ProtectedRoute from './components/ProtectedRoute';
import { BadgeProvider } from './context/BadgeContext';

export default function App() {
  return (
    <BrowserRouter basename={import.meta.env.BASE_URL}>
      <BadgeProvider>
        <Routes>
          <Route path="/" element={<Layout />}>
            <Route index element={<HomePage />} />
            <Route path="search" element={<SearchPage />} />
            <Route path="product/:id" element={<ProductPage />} />
            <Route path="cart" element={<ProtectedRoute allow={['Customer','Admin']}><CartPage /></ProtectedRoute>} />
            <Route path="checkout" element={<ProtectedRoute allow={['Customer','Admin']}><CheckoutPage /></ProtectedRoute>} />
            <Route path="wishlist" element={<ProtectedRoute allow={['Customer','Admin']}><WishlistPage /></ProtectedRoute>} />
            <Route path="addresses" element={<ProtectedRoute allow={['Customer','Admin']}><AddressesPage /></ProtectedRoute>} />
            <Route path="orders" element={<ProtectedRoute allow={['Customer','Vendor','Admin','FulfillmentAgent']}><OrdersPage /></ProtectedRoute>} />
            <Route path="orders/:id" element={<ProtectedRoute allow={['Customer','Vendor','Admin','FulfillmentAgent']}><OrderDetailPage /></ProtectedRoute>} />
            <Route path="billing" element={<ProtectedRoute allow={['Customer','Vendor','Admin']}><BillingPage /></ProtectedRoute>} />
            <Route path="fulfillment" element={<ProtectedRoute allow={['FulfillmentAgent','Admin']}><FulfillmentPage /></ProtectedRoute>} />
            <Route path="inventory" element={<ProtectedRoute allow={['Vendor','Admin']}><InventoryPage /></ProtectedRoute>} />
            <Route path="notifications" element={<ProtectedRoute allow={['Customer','Vendor','Admin','FulfillmentAgent']}><NotificationsPage /></ProtectedRoute>} />
            <Route path="setup/products" element={<ProtectedRoute allow={['Vendor','Admin']}><SetupProductsPage /></ProtectedRoute>} />
            <Route path="setup/vendors" element={<ProtectedRoute allow={['Admin']}><SetupVendorsPage /></ProtectedRoute>} />
            <Route path="setup/users" element={<ProtectedRoute allow={['Admin']}><SetupUsersPage /></ProtectedRoute>} />
            <Route path="setup/coupons" element={<ProtectedRoute allow={['Admin']}><SetupCouponsPage /></ProtectedRoute>} />
          </Route>
        </Routes>
      </BadgeProvider>
    </BrowserRouter>
  );
}
