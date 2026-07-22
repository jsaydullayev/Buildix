import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import '@/shared/i18n';
import { Providers } from '@/app/providers';

const rootEl = document.getElementById('root');
if (!rootEl) throw new Error('Root element #root not found');

createRoot(rootEl).render(
  <StrictMode>
    <Providers />
  </StrictMode>,
);
