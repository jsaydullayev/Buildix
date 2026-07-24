import { apiClient } from '@/shared/api/client';

export interface NotificationItem {
  id: string;
  /** Warehouse | Debt | Shift | Supply */
  category: string;
  /** Info | Success | Warning | Danger */
  severity: string;
  title: string;
  text: string;
  /** Internal route target (e.g. "warehouse", "debts") or null. */
  actionTarget: string | null;
  isRead: boolean;
  createdAt: string;
}

export interface NotificationFeed {
  unreadCount: number;
  items: NotificationItem[];
}

export const notificationsApi = {
  feed: async (category?: string | null): Promise<NotificationFeed> => {
    const { data } = await apiClient.get<NotificationFeed>('/Notifications', {
      params: { category: category || undefined },
    });
    return data;
  },

  unreadCount: async (): Promise<number> => {
    const { data } = await apiClient.get<{ count: number }>('/Notifications/unread-count');
    return data.count;
  },

  markRead: async (id: string): Promise<void> => {
    await apiClient.post(`/Notifications/${id}/read`);
  },

  markAllRead: async (): Promise<void> => {
    await apiClient.post('/Notifications/read-all');
  },
};
