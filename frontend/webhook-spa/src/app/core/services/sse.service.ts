import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { RequestSummary } from '../models/request-summary.model';

export type SseEvent =
  | { eventType: 'connected' }
  | { eventType: 'disconnected' }
  | { eventType: 'new-request'; data: RequestSummary }
  | { eventType: 'token-deleted' };

@Injectable({ providedIn: 'root' })
export class SseService {
  connect(tokenId: string): Observable<SseEvent> {
    return new Observable<SseEvent>((subscriber) => {
      const es = new EventSource(`/api/tokens/${tokenId}/sse`);

      es.onopen = () => subscriber.next({ eventType: 'connected' });

      // Backend sends `event: request` (matches SseNotifier.NotifyAsync)
      es.addEventListener('request', (e: MessageEvent) => {
        try {
          subscriber.next({ eventType: 'new-request', data: JSON.parse(e.data) });
        } catch {
          // malformed SSE payload — skip
        }
      });

      es.addEventListener('token-deleted', () => {
        subscriber.next({ eventType: 'token-deleted' });
        subscriber.complete();
        es.close();
      });

      es.onerror = () => subscriber.next({ eventType: 'disconnected' });

      return () => es.close();
    });
  }
}
