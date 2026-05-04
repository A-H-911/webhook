export interface RequestDetail {
  id: string;
  tokenId: string;
  method: string;
  path: string;
  queryString: string | null;
  receivedAt: string;
  contentType: string | null;
  headers: string;
  body: string | null;
  isBodyBase64: boolean;
  sizeBytes: number;
  ipAddress: string;
  userAgent: string | null;
}
