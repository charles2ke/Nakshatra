import { useState, useEffect, useCallback, useRef, useLayoutEffect } from 'react';
import { getNotifications, markNotificationRead, markAllNotificationsRead, errorMessage } from '../api/client';
import type { AppNotification } from '../api/client';
import { usePersona } from '../context/PersonaContext';
import { useBadges } from '../context/BadgeContext';
import { useLocale } from '../i18n/LocaleContext';

export default function NotificationsPage() {
  const { t, formatDate } = useLocale();
  const { currentUser } = usePersona();
  const { refreshBadges, syncUnread } = useBadges();
  const [items, setItems] = useState<AppNotification[]>([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const contextKey = currentUser ? `${currentUser.id}:${currentUser.persona}:${unreadOnly}` : '';
  const activeContext = useRef(contextKey);

  const load = useCallback(async () => {
    if (!currentUser) return;
    const requestContext = contextKey;
    if (activeContext.current !== requestContext) return;
    setLoading(true);
    try {
      const feed = await getNotifications({ userId: currentUser.id, audience: currentUser.persona, unreadOnly });
      if (activeContext.current !== requestContext) return;
      setItems(feed.items);
      setUnreadCount(feed.unreadCount);
      syncUnread(feed.unreadCount);
    } catch (e) {
      if (activeContext.current === requestContext) setError(errorMessage(e));
    }
    if (activeContext.current === requestContext) setLoading(false);
  }, [contextKey, currentUser, unreadOnly, syncUnread]);

  useLayoutEffect(() => { activeContext.current = contextKey; }, [contextKey]);
  useEffect(() => { load(); }, [load]);

  async function handleRead(id: string) {
    const requestContext = contextKey;
    setError('');
    try {
      const updated = await markNotificationRead(id);
      if (activeContext.current !== requestContext) return;
      setItems(prev => prev.map(n => n.id === id ? updated : n));
      setUnreadCount(c => Math.max(c - 1, 0));
      refreshBadges();
    } catch (e) {
      if (activeContext.current === requestContext) setError(errorMessage(e));
    }
  }

  async function handleReadAll() {
    if (!currentUser) return;
    const requestContext = contextKey;
    setError('');
    try {
      await markAllNotificationsRead({ userId: currentUser.id, audience: currentUser.persona });
      if (activeContext.current !== requestContext) return;
      await load();
    } catch (e) {
      if (activeContext.current === requestContext) setError(errorMessage(e));
    }
  }

  if (!currentUser) return <div data-testid="notifications-no-user">{t('notifications.noUser')}</div>;

  return (
    <div data-testid="notifications-page">
      <h1>{t('notifications.title')}</h1>
      <div data-testid="notifications-unread-count">{t('notifications.unread', { count: String(unreadCount) })}</div>
      {error && <div className="error" role="alert" data-testid="notifications-error">{error}</div>}
      <div className="notifications-toolbar">
        <label>
          <input
            type="checkbox"
            data-testid="notifications-unread-toggle"
            checked={unreadOnly}
            onChange={e => setUnreadOnly(e.target.checked)}
          />
          {t('notifications.unreadOnly')}
        </label>
        <button data-testid="notifications-read-all" onClick={handleReadAll}>{t('notifications.readAll')}</button>
      </div>
      {loading && <div>{t('notifications.loading')}</div>}
      {!loading && items.length === 0 && <div data-testid="notifications-empty">{t('notifications.empty')}</div>}
      <div data-testid="notifications-list">
        {items.map(n => (
          <div
            key={n.id}
            className={`notification-card severity-${n.severity.toLowerCase()}${n.read ? ' read' : ''}`}
            data-testid={`notification-${n.id}`}
          >
            <div className="notification-title">{n.title}</div>
            <div>{n.body}</div>
            <div className="timeline-time">{formatDate(n.createdAt)}</div>
            {!n.read && (
              <button data-testid={`notification-read-${n.id}`} onClick={() => handleRead(n.id)}>
                {t('notifications.markRead')}
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
