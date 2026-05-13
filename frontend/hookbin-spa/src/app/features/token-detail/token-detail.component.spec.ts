import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute } from '@angular/router';
import { of, Observable, EMPTY, Subject, throwError } from 'rxjs';
import { Router } from '@angular/router';
import { ModalService } from '../../shared/modal/modal.service';
import { ToastService } from '../../shared/toast/toast.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { SseEvent } from '../../core/services/sse.service';

import { TokenDetailComponent } from './token-detail.component';
import { TokenService } from '../../core/services/token.service';
import { RequestService } from '../../core/services/request.service';
import { SseService } from '../../core/services/sse.service';
import { RequestDetail } from '../../core/models/request-detail.model';
import { Token } from '../../core/models/token.model';
import { RequestSummary } from '../../core/models/request-summary.model';

// ── Fixtures ─────────────────────────────────────────────────────────────────

function makeDetail(overrides: Partial<RequestDetail> = {}): RequestDetail {
  return {
    id: 'req-1',
    tokenId: 'tok-1',
    method: 'POST',
    path: '/webhook/tok-1',
    queryString: null,
    receivedAt: '2026-01-01T00:00:00Z',
    contentType: 'application/json',
    headers: '{}',
    body: null,
    isBodyBase64: false,
    sizeBytes: 0,
    ipAddress: '1.2.3.4',
    userAgent: null,
    processingTimeMs: null,
    note: null,
    responseStatusCode: null,
    ipCountry: null,
    ...overrides,
  };
}

function makeToken(): Token {
  return {
    id: 'tok-1',
    token: 'aaaaaaaa-0000-0000-0000-000000000001',
    name: 'test-token',
    description: 'test token',
    webhookUrl: 'https://example.com/webhook/tok-1',
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    customResponse: null,
  };
}

function makeSummary(overrides: Partial<RequestSummary> = {}): RequestSummary {
  return {
    id: 'req-1',
    tokenId: 'tok-1',
    method: 'GET',
    path: '/webhook/tok-1',
    receivedAt: '2026-01-01T00:00:00Z',
    sizeBytes: 0,
    contentType: 'application/json',
    ...overrides,
  };
}

// ── Setup helper ─────────────────────────────────────────────────────────────

function setup(
  dialogAfterClosed: Observable<unknown> = of(undefined),
  sseEvents$: Observable<SseEvent> = EMPTY,
) {
  const tokenService = {
    getToken: vi.fn().mockReturnValue(of(makeToken())),
    deleteToken: vi.fn().mockReturnValue(of(null)),
    setCustomResponse: vi.fn().mockReturnValue(of(null)),
    resetCustomResponse: vi.fn().mockReturnValue(of(null)),
  };
  const requestService = {
    getRequests: vi.fn().mockReturnValue(of({ items: [], total: 0 })),
    getRequestDetail: vi.fn().mockReturnValue(of(makeDetail())),
    updateNote: vi.fn().mockReturnValue(of(null)),
    deleteRequest: vi.fn().mockReturnValue(of(null)),
    clearRequests: vi.fn().mockReturnValue(of(null)),
    exportRequest: vi.fn(),
  };
  const sseService = { connect: vi.fn().mockReturnValue(sseEvents$) };
  const dashboardService = { getMetrics: vi.fn().mockReturnValue(of(null)) };
  const modal = {
    open: vi.fn().mockReturnValue({ afterClosed: () => dialogAfterClosed }),
  };
  const toast = { show: vi.fn() };

  TestBed.configureTestingModule({
    imports: [TokenDetailComponent],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: TokenService, useValue: tokenService },
      { provide: RequestService, useValue: requestService },
      { provide: SseService, useValue: sseService },
      { provide: DashboardService, useValue: dashboardService },
      { provide: ModalService, useValue: modal },
      { provide: ToastService, useValue: toast },
      {
        provide: ActivatedRoute,
        useValue: {
          snapshot: { paramMap: { get: () => 'tok-1' } },
          paramMap: of(new Map([['id', 'tok-1']])),
        },
      },
    ],
  });

  const fixture = TestBed.createComponent(TokenDetailComponent);
  const component = fixture.componentInstance;
  fixture.detectChanges();
  const router = TestBed.inject(Router);
  return {
    fixture,
    component,
    requestService,
    tokenService,
    dashboardService,
    dialog: modal,
    snackBar: toast,
    router,
  };
}

