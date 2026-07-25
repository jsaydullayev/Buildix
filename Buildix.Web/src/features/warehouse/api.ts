import { apiClient } from '@/shared/api/client';
import type { PagedResult } from '@/shared/api/paged';

export interface Product {
  id: string;
  name: string;
  costPrice: number;
  salePrice: number;
  minSalePrice: number;
  quantity: number;
  minThreshold: number;
  unit: number;
  unitName: string;
  categoryId: number | null;
  categoryName: string | null;
  isTemporary: boolean;
  isInStock: boolean;
  isLowStock: boolean;
  imageUrl: string | null;
  hidePriceFromSellers: boolean;
  sku: string | null;
  /** Seller-visible short description (Товары "Описание"). */
  description: string | null;
  /** Hidden from POS / seller catalog (still in reports). Distinct from hidePriceFromSellers. */
  isHidden: boolean;
  /** «Место на складе» — storage location (МЕСТО column + seller card). */
  warehouseLocation: string | null;
  /** Склад «ПОСЛ. ПРИХОД» — last accepted goods-receipt date + number (null = none). */
  lastReceiptAt: string | null;
  lastReceiptNumber: number | null;
}

/** Seller product card stats (supplier / last receipt / sold this month). */
export interface ProductStats {
  supplierName: string | null;
  lastReceiptAt: string | null;
  lastReceiptNumber: number | null;
  soldThisMonth: number;
}

export interface ProductCategory {
  id: number;
  name: string;
  description?: string | null;
  icon?: string | null;
  isActive?: boolean;
  productCount?: number;
}

export interface CategoryBody {
  name: string;
  description?: string | null;
  icon?: string | null;
}

/** GET /Products/summary — warehouse KPI tiles. */
export interface WarehouseSummary {
  positions: number;
  /** null when the caller lacks data.costPrice. */
  stockValue: number | null;
  lowStock: number;
  outOfStock: number;
}

export interface UnitInfo {
  value: number;
  nameUz: string;
  nameEn: string;
  nameRu: string;
}

export interface ProductQuery {
  page?: number;
  size?: number;
  search?: string;
  categoryId?: number | null;
  lowStockOnly?: boolean;
  /** Admin Товары/Склад pass true to see hidden products; POS/seller omit it. */
  includeHidden?: boolean;
}

export interface CreateProductBody {
  name: string;
  sku?: string | null;
  salePrice: number;
  minSalePrice: number;
  costPrice: number;
  quantity: number;
  minThreshold: number;
  unit: number;
  categoryId: number | null;
  isTemporary: boolean;
  hidePriceFromSellers: boolean;
  description?: string | null;
}

export interface StocktakeItem {
  productId: string;
  countedQty: number;
}

export interface StocktakeResult {
  adjustedCount: number;
  lines: {
    productId: string;
    name: string;
    before: number;
    counted: number;
    variance: number;
  }[];
}

export const productsApi = {
  listPaged: async (q: ProductQuery): Promise<PagedResult<Product>> => {
    const { data } = await apiClient.get<PagedResult<Product>>('/Products/GetAllProductsPaged', {
      params: {
        page: q.page ?? 1,
        size: q.size ?? 50,
        search: q.search || undefined,
        categoryId: q.categoryId ?? undefined,
        lowStockOnly: q.lowStockOnly || undefined,
        includeHidden: q.includeHidden || undefined,
      },
    });
    return data;
  },

  /** Inline edit of a single field (sale price / min stock / visibility). Only
   *  the given fields change; the server audits price & visibility changes. */
  patch: async (
    id: string,
    body: { salePrice?: number; minThreshold?: number; isHidden?: boolean },
  ): Promise<Product> => {
    const { data } = await apiClient.patch<Product>(`/Products/PatchProduct/${id}`, body);
    return data;
  },

  create: async (body: CreateProductBody): Promise<Product> => {
    const { data } = await apiClient.post<Product>('/Products/CreateProduct', body);
    return data;
  },

  update: async (
    id: string,
    body: Omit<CreateProductBody, 'quantity'> & { quantity: number | null },
  ): Promise<Product> => {
    // Controller uses [Route("api/[controller]/[action]")] + [HttpPut("{id}")]
    // → real route keeps the action segment.
    const { data } = await apiClient.put<Product>(`/Products/UpdateProduct/${id}`, { id, ...body });
    return data;
  },

  remove: async (id: string): Promise<void> => {
    await apiClient.delete(`/Products/DeleteProduct/${id}`);
  },

  exportExcel: async (lang: string): Promise<Blob> => {
    const { data } = await apiClient.get('/Products/export', { params: { lang }, responseType: 'blob' });
    return data as Blob;
  },

  /** Warehouse KPI tiles, aggregated server-side (no full-list download). */
  summary: async (): Promise<WarehouseSummary> => {
    const { data } = await apiClient.get<WarehouseSummary>('/Products/summary');
    return data;
  },

  /** Unpaged list (server caps at 5000) — used to compute warehouse stat cards. */
  listAll: async (): Promise<Product[]> => {
    const { data } = await apiClient.get<Product[]>('/Products/GetAllProducts');
    return data;
  },

  units: async (): Promise<UnitInfo[]> => {
    const { data } = await apiClient.get<UnitInfo[]>('/Products/GetUnits/units');
    return data;
  },

  stocktake: async (items: StocktakeItem[]): Promise<StocktakeResult> => {
    const { data } = await apiClient.post<StocktakeResult>('/Products/stocktake', { items });
    return data;
  },

  /** Stock-movement ledger for one product ("Движение товара"), newest first. */
  movements: async (productId: string, limit = 50): Promise<StockMovement[]> => {
    const { data } = await apiClient.get<StockMovement[]>(`/Products/${productId}/movements`, {
      params: { limit },
    });
    return data;
  },

  stats: async (productId: string): Promise<ProductStats> => {
    const { data } = await apiClient.get<ProductStats>(`/Products/${productId}/stats`);
    return data;
  },
};

/** One line of the stock-movement ledger (see Buildix.Domain StockMovement). */
export interface StockMovement {
  id: string;
  /** InitialStock | Purchase | Sale | SaleReversal | Correction */
  type: string;
  delta: number;
  resultingQty: number;
  /** Source document number (Ч-#### / З-###) or null. */
  refNumber: number | null;
  userName: string | null;
  comment: string | null;
  createdAt: string;
}

export const categoriesApi = {
  list: async (): Promise<ProductCategory[]> => {
    const { data } = await apiClient.get<ProductCategory[]>('/ProductCategories/GetAllCategories');
    return data;
  },

  create: async (body: CategoryBody): Promise<ProductCategory> => {
    const { data } = await apiClient.post<ProductCategory>('/ProductCategories/CreateCategory', body);
    return data;
  },

  update: async (id: number, body: CategoryBody & { isActive: boolean }): Promise<ProductCategory> => {
    const { data } = await apiClient.put<ProductCategory>('/ProductCategories/UpdateCategory', { id, ...body });
    return data;
  },

  remove: async (id: number): Promise<void> => {
    await apiClient.delete(`/ProductCategories/DeleteCategory/${id}`);
  },
};
