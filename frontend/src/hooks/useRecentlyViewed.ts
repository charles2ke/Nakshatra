import { useEffect, useState } from 'react';
import { getProduct } from '../api/client';
import type { Product } from '../api/client';

const MAX_RECENT = 10;
const storageKey = (userId: string | undefined) => `nakshatra_recent_${userId ?? 'guest'}`;

function readIds(userId: string | undefined): string[] {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(storageKey(userId)) ?? '[]');
    return Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === 'string') : [];
  } catch { return []; }
}

/** Remembers, in this browser, that the shopper opened a product (newest first). */
export function recordRecentlyViewed(userId: string | undefined, productId: string) {
  const ids = [productId, ...readIds(userId).filter(id => id !== productId)].slice(0, MAX_RECENT);
  try { localStorage.setItem(storageKey(userId), JSON.stringify(ids)); } catch { /* storage is full or disabled */ }
}

/**
 * The products the shopper opened most recently. Only ids are stored, so prices and stock are
 * re-read from the catalog; products that can no longer be loaded are left out.
 */
export function useRecentlyViewed(userId: string | undefined) {
  const [loaded, setLoaded] = useState<{ userId: string | undefined; products: Product[] }>({ userId: undefined, products: [] });

  useEffect(() => {
    let current = true;
    Promise.allSettled(readIds(userId).map(id => getProduct(id))).then(results => {
      if (!current) return;
      setLoaded({ userId, products: results.flatMap(r => (r.status === 'fulfilled' ? [r.value] : [])) });
    });
    return () => { current = false; };
  }, [userId]);

  return loaded.userId === userId ? loaded.products : [];
}