// ── parsedQueryParams ─────────────────────────────────────────────────────────

describe('TokenDetailComponent — parsedQueryParams', () => {
  it('returns empty array when selectedDetail is null', () => {
    const { component } = setup();
    expect(component.parsedQueryParams()).toEqual([]);
  });

  it('returns empty array when queryString is null', () => {
    const { component } = setup();
    component.selectedDetail.set(makeDetail({ queryString: null }));
    expect(component.parsedQueryParams()).toEqual([]);
  });

  it('parses simple query string into key-value pairs', () => {
    const { component } = setup();
    component.selectedDetail.set(makeDetail({ queryString: '?a=1&b=2' }));
    const result = component.parsedQueryParams();
    expect(result).toContainEqual(['a', '1']);
    expect(result).toContainEqual(['b', '2']);
    expect(result.length).toBe(2);
  });

  it('handles query string without leading question mark', () => {
    const { component } = setup();
    component.selectedDetail.set(makeDetail({ queryString: 'foo=bar' }));
    expect(component.parsedQueryParams()).toContainEqual(['foo', 'bar']);
  });

  it('URL-decodes values', () => {
    const { component } = setup();
    component.selectedDetail.set(makeDetail({ queryString: '?email=john%40example.com' }));
    expect(component.parsedQueryParams()).toContainEqual(['email', 'john@example.com']);
  });
});

// ── parsedFormValues ──────────────────────────────────────────────────────────

describe('TokenDetailComponent — parsedFormValues', () => {
  it('returns empty array when contentType is not form-urlencoded', () => {
    const { component } = setup();
    component.selectedDetail.set(makeDetail({ contentType: 'application/json', body: 'a=1' }));
    expect(component.parsedFormValues()).toEqual([]);
  });

  it('returns empty array when selectedDetail is null', () => {
    const { component } = setup();
    expect(component.parsedFormValues()).toEqual([]);
  });

  it('parses form body into key-value pairs', () => {
    const { component } = setup();
    component.selectedDetail.set(
      makeDetail({
        contentType: 'application/x-www-form-urlencoded',
        body: 'username=john&role=admin',
        isBodyBase64: false,
      }),
    );
    const result = component.parsedFormValues();
    expect(result).toContainEqual(['username', 'john']);
    expect(result).toContainEqual(['role', 'admin']);
  });

  it('decodes base64 body before parsing', () => {
    const { component } = setup();
    const base64 = btoa('key=value');
    component.selectedDetail.set(
      makeDetail({
        contentType: 'application/x-www-form-urlencoded',
        body: base64,
        isBodyBase64: true,
      }),
    );
    expect(component.parsedFormValues()).toContainEqual(['key', 'value']);
  });

  it('returns empty array when base64 decode fails', () => {
    const { component } = setup();
    component.selectedDetail.set(
      makeDetail({
        contentType: 'application/x-www-form-urlencoded',
        body: '!!!invalid-base64!!!',
        isBodyBase64: true,
      }),
    );
    expect(component.parsedFormValues()).toEqual([]);
  });
});

// ── threatLinks ───────────────────────────────────────────────────────────────

