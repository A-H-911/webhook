import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { RequestSummary, PagedResult } from '../models/request-summary.model';
import { RequestDetail } from '../models/request-detail.model';

@Injectable({ providedIn: 'root' })
export class RequestService {
  private readonly http = inject(HttpClient);

  getRequests(
    tokenId: string,
    page = 1,
    pageSize = 20,
    search = '',
  ): Observable<PagedResult<RequestSummary>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search.trim()) params = params.set('search', search.trim());
    return this.http.get<PagedResult<RequestSummary>>(`/api/tokens/${tokenId}/requests`, {
      params,
    });
  }

  getRequestDetail(tokenId: string, requestId: string): Observable<RequestDetail> {
    return this.http.get<RequestDetail>(`/api/tokens/${tokenId}/requests/${requestId}`);
  }

  deleteRequest(tokenId: string, requestId: string): Observable<void> {
    return this.http.delete<void>(`/api/tokens/${tokenId}/requests/${requestId}`);
  }

  clearRequests(tokenId: string): Observable<void> {
    return this.http.delete<void>(`/api/tokens/${tokenId}/requests`);
  }

  exportRequest(tokenId: string, requestId: string): void {
    const a = document.createElement('a');
    a.href = `/api/tokens/${tokenId}/requests/${requestId}/export`;
    a.download = `request-${requestId}.json`;
    a.click();
  }

  updateNote(tokenId: string, requestId: string, note: string | null): Observable<void> {
    return this.http.patch<void>(`/api/tokens/${tokenId}/requests/${requestId}/note`, { note });
  }
}
