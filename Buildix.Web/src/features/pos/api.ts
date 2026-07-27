import { apiClient } from '@/shared/api/client';
import type { PagedResult } from '@/shared/api/paged';
import { productsApi, type Product, type ProductQuery } from '@/features/warehouse/api';

/** A line on a POS sale (SaleItemDto). */
export interface PosSaleItem {
  id: string;
  saleId: string;
  productId: string | null;
  productName: string;
  quantity: number;
  costPrice: number;
  salePrice: number;
  totalPrice: number;
  profit: number;
  /** Server-side Uzbek abbreviation — prefer `unitValue` + unitLabel() for display. */
  unit: string;
  /** UnitType number; 0 for external lines. Localise from this. */
  unitValue: number;
  comment: string | null;
  isExternal: boolean;
}

/** A payment recorded against a sale (PaymentDto). */
export interface PosPayment {
  paymentType: string;
  amount: number;
}

/** A POS sale (SaleDto). */
export interface PosSale {
  id: string;
  saleNumber: number;
  sellerId: string;
  sellerName: string;
  customerId: string | null;
  customerName: string | null;
  customerPhone: string | null;
  status: string;
  totalAmount: number;
  paidAmount: number;
  remainingAmount: number;
  discountAmount: number;
  createdAt: string;
  items: PosSaleItem[];
  payments: PosPayment[];
}

/** A customer (CustomerDto). */
export interface PosCustomer {
  id: string;
  phone: string;
  fullName: string | null;
  comment: string | null;
  totalDebt: number;
  customerType: string;
  isRegular: boolean;
  debtLimit: number | null;
}

export interface AddSaleItemBody {
  isExternal: boolean;
  productId?: string | null;
  externalProductName?: string | null;
  externalCostPrice?: number | null;
  quantity: number;
  salePrice: number;
  minSalePrice: number;
  comment?: string | null;
}

export interface AddPaymentBody {
  paymentType: string;
  amount: number;
  dueDate?: string | null;
}

export interface CreateCustomerBody {
  phone: string;
  fullName?: string | null;
  comment?: string | null;
  initialDebt?: number | null;
  customerType?: string | null;
  isRegular?: boolean;
  debtLimit?: number | null;
}

export type { Product };