describe('TokenDetailComponent — threatLinks', () => {
  it('contains encoded IP in all four URLs', () => {
    const { component } = setup();
    component.selectedDetail.set(makeDetail({ ipAddress: '192.168.0.1' }));
    const links = component.threatLinks();
    expect(links.whois).toContain('192.168.0.1');
    expect(links.shodan).toContain('192.168.0.1');
    expect(links.virustotal).toContain('192.168.0.1');
    expect(links.censys).toContain('192.168.0.1');
  });

  it('uses encodeURIComponent on IPv6 colons', () => {
    const { component } = setup();
    component.selectedDetail.set(makeDetail({ ipAddress: '::1' }));
    const links = component.threatLinks();
    expect(links.whois).toContain('%3A%3A1');
    expect(links.shodan).toContain('%3A%3A1');
  });

  it('links to correct domains', () => {
    const { component } = setup();
    component.selectedDetail.set(makeDetail({ ipAddress: '1.1.1.1' }));
    const links = component.threatLinks();
    expect(links.whois).toMatch(/whois\.com/);
    expect(links.shodan).toMatch(/shodan\.io/);
    expect(links.virustotal).toMatch(/virustotal\.com/);
    expect(links.censys).toMatch(/censys\.io/);
  });
});

// ── Note editing state machine ────────────────────────────────────────────────

describe('TokenDetailComponent — note state', () => {
  it('saveNote calls updateNote with trimmed value when changed', () => {
    const { component, requestService } = setup();
    component.selectedDetail.set(makeDetail({ id: 'req-1', note: null }));
    component.noteValue = '  my note  ';
    component.saveNote();
    expect(requestService.updateNote).toHaveBeenCalledWith('tok-1', 'req-1', 'my note');
  });

  it('saveNote with empty value passes null when previously had a note', () => {
    const { component, requestService } = setup();
    component.selectedDetail.set(makeDetail({ id: 'req-1', note: 'old' }));
    component.noteValue = '   ';
    component.saveNote();
    expect(requestService.updateNote).toHaveBeenCalledWith('tok-1', 'req-1', null);
  });

  it('saveNote skips update when value is unchanged', () => {
    const { component, requestService } = setup();
    component.selectedDetail.set(makeDetail({ id: 'req-1', note: 'same' }));
    component.noteValue = 'same';
    component.saveNote();
    expect(requestService.updateNote).not.toHaveBeenCalled();
  });

  it('saveNote skips update when both stored and current are empty', () => {
    const { component, requestService } = setup();
    component.selectedDetail.set(makeDetail({ id: 'req-1', note: null }));
    component.noteValue = '';
    component.saveNote();
    expect(requestService.updateNote).not.toHaveBeenCalled();
  });
});

// ── Lifecycle ─────────────────────────────────────────────────────────────────

describe('TokenDetailComponent — lifecycle', () => {
  it('loads token and requests on init', () => {
    const { tokenService, requestService } = setup();
    expect(tokenService.getToken).toHaveBeenCalledWith('tok-1');
    expect(requestService.getRequests).toHaveBeenCalled();
  });

  it('sets token signal after init', () => {
    const { component } = setup();
    expect(component.token()).toEqual(makeToken());
  });

  it('connected signal is false after EMPTY SSE stream completes', () => {
    const { component } = setup();
    expect(component.connected()).toBe(false);
  });
});

// ── selectRequest ─────────────────────────────────────────────────────────────

describe('TokenDetailComponent — selectRequest', () => {
  it('calls getRequestDetail and populates selectedDetail', () => {
    const detail = makeDetail({ id: 'req-42' });
    const { component, requestService } = setup();
    requestService.getRequestDetail.mockReturnValue(of(detail));
    component.selectRequest(makeSummary({ id: 'req-42' }));
    expect(requestService.getRequestDetail).toHaveBeenCalledWith('tok-1', 'req-42');
    expect(component.selectedDetail()).toEqual(detail);
  });

  it('syncs noteValue from the loaded detail when selecting a new request', () => {
    const { component, requestService } = setup();
    (requestService.getRequestDetail as ReturnType<typeof vi.fn>).mockReturnValue(
      of(makeDetail({ note: 'preloaded note' })),
    );
    component.noteValue = 'stale value';
    component.selectRequest(makeSummary());
    expect(component.noteValue).toBe('preloaded note');
  });
});

// ── Pagination ─────────────────────────────────────────────────────────────────

