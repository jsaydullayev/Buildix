import { QueryClient } from '@tanstack/react-query';
import type { ApiError } from '@/shared/api/types';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => {
        const status = (error as unknown as ApiError)?.status ?? 0;
        // Never retry auth/permission/client errors; retry transient ones twice.
        if (status >= 400 && status < 500) return false;
        return failureCount < 2;
      },
    },
    mutations: { retry: false },
  },
});