export const posApi = {
  /** Create a fresh Draft sale (seller taken from JWT). */
  createDraft: async (customerId: string | null = null): Promise<PosSale> => {
    const { data } = await apiClient.post<PosSale>('/Sales', { customerId });
    return data;
  },

  /** Read the authoritative state of a sale. */
  getSale: async (saleId: string): Promise<PosSale> => {
    const { data } = await apiClient.get<PosSale>(`/Sales/${saleId}`);
    return data;
  },

  /** Search products for the POS grid (reuses the paged Products endpoint). */
  searchProducts: (q: ProductQuery): Promise<PagedResult<Product>> => productsApi.listPaged(q),

  /** Add a product (or free-form item) to the sale. Returns the affected line. */
  addItem: async (saleId: string, body: AddSaleItemBody): Promise<PosSaleItem> => {
    const { data } = await apiClient.post<PosSaleItem>(`/Sales/${saleId}/items`, body);
    return data;
  },

  /** Reduce a line's quantity (or remove it fully). */
  removeItem: async (saleId: string, saleItemId: string, quantity: number): Promise<PosSaleItem> => {
    const { data } = await apiClient.post<PosSaleItem>(`/Sales/${saleId}/items/remove`, {
      saleItemId,
      quantity,
    });
    return data;
  },

  /**
   * Set a line to an EXACT quantity — decimals allowed (12, 3.5, 0.25).
   * The register's primary quantity path: «+» N times cannot express a
   * fraction, and for "30 qop" it is 30 round-trips. Quantity 0 drops the line.
   * The server takes the difference from the row it reads under the product
   * lock, so a stale client-side quantity cannot double-apply.
   */
  setItemQuantity: async (saleId: string, saleItemId: string, quantity: number): Promise<PosSaleItem> => {
    const { data } = await apiClient.patch<PosSaleItem>(`/Sales/${saleId}/items/quantity`, {
      saleItemId,
      quantity,
    });
    return data;
  },

  /** Override one line's price (торг). Audited server-side; needs sales.edit. */
  updateItemPrice: async (saleItemId: string, newPrice: number, comment?: string | null): Promise<PosSaleItem> => {
    const { data } = await apiClient.patch<PosSaleItem>('/Sales/items/price', {
      saleItemId,
      newPrice,
      comment: comment ?? null,
    });
    return data;
  },

  /** Set (or clear, with 0) the sale-level discount. */
  setDiscount: async (saleId: string, discountAmount: number): Promise<PosSale> => {
    const { data } = await apiClient.patch<PosSale>(`/Sales/${saleId}/discount`, { discountAmount });
    return data;
  },

  /** Attach (or detach with null) the customer on the sale. */
  attachCustomer: async (saleId: string, customerId: string | null): Promise<PosSale> => {
    const { data } = await apiClient.patch<PosSale>(`/Sales/${saleId}/customer`, { customerId });
    return data;
  },

  /** Take a payment. The sale finalizes automatically once paid >= total. */
  addPayment: async (saleId: string, body: AddPaymentBody): Promise<PosPayment> => {
    const { data } = await apiClient.post<PosPayment>(`/Sales/${saleId}/payments`, body);
    return data;
  },

  /**
   * Close the sale with several tenders at once (Микс). One transaction on the
   * server — two sequential addPayment calls would be rejected on a walk-in sale
   * and would transiently mark the sale as debt in between.
   */
  checkout: async (
    saleId: string,
    tenders: { paymentType: string; amount: number }[],
    dueDate?: string | null,
  ): Promise<PosPayment> => {
    const { data } = await apiClient.post<PosPayment>(`/Sales/${saleId}/checkout`, {
      tenders,
      dueDate: dueDate ?? null,
    });
    return data;
  },

  /** Mark the whole sale as debt (fully unpaid). */
  markDebt: async (saleId: string, dueDate?: string | null): Promise<PosSale> => {
    const { data } = await apiClient.post<PosSale>(
      `/Sales/${saleId}/mark-debt`,
      dueDate ? { dueDate } : {},
    );
    return data;
  },

  /**
   * The seller's own parked (Draft) sales — the "Отложенные чеки" strip. A
   * parked receipt is just a Draft left in place: nothing extra is stored, and
   * resuming one is a plain getSale on its id.
   */
  myDrafts: async (): Promise<PosSale[]> => {
    const { data } = await apiClient.get<PosSale[]>('/Sales/my-drafts');
    return data;
  },

  /** Discard one of the caller's own parked (Draft) receipts. Separate from the
   *  general DELETE /Sales/{id}, which needs sales.delete — a permission that
   *  also reverses PAID sales and is deliberately withheld from cashiers. */
  deleteMyDraft: async (saleId: string): Promise<void> => {
    await apiClient.delete(`/Sales/my-drafts/${saleId}`);
  },

  /**
   * Kassa cheki — TERMAL rulon (58/80 mm) uchun PDF. Kassadagi «Chek chiqarish»
   * shuni oladi: A4 faktura rulonli printerda siqilib yoki kesilib chiqardi.
   */
  receiptPdf: async (saleId: string, lang: string, widthMm = 80): Promise<Blob> => {
    const { data } = await apiClient.get(`/Sales/${saleId}/receipt`, {
      params: { lang, width: widthMm },
      responseType: 'blob',
    });
    return data as Blob;
  },

  /** A4 faktura (ofis hujjati). Fetched through apiClient so the JWT is attached —
   *  a bare window.open would hit the endpoint unauthenticated. */
  invoicePdf: async (saleId: string, lang: string): Promise<Blob> => {
    const { data } = await apiClient.get(`/Sales/${saleId}/invoice`, {
      params: { lang },
      responseType: 'blob',
    });
    return data as Blob;
  },

  /** Look up a single customer by phone. */
  customerByPhone: async (phone: string): Promise<PosCustomer> => {
    const { data } = await apiClient.get<PosCustomer>(
      `/Customers/GetCustomerByPhone/phone/${encodeURIComponent(phone)}`,
    );
    return data;
  },

  /** Paged customer search (by name or phone). */
  searchCustomers: async (search: string, size = 20): Promise<PagedResult<PosCustomer>> => {
    const { data } = await apiClient.get<PagedResult<PosCustomer>>('/Customers/GetCustomersPaged', {
      params: { page: 1, size, search: search || undefined },
    });
    return data;
  },

  /** Create a new customer. */
  createCustomer: async (body: CreateCustomerBody): Promise<PosCustomer> => {
    const { data } = await apiClient.post<PosCustomer>('/Customers/CreateCustomer', body);
    return data;
  },
};