describe('TokenDetailComponent — pagination', () => {
  it('prevPage does not go below page 1', () => {
    const { component, requestService } = setup();
    const callsBefore = requestService.getRequests.mock.calls.length;
    component.prevPage();
    expect(requestService.getRequests.mock.calls.length).toBe(callsBefore);
  });

  it('nextPage increments page when more pages exist', () => {
    const { component, requestService } = setup();
    requestService.getRequests.mockReturnValue(of({ items: [], total: 25 }));
    component.total.set(25);
    component.nextPage();
    expect(component.page).toBe(2);
    expect(requestService.getRequests).toHaveBeenCalled();
  });

  it('nextPage does not increment past totalPages', () => {
    const { component, requestService } = setup();
    component.total.set(10);
    const callsBefore = requestService.getRequests.mock.calls.length;
    component.nextPage();
    expect(requestService.getRequests.mock.calls.length).toBe(callsBefore);
  });
});

// ── Utility methods ───────────────────────────────────────────────────────────

describe('TokenDetailComponent — utility methods', () => {
  it('formatHeaders parses JSON headers into name: value lines', () => {
    const { component } = setup();
    const result = component.formatHeaders('{"Content-Type":"application/json","X-Foo":"bar"}');
    expect(result).toContain('Content-Type: application/json');
    expect(result).toContain('X-Foo: bar');
  });

  it('formatHeaders returns raw string on invalid JSON', () => {
    const { component } = setup();
    expect(component.formatHeaders('not-json')).toBe('not-json');
  });

  it('formatHeaders handles array header values', () => {
    const { component } = setup();
    const result = component.formatHeaders('{"Accept":["text/html","application/json"]}');
    expect(result).toContain('Accept: text/html');
    expect(result).toContain('Accept: application/json');
  });

  it('decodeBody returns empty string when body is null', () => {
    const { component } = setup();
    expect(component.decodeBody(makeDetail({ body: null }))).toBe('');
  });

  it('decodeBody pretty-prints JSON body', () => {
    const { component } = setup();
    const result = component.decodeBody(makeDetail({ body: '{"a":1}', isBodyBase64: false }));
    expect(result).toContain('"a": 1');
  });

  it('decodeBody returns raw string for non-JSON body', () => {
    const { component } = setup();
    const result = component.decodeBody(makeDetail({ body: 'plain text', isBodyBase64: false }));
    expect(result).toBe('plain text');
  });

  it('decodeBody decodes base64 body as utf-8', () => {
    const { component } = setup();
    const b64 = btoa('hello');
    const result = component.decodeBody(makeDetail({ body: b64, isBodyBase64: true }));
    expect(result).toBe('hello');
  });

  it('showDateHeader always returns true for index 0', () => {
    const { component } = setup();
    component.requests.set([makeSummary(), makeSummary({ receivedAt: '2026-01-02T00:00:00Z' })]);
    expect(component.showDateHeader(0)).toBe(true);
  });

  it('showDateHeader returns false when consecutive requests share the same day', () => {
    const { component } = setup();
    component.requests.set([
      makeSummary({ receivedAt: '2026-01-01T10:00:00Z' }),
      makeSummary({ receivedAt: '2026-01-01T11:00:00Z' }),
    ]);
    expect(component.showDateHeader(1)).toBe(false);
  });

  it('showDateHeader returns true when consecutive requests are on different days', () => {
    const { component } = setup();
    component.requests.set([
      makeSummary({ receivedAt: '2026-01-01T00:00:00Z' }),
      makeSummary({ receivedAt: '2026-01-02T00:00:00Z' }),
    ]);
    expect(component.showDateHeader(1)).toBe(true);
  });

  it('getDateLabel returns Today for current date', () => {
    const { component } = setup();
    const today = new Date().toISOString();
    expect(component.getDateLabel(today)).toBe('Today');
  });

  it('getDateLabel returns Yesterday for previous date', () => {
    const { component } = setup();
    const yesterday = new Date();
    yesterday.setDate(yesterday.getDate() - 1);
    expect(component.getDateLabel(yesterday.toISOString())).toBe('Yesterday');
  });
});

// ── Dialog-driven actions ─────────────────────────────────────────────────────

