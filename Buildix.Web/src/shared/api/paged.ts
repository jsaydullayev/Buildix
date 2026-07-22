/** Standard pagination envelope from the API (PagedResult<T>). */
export interface PagedResult<T> {
  items: T[];
  page: number;
  size: number;
  total: number;
  totalPages: number;
}
