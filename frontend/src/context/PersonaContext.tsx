import { createContext, useContext, useState } from 'react';
import type { ReactNode } from 'react';
import type { Persona } from '../api/client';

export interface CurrentUser { id: string; name: string; persona: Persona; }

interface PersonaContextValue {
  currentUser: CurrentUser | null;
  setCurrentUser: (u: CurrentUser | null) => void;
}

const PersonaContext = createContext<PersonaContextValue>({
  currentUser: null,
  setCurrentUser: () => {},
});

const LS_KEY = 'nakshatra_persona';

export function PersonaProvider({ children }: { children: ReactNode }) {
  const [currentUser, setCurrentUserState] = useState<CurrentUser | null>(() => {
    try { return JSON.parse(localStorage.getItem(LS_KEY) || 'null'); } catch { return null; }
  });

  function setCurrentUser(u: CurrentUser | null) {
    setCurrentUserState(u);
    if (u) localStorage.setItem(LS_KEY, JSON.stringify(u));
    else localStorage.removeItem(LS_KEY);
  }

  return (
    <PersonaContext.Provider value={{ currentUser, setCurrentUser }}>
      {children}
    </PersonaContext.Provider>
  );
}

export function usePersona() { return useContext(PersonaContext); }
