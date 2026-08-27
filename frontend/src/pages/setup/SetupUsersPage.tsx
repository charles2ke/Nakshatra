import { useState, useEffect } from 'react';
import { getUsers, createUser, updateUser } from '../../api/client';
import type { User, Persona } from '../../api/client';

const EMPTY = { name:'', email:'', persona:'Customer' as Persona };

export default function SetupUsersPage() {
  const [users, setUsers] = useState<User[]>([]);
  const [form, setForm] = useState(EMPTY);
  const [editId, setEditId] = useState<string|null>(null);
  const [error, setError] = useState('');

  async function load() { try { setUsers(await getUsers()); } catch { /* ignore */ } }
  useEffect(() => { load(); }, []);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      if (editId) { await updateUser(editId, form); } else { await createUser(form); }
      setForm(EMPTY); setEditId(null); load();
    } catch (e) { setError(String(e)); }
  }

  return (
    <div data-testid="setup-users-page">
      <h1>Users Setup</h1>
      {error && <div className="error">{error}</div>}
      <form onSubmit={handleSubmit} data-testid="user-form" className="setup-form">
        <input data-testid="user-form-name" placeholder="Name" required value={form.name} onChange={e => setForm(f => ({...f, name: e.target.value}))} />
        <input data-testid="user-form-email" placeholder="Email" type="email" required value={form.email} onChange={e => setForm(f => ({...f, email: e.target.value}))} />
        <select data-testid="user-form-persona" value={form.persona} onChange={e => setForm(f => ({...f, persona: e.target.value as Persona}))}>
          <option value="Customer">Customer</option>
          <option value="Vendor">Vendor</option>
          <option value="Admin">Admin</option>
          <option value="FulfillmentAgent">FulfillmentAgent</option>
        </select>
        <button type="submit" data-testid="user-form-submit">{editId ? 'Update' : 'Create'}</button>
        {editId && <button type="button" onClick={() => { setEditId(null); setForm(EMPTY); }}>Cancel</button>}
      </form>
      <table className="setup-table" data-testid="users-table">
        <thead><tr><th>Name</th><th>Email</th><th>Persona</th><th>Actions</th></tr></thead>
        <tbody>
          {users.map(u => (
            <tr key={u.id} data-testid={`user-row-${u.id}`}>
              <td>{u.name}</td><td>{u.email}</td><td>{u.persona}</td>
              <td><button data-testid={`edit-user-${u.id}`} onClick={() => { setEditId(u.id); setForm({name:u.name,email:u.email,persona:u.persona}); }}>Edit</button></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
