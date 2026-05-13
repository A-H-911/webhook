export interface CustomResponse {
  statusCode: number;
  contentType: string;
  body: string | null;
  headers: string;
}

export interface Token {
  id: string;
  token: string;
  name: string;
  webhookUrl: string;
  description: string | null;
  createdAt: string;
  isActive: boolean;
  customResponse: CustomResponse | null;
}

export interface TokenListItem {
  id: string;
  token: string;
  name: string;
  webhookUrl: string;
  description: string | null;
  createdAt: string;
  isActive: boolean;
  hasCustomResponse: boolean;
  lifetimeRequestCount: number;
  requestCount24h: number;
  sparkline24h: number[];
  lastReceivedAt: string | null;
}

export interface TokensPage {
  items: TokenListItem[];
  total: number;
  hasMore: boolean;
}

export interface SetCustomResponseDto {
  statusCode: number;
  contentType: string;
  body: string | null;
  headers: string;
}