describe('TokenDetailComponent — dialog actions', () => {
  it('deleteRequest opens dialog and removes request on confirm', () => {
    const { component, requestService, dialog } = setup(of(true));
    component.requests.set([makeSummary({ id: 'req-1' })]);
    component.total.set(1);
    const event = { stopPropagation: vi.fn() } as unknown as MouseEvent;
    component.deleteRequest(makeSummary({ id: 'req-1' }), event);
    expect(dialog.open).toHaveBeenCalled();
    expect(requestService.deleteRequest).toHaveBeenCalledWith('tok-1', 'req-1');
    expect(component.requests().length).toBe(0);
    expect(component.total()).toBe(0);
  });

  it('deleteRequest does not delete when dialog is cancelled', () => {
    const { component, requestService, dialog } = setup(of(false));
    component.requests.set([makeSummary({ id: 'req-1' })]);
    const event = { stopPropagation: vi.fn() } as unknown as MouseEvent;
    component.deleteRequest(makeSummary({ id: 'req-1' }), event);
    expect(dialog.open).toHaveBeenCalled();
    expect(requestService.deleteRequest).not.toHaveBeenCalled();
  });

  it('clearAll opens dialog and clears requests on confirm', () => {
    const { component, requestService, dialog } = setup(of(true));
    component.requests.set([makeSummary()]);
    component.total.set(1);
    component.clearAll();
    expect(dialog.open).toHaveBeenCalled();
    expect(requestService.clearRequests).toHaveBeenCalledWith('tok-1');
    expect(component.requests()).toEqual([]);
    expect(component.total()).toBe(0);
  });

  it('deleteRequest refreshes dashboard metrics after confirm', () => {
    const { component, dashboardService } = setup(of(true));
    component.requests.set([makeSummary({ id: 'req-1' })]);
    component.total.set(1);
    const event = { stopPropagation: vi.fn() } as unknown as MouseEvent;
    dashboardService.getMetrics.mockClear();

    component.deleteRequest(makeSummary({ id: 'req-1' }), event);

    expect(dashboardService.getMetrics).toHaveBeenCalledTimes(1);
  });

  it('clearAll refreshes dashboard metrics after confirm', () => {
    const { component, dashboardService } = setup(of(true));
    component.requests.set([makeSummary()]);
    component.total.set(1);
    dashboardService.getMetrics.mockClear();

    component.clearAll();

    expect(dashboardService.getMetrics).toHaveBeenCalledTimes(1);
  });

  it('exportSelected calls exportRequest for selectedDetail', () => {
    const { component, requestService } = setup();
    component.selectedDetail.set(makeDetail({ id: 'req-99' }));
    component.exportSelected();
    expect(requestService.exportRequest).toHaveBeenCalledWith('tok-1', 'req-99');
  });

  it('exportSelected does nothing when no detail is selected', () => {
    const { component, requestService } = setup();
    component.exportSelected();
    expect(requestService.exportRequest).not.toHaveBeenCalled();
  });

  it('deleteToken opens dialog and calls deleteToken on confirm', () => {
    const { component, tokenService, dialog, router } = setup(of(true));
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.deleteToken();
    expect(dialog.open).toHaveBeenCalled();
    expect(tokenService.deleteToken).toHaveBeenCalledWith('tok-1');
  });

  it('deleteToken does nothing when dialog is cancelled', () => {
    const { component, tokenService, dialog } = setup(of(false));
    component.deleteToken();
    expect(dialog.open).toHaveBeenCalled();
    expect(tokenService.deleteToken).not.toHaveBeenCalled();
  });

  it('openCustomResponse save path calls setCustomResponse', () => {
    const dto = { statusCode: 201, contentType: 'text/plain', body: null, headers: '{}' };
    const { component, tokenService } = setup(of({ action: 'save', dto }));
    component.token.set(makeToken());
    component.openCustomResponse();
    expect(tokenService.setCustomResponse).toHaveBeenCalledWith('tok-1', dto);
  });

  it('openCustomResponse reset path calls resetCustomResponse', () => {
    const { component, tokenService } = setup(of({ action: 'reset' }));
    component.token.set(makeToken());
    component.openCustomResponse();
    expect(tokenService.resetCustomResponse).toHaveBeenCalledWith('tok-1');
  });

  it('openCustomResponse does nothing when token is null', () => {
    const { component, tokenService } = setup();
    component.token.set(null);
    component.openCustomResponse();
    expect(tokenService.setCustomResponse).not.toHaveBeenCalled();
  });
});

