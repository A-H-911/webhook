export interface CustomResponse {
  statusCode: number;
  contentType: string;
  body: string | null;
  headers: string;
}

export interface Token {
  id: string;
  token: string;
  webhookUrl: string;
  description: string | null;
  createdAt: string;
  isActive: boolean;
  customResponse: CustomResponse | null;
}

export interface SetCustomResponseDto {
  statusCode: number;
  contentType: string;
  body: string | null;
  headers: Record<string, string>;
}
