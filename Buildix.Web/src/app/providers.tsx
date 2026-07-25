import type { ReactNode } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from 'react-router-dom';
import { router } from './router';
import { queryClient } from './queryClient';
import { useBootstrap } from '@/shared/auth/useBootstrap';
import { ConfirmProvider } from '@/shared/ui';

function BootstrapGate({ children }: { children: ReactNode }) {
  useBootstrap();
  return <>{children}</>;
}

export function Providers() {
  return (
    <QueryClientProvider client={queryClient}>
      <ConfirmProvider>
        <BootstrapGate>
          <RouterProvider router={router} />
        </BootstrapGate>
      </ConfirmProvider>
    </QueryClientProvider>
  );
}