// ── Error paths ───────────────────────────────────────────────────────────────

describe('TokenDetailComponent — error paths', () => {
  it('loadRequests error sets loading to false', () => {
    const { component, requestService } = setup();
    requestService.getRequests.mockReturnValue(throwError(() => new Error('net')));
    component.loadRequests();
    expect(component.loading()).toBe(false);
  });

  it('selectRequest error sets detailLoading to false', () => {
    const { component, requestService } = setup();
    requestService.getRequestDetail.mockReturnValue(throwError(() => new Error('net')));
    component.selectRequest(makeSummary());
    expect(component.detailLoading()).toBe(false);
  });

  it('saveNote error shows toast and clears noteSaving', () => {
    const { component, requestService, snackBar } = setup();
    requestService.updateNote.mockReturnValue(throwError(() => new Error('fail')));
    component.selectedDetail.set(makeDetail());
    component.noteValue = 'note';
    component.saveNote();
    expect(component.noteSaving()).toBe(false);
    expect(snackBar.show).toHaveBeenCalled();
  });
});

// ── Lifecycle ─────────────────────────────────────────────────────────────────

describe('TokenDetailComponent — ngOnDestroy', () => {
  it('ngOnDestroy does not throw', () => {
    const { fixture } = setup();
    expect(() => fixture.destroy()).not.toThrow();
  });
});

// ── onSearchChange ────────────────────────────────────────────────────────────

describe('TokenDetailComponent — onSearchChange', () => {
  it('onSearchChange forwards the term to the search subject without throwing', () => {
    const { component } = setup();
    expect(() => component.onSearchChange('hello')).not.toThrow();
  });
});

// ── SSE events ────────────────────────────────────────────────────────────────

describe('TokenDetailComponent — SSE events', () => {
  it('connected SSE event sets connected signal to true', () => {
    const subject = new Subject<SseEvent>();
    const { component } = setup(of(undefined), subject.asObservable());
    subject.next({ eventType: 'connected' });
    expect(component.connected()).toBe(true);
  });

  it('disconnected SSE event sets connected signal to false', () => {
    const subject = new Subject<SseEvent>();
    const { component } = setup(of(undefined), subject.asObservable());
    subject.next({ eventType: 'connected' });
    subject.next({ eventType: 'disconnected' });
    expect(component.connected()).toBe(false);
  });

  it('new-request SSE event prepends to requests list', () => {
    const subject = new Subject<SseEvent>();
    const { component } = setup(of(undefined), subject.asObservable());
    const newReq: RequestSummary = makeSummary({ id: 'req-new', method: 'POST' });
    subject.next({ eventType: 'new-request', data: newReq });
    expect(component.requests()[0].id).toBe('req-new');
    expect(component.total()).toBe(1);
  });
});

// ── copyUrl ───────────────────────────────────────────────────────────────────

describe('TokenDetailComponent — copyUrl', () => {
  it('copyUrl writes webhook URL to clipboard', async () => {
    const { component } = setup();
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText },
      writable: true,
      configurable: true,
    });
    component.token.set(makeToken());
    component.copyUrl();
    await Promise.resolve();
    expect(writeText).toHaveBeenCalledWith('https://example.com/webhook/tok-1');
  });
});

// ── decodeBody edge cases ─────────────────────────────────────────────────────

describe('TokenDetailComponent — decodeBody edge cases', () => {
  it('returns raw body when base64 decode throws', () => {
    const { component } = setup();
    const result = component.decodeBody(makeDetail({ body: '!!!invalid!!!', isBodyBase64: true }));
    expect(result).toBe('!!!invalid!!!');
  });
});
