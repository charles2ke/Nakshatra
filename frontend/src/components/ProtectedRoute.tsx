import type { ReactNode } from 'react';
import { usePersona } from '../context/PersonaContext';
import type { Persona } from '../api/client';

export default function ProtectedRoute({ allow, children }: { allow: Persona[]; children: ReactNode }) {
  const { currentUser } = usePersona();
  if (!currentUser || !allow.includes(currentUser.persona)) {
    return <div data-testid="access-restricted" className="restricted">Access restricted for your persona.</div>;
  }
  return <>{children}</>;
}
