import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import App from './App';
import { PersonaProvider } from './context/PersonaContext';
import { LocaleProvider } from './i18n/LocaleContext';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <PersonaProvider>
      <LocaleProvider>
        <App />
      </LocaleProvider>
    </PersonaProvider>
  </StrictMode>,
);
