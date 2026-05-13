import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Token, TokensPage, SetCustomResponseDto } from '../models/token.model';

@Injectable({ providedIn: 'root' })
export class TokenService {
  private readonly http = inject(HttpClient);

  getTokens(skip = 0, take = 50): Observable<TokensPage> {
    return this.http.get<TokensPage>('/api/tokens', { params: { skip, take } });
  }

  getToken(id: string): Observable<Token> {
    return this.http.get<Token>(`/api/tokens/${id}`);
  }

  createToken(name: string, description?: string): Observable<Token> {
    return this.http.post<Token>('/api/tokens', { name, description: description ?? null });
  }

  deleteToken(id: string): Observable<void> {
    return this.http.delete<void>(`/api/tokens/${id}`);
  }

  setCustomResponse(tokenId: string, dto: SetCustomResponseDto): Observable<void> {
    return this.http.put<void>(`/api/tokens/${tokenId}/custom-response`, dto);
  }

  resetCustomResponse(tokenId: string): Observable<void> {
    return this.http.delete<void>(`/api/tokens/${tokenId}/custom-response`);
  }
}
