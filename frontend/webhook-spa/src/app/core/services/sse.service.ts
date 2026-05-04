import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { RequestSummary } from '../models/request-summary.model';

export type SseEvent =
  | { eventType: 'new-request'; data: RequestSummary }
  | { eventType: 'token-deleted' };

@Injectable({ providedIn: 'root' })
export class SseService {
  connect(tokenId: string): Observable<SseEvent> {
    return new Observable<SseEvent>(subscriber => {
      const es = new EventSource(`/api/events/${tokenId}`);

      es.addEventListener('new-request', (e: MessageEvent) => {
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

      // EventSource auto-reconnects using `retry:` interval sent by server
      es.onerror = () => {};

      return () => es.close();
    });
  }
}
