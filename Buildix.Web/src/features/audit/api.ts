import { apiClient } from '@/shared/api/client';
import type { PagedResult } from '@/shared/api/paged';

/** One audit-log entry (AuditLogDto). */
export interface AuditLog {
  id: string;
  entityType: string;
  entityId: string;
  action: string;
  userId: string | null;
  userName: string | null;
  /** JSON string with the action's details. */
  payload: string;
  ipAddress: string | null;
  marketId: number | null;
  createdAt: string;
}

export interface AuditQuery {
  entityType?: string | null;
  action?: string | null;
  userId?: string | null;
  from?: string | null;
  to?: string | null;
  page?: number;
  size?: number;
}

export interface FailedLoginBurst {
  username: string;
  count: number;
  firstSeenUtc: string;
  lastSeenUtc: string;
  ipAddresses: string[];
}

export interface BulkDeleteBurst {
  userId: string;
  userName: string | null;
  count: number;
  firstSeenUtc: string;
  lastSeenUtc: string;
  entityTypes: string[];
}

export interface ErrorEntry {
  statusCode: number;
  message: string;
  path: string | null;
  method: string | null;
  userName: string | null;
  createdAt: string;
}

export interface SuspiciousReport {
  failedLoginBursts: FailedLoginBurst[];
  bulkDeleteBursts: BulkDeleteBurst[];
  recentErrors: ErrorEntry[];
}

export const auditApi = {
  query: async (q: AuditQuery): Promise<PagedResult<AuditLog>> => {
    const { data } = await apiClient.get<PagedResult<AuditLog>>('/audit-logs', {
      params: {
        entityType: q.entityType || undefined,
        action: q.action || undefined,
        userId: q.userId || undefined,
        from: q.from || undefined,
        to: q.to || undefined,
        page: q.page ?? 1,
        size: q.size ?? 50,
      },
    });
    return data;
  },

  suspicious: async (): Promise<SuspiciousReport> => {
    const { data } = await apiClient.get<SuspiciousReport>('/audit-logs/suspicious');
    return data;
  },
};
