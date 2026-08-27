import type { ReactNode } from 'react';
import { usePersona } from '../context/PersonaContext';
import { useLocale } from '../i18n/LocaleContext';
import type { Persona } from '../api/client';

export default function ProtectedRoute({ allow, children }: { allow: Persona[]; children: ReactNode }) {
  const { currentUser } = usePersona();
  const { t } = useLocale();
  if (!currentUser || !allow.includes(currentUser.persona)) {
    return <div data-testid="access-restricted" className="restricted">{t('access.restricted')}</div>;
  }
  return <>{children}</>;
}
