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
}

export interface ProductCategory {
  id: number;
  name: string;
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
      },
    });
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
};

export const categoriesApi = {
  list: async (): Promise<ProductCategory[]> => {
    const { data } = await apiClient.get<ProductCategory[]>('/ProductCategories/GetAllCategories');
    return data;
  },
};
