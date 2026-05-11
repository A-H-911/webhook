export interface RequestSummary {
  id: string;
  tokenId: string;
  method: string;
  path: string;
  receivedAt: string;
  contentType?: string | null;
  sizeBytes?: number;
  ipAddress?: string;
  statusCode?: number;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}
