import { TestBed } from '@angular/core/testing';
import { SseService, SseEvent } from './sse.service';

// ── MockEventSource ───────────────────────────────────────────────────────────

class MockEventSource {
  static lastInstance: MockEventSource | null = null;

  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  private listeners: Map<string, ((e: MessageEvent) => void)[]> = new Map();
  closed = false;
  readonly url: string;
  readonly withCredentials: boolean;

  constructor(url: string, opts?: EventSourceInit) {
    this.url = url;
    this.withCredentials = opts?.withCredentials ?? false;
    MockEventSource.lastInstance = this;
  }

  addEventListener(type: string, handler: (e: MessageEvent) => void) {
    if (!this.listeners.has(type)) this.listeners.set(type, []);
    this.listeners.get(type)!.push(handler);
  }

  simulateOpen() {
    this.onopen?.();
  }
  simulateError() {
    this.onerror?.();
  }
  simulateEvent(type: string, data: string) {
    const event = { data } as MessageEvent;
    (this.listeners.get(type) ?? []).forEach((h) => h(event));
  }

  close() {
    this.closed = true;
  }
}

describe('SseService', () => {
  let service: SseService;

  beforeEach(() => {
    MockEventSource.lastInstance = null;
    vi.stubGlobal('EventSource', MockEventSource);
    TestBed.configureTestingModule({});
    service = TestBed.inject(SseService);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('constructs EventSource with correct URL and withCredentials', () => {
    service.connect('tok-abc').subscribe();
    expect(MockEventSource.lastInstance?.url).toBe('/api/tokens/tok-abc/sse');
    expect(MockEventSource.lastInstance?.withCredentials).toBe(true);
  });

  it('emits connected event on onopen', () => {
    const events: SseEvent[] = [];
    service.connect('tok-1').subscribe((e) => events.push(e));
    MockEventSource.lastInstance?.simulateOpen();
    expect(events).toContainEqual({ eventType: 'connected' });
  });

  it('emits new-request event when request event is received', () => {
    const events: SseEvent[] = [];
    service.connect('tok-1').subscribe((e) => events.push(e));
    const payload = JSON.stringify({
      id: 'req-1',
      receivedAt: '2026-01-01T00:00:00Z',
      method: 'POST',
    });
    MockEventSource.lastInstance?.simulateEvent('request', payload);
    const newReq = events.find((e) => e.eventType === 'new-request');
    expect(newReq).toBeDefined();
    expect((newReq as Extract<SseEvent, { eventType: 'new-request' }>).data.id).toBe('req-1');
  });

  it('ignores non-JSON SSE data without throwing', () => {
    const events: SseEvent[] = [];
    service.connect('tok-1').subscribe((e) => events.push(e));
    expect(() => MockEventSource.lastInstance?.simulateEvent('request', 'not-json')).not.toThrow();
    expect(events.every((e) => e.eventType !== 'new-request')).toBe(true);
  });

  it('emits disconnected event on onerror', () => {
    const events: SseEvent[] = [];
    const sub = service.connect('tok-1').subscribe((e) => events.push(e));
    MockEventSource.lastInstance?.simulateError();
    expect(events).toContainEqual({ eventType: 'disconnected' });
    // Unsubscribe to clear the pending 1000 ms reconnect setTimeout. Without this the real
    // timer fires during a later test file and throws "EventSource is not defined" in jsdom.
    sub.unsubscribe();
  });

  it('calls close on EventSource when subscription is unsubscribed', () => {
    const sub = service.connect('tok-1').subscribe();
    const es = MockEventSource.lastInstance!;
    sub.unsubscribe();
    expect(es.closed).toBe(true);
  });

  it('emits token-deleted event and completes observable', () => {
    const events: SseEvent[] = [];
    let completed = false;
    service.connect('tok-1').subscribe({
      next: (e) => events.push(e),
      complete: () => (completed = true),
    });
    MockEventSource.lastInstance?.simulateEvent('token-deleted', '');
    expect(events).toContainEqual({ eventType: 'token-deleted' });
    expect(completed).toBe(true);
  });

  it('after onerror, schedules a reconnect (creates a new EventSource)', () => {
    vi.useFakeTimers();
    try {
      const events: SseEvent[] = [];
      service.connect('tok-1').subscribe((e) => events.push(e));
      const firstInstance = MockEventSource.lastInstance;
      expect(firstInstance).toBeDefined();

      MockEventSource.lastInstance?.simulateError();
      expect(firstInstance?.closed).toBe(true);
      expect(events).toContainEqual({ eventType: 'disconnected' });

      // Advance past the initial 1000 ms reconnect delay → service must construct a NEW EventSource
      vi.advanceTimersByTime(1100);
      expect(MockEventSource.lastInstance).not.toBe(firstInstance);
      expect(MockEventSource.lastInstance?.url).toBe('/api/tokens/tok-1/sse');
    } finally {
      vi.useRealTimers();
    }
  });

  it('does not reconnect after unsubscribe even if onerror is queued', () => {
    vi.useFakeTimers();
    try {
      const sub = service.connect('tok-1').subscribe();
      const firstInstance = MockEventSource.lastInstance;

      sub.unsubscribe();
      MockEventSource.lastInstance?.simulateError();

      vi.advanceTimersByTime(5000);
      // After unsubscribe, the service should NOT create a new EventSource
      expect(MockEventSource.lastInstance).toBe(firstInstance);
    } finally {
      vi.useRealTimers();
    }
  });
});
