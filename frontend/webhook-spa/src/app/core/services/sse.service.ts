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
      let stableTimer: ReturnType<typeof setTimeout> | null = null;
      let closed = false;

      const clearStableTimer = () => {
        if (stableTimer !== null) {
          clearTimeout(stableTimer);
          stableTimer = null;
        }
      };

      const open = () => {
        if (closed) return;
        es = new EventSource(`/api/tokens/${tokenId}/sse`, { withCredentials: true });

        es.onopen = () => {
          // Reset backoff only after 10 s of stability to avoid storm-reconnects (e.g. ngrok flap)
          stableTimer = setTimeout(() => {
            reconnectDelay = 1000;
            stableTimer = null;
          }, 10_000);
          subscriber.next({ eventType: 'connected' });
        };

        // Backend sends `event: request` (matches SseNotifier.NotifyAsync)
        es.addEventListener('request', (e: MessageEvent) => {
          let parsed: unknown;
          try {
            parsed = JSON.parse(e.data);
          } catch {
            console.warn('[SseService] Received non-JSON SSE data, ignoring', e.data);
            return;
          }
          if (
            typeof parsed !== 'object' ||
            parsed === null ||
            !('id' in parsed) ||
            !('receivedAt' in parsed)
          ) {
            console.warn('[SseService] Unexpected SSE payload shape, ignoring', parsed);
            return;
          }
          subscriber.next({ eventType: 'new-request', data: parsed as RequestSummary });
        });

        es.addEventListener('token-deleted', () => {
          subscriber.next({ eventType: 'token-deleted' });
          subscriber.complete();
          closed = true;
          es?.close();
        });

        es.onerror = () => {
          clearStableTimer();
          subscriber.next({ eventType: 'disconnected' });
          es?.close();
          if (!closed) {
            reconnectTimer = setTimeout(() => {
              reconnectDelay = Math.min(reconnectDelay * 2, 30_000);
              open();
            }, reconnectDelay);
          }
        };
      };

      open();

      return () => {
        closed = true;
        clearStableTimer();
        if (reconnectTimer !== null) clearTimeout(reconnectTimer);
        es?.close();
      };
    });
  }
}
