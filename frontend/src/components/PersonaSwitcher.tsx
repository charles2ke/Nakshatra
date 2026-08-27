import { useState } from 'react';
import { usePersona } from '../context/PersonaContext';
import { getUsers } from '../api/client';
import type { User } from '../api/client';

export default function PersonaSwitcher() {
  const { currentUser, setCurrentUser } = usePersona();
  const [open, setOpen] = useState(false);
  const [users, setUsers] = useState<User[]>([]);
  const [loading, setLoading] = useState(false);

  async function openDropdown() {
    setOpen(o => !o);
    if (!users.length) {
      setLoading(true);
      try { setUsers(await getUsers()); } catch { /* ignore */ }
      setLoading(false);
    }
  }

  return (
    <div className="persona-switcher" data-testid="persona-switcher">
      <button onClick={openDropdown} data-testid="persona-switcher-btn">
        {currentUser ? `${currentUser.name} (${currentUser.persona})` : 'Select User'}
      </button>
      {open && (
        <div className="persona-dropdown" data-testid="persona-dropdown">
          <div className="persona-option" onClick={() => { setCurrentUser(null); setOpen(false); }}>— Sign out —</div>
          {loading && <div>Loading…</div>}
          {users.map(u => (
            <div key={u.id} className="persona-option" data-testid={`persona-option-${u.id}`}
              onClick={() => { setCurrentUser({ id: u.id, name: u.name, persona: u.persona }); setOpen(false); }}>
              {u.name} <span className="persona-badge">{u.persona}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
