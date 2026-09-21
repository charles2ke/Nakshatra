import { useEffect, useRef, useState } from 'react';
import { usePersona } from '../context/PersonaContext';
import { getUsers } from '../api/client';
import { useLocale } from '../i18n/LocaleContext';
import type { User } from '../api/client';

export default function PersonaSwitcher() {
  const { currentUser, setCurrentUser } = usePersona();
  const { t } = useLocale();
  const [open, setOpen] = useState(false);
  const [users, setUsers] = useState<User[]>([]);
  const [loading, setLoading] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  async function openDropdown() {
    setOpen(o => !o);
    if (!users.length) {
      setLoading(true);
      try { setUsers(await getUsers()); } catch { /* ignore */ }
      setLoading(false);
    }
  }

  useEffect(() => {
    if (!open) return;
    function onKeyDown(e: KeyboardEvent) { if (e.key === 'Escape') setOpen(false); }
    function onPointerDown(e: MouseEvent) {
      if (!containerRef.current?.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener('keydown', onKeyDown);
    document.addEventListener('mousedown', onPointerDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      document.removeEventListener('mousedown', onPointerDown);
    };
  }, [open]);

  return (
    <div className="persona-switcher" data-testid="persona-switcher" ref={containerRef}>
      <button
        onClick={openDropdown}
        data-testid="persona-switcher-btn"
        aria-expanded={open}
        aria-controls={open ? 'persona-dropdown' : undefined}
      >
        {currentUser ? `${currentUser.name} (${currentUser.persona})` : t('persona.selectUser')}
      </button>
      {open && (
        <div id="persona-dropdown" className="persona-dropdown" data-testid="persona-dropdown">
          <button
            type="button"
            className="persona-option"
            data-testid="persona-sign-out"
            onClick={() => { setCurrentUser(null); setOpen(false); }}
          >
            {t('persona.signOut')}
          </button>
          {loading && <div className="persona-option">{t('persona.loading')}</div>}
          {users.map(u => (
            <button
              type="button"
              key={u.id}
              className="persona-option"
              data-testid={`persona-option-${u.id}`}
              onClick={() => { setCurrentUser({ id: u.id, name: u.name, persona: u.persona }); setOpen(false); }}
            >
              {u.name} <span className="persona-badge">{u.persona}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
