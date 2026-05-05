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
      let es: EventSource | null = null;
      let reconnectDelay = 1000;
      let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
      let closed = false;

      const open = () => {
        if (closed) return;
        es = new EventSource(`/api/tokens/${tokenId}/sse`);

        es.onopen = () => {
          reconnectDelay = 1000;
          subscriber.next({ eventType: 'connected' });
        };

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
          closed = true;
          es?.close();
        });

        es.onerror = () => {
          subscriber.next({ eventType: 'disconnected' });
          es?.close();
          if (!closed) {
            reconnectTimer = setTimeout(() => {
              reconnectDelay = Math.min(reconnectDelay * 2, 30000);
              open();
            }, reconnectDelay);
          }
        };
      };

      open();

      return () => {
        closed = true;
        if (reconnectTimer !== null) clearTimeout(reconnectTimer);
        es?.close();
      };
    });
  }
}
