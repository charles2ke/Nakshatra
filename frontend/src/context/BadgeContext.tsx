import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { useLocation } from 'react-router-dom';
import { getCart, getNotifications } from '../api/client';
import type { Cart } from '../api/client';
import { usePersona } from './PersonaContext';

interface BadgeContextValue {
  cartCount: number;
  unreadCount: number;
  /** Re-reads both counts from the services, e.g. after an order empties the cart. */
  refreshBadges: () => void;
  /** Updates the cart badge from a cart the page already has. */
  syncCart: (cart: Cart) => void;
  /** Updates the notifications badge from a feed the page already has. */
  syncUnread: (count: number) => void;
}

const BadgeContext = createContext<BadgeContextValue>({
  cartCount: 0,
  unreadCount: 0,
  refreshBadges: () => {},
  syncCart: () => {},
  syncUnread: () => {},
});

// Counts remember which persona they were read for, so switching persona never shows someone else's count.
interface Count { owner: string; value: number; }

const NO_COUNT: Count = { owner: '', value: 0 };

const quantityOf = (cart: Cart) => cart.items.reduce((sum, item) => sum + item.quantity, 0);

export function BadgeProvider({ children }: { children: ReactNode }) {
  const { currentUser } = usePersona();
  const { pathname } = useLocation();
  const owner = currentUser ? `${currentUser.id}:${currentUser.persona}` : '';
  const [cart, setCart] = useState<Count>(NO_COUNT);
  const [unread, setUnread] = useState<Count>(NO_COUNT);
  // Bumped on every update so a slow response cannot overwrite a newer count.
  const cartVersion = useRef(0);
  const unreadVersion = useRef(0);

  const refreshBadges = useCallback(() => {
    if (!currentUser) return;
    const reader = `${currentUser.id}:${currentUser.persona}`;
    if (currentUser.persona === 'Customer' || currentUser.persona === 'Admin') {
      const version = ++cartVersion.current;
      getCart(currentUser.id)
        .then(c => { if (cartVersion.current === version) setCart({ owner: reader, value: quantityOf(c) }); })
        .catch(() => {});
    }
    const version = ++unreadVersion.current;
    getNotifications({ userId: currentUser.id, audience: currentUser.persona, unreadOnly: true })
      .then(feed => { if (unreadVersion.current === version) setUnread({ owner: reader, value: feed.unreadCount }); })
      .catch(() => {});
  }, [currentUser]);

  // Orders empty the cart and events raise notifications behind the scenes, so re-read on every navigation.
  useEffect(() => { refreshBadges(); }, [refreshBadges, pathname]);

  const syncCart = useCallback((c: Cart) => {
    cartVersion.current++;
    setCart({ owner, value: quantityOf(c) });
  }, [owner]);

  const syncUnread = useCallback((count: number) => {
    unreadVersion.current++;
    setUnread({ owner, value: count });
  }, [owner]);

  const value = useMemo(() => ({
    cartCount: cart.owner === owner ? cart.value : 0,
    unreadCount: unread.owner === owner ? unread.value : 0,
    refreshBadges,
    syncCart,
    syncUnread,
  }), [cart, unread, owner, refreshBadges, syncCart, syncUnread]);

  return <BadgeContext.Provider value={value}>{children}</BadgeContext.Provider>;
}

export function useBadges() { return useContext(BadgeContext); }
