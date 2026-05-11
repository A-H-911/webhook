import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { RequestService } from './request.service';

describe('RequestService', () => {
  let service: RequestService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RequestService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── getRequests ─────────────────────────────────────────────────────────────

  it('getRequests sends GET with default page and pageSize params', () => {
    service.getRequests('tok-1').subscribe();
    const req = http.expectOne((r) => r.url === '/api/tokens/tok-1/requests');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('20');
    expect(req.request.params.has('search')).toBe(false);
    req.flush({ items: [], total: 0 });
  });

  it('getRequests includes search param when non-empty', () => {
    service.getRequests('tok-1', 2, 10, '  hello  ').subscribe();
    const req = http.expectOne((r) => r.url === '/api/tokens/tok-1/requests');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('10');
    expect(req.request.params.get('search')).toBe('hello');
    req.flush({ items: [], total: 0 });
  });

  it('getRequests omits search param for whitespace-only string', () => {
    service.getRequests('tok-1', 1, 20, '   ').subscribe();
    const req = http.expectOne((r) => r.url === '/api/tokens/tok-1/requests');
    expect(req.request.params.has('search')).toBe(false);
    req.flush({ items: [], total: 0 });
  });

  // ── getRequestDetail ─────────────────────────────────────────────────────────

  it('getRequestDetail sends GET to correct URL', () => {
    service.getRequestDetail('tok-1', 'req-1').subscribe();
    const req = http.expectOne('/api/tokens/tok-1/requests/req-1');
    expect(req.request.method).toBe('GET');
    req.flush({ id: 'req-1' });
  });

  // ── deleteRequest ─────────────────────────────────────────────────────────

  it('deleteRequest sends DELETE to correct URL', () => {
    service.deleteRequest('tok-1', 'req-1').subscribe();
    const req = http.expectOne('/api/tokens/tok-1/requests/req-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  // ── clearRequests ─────────────────────────────────────────────────────────

  it('clearRequests sends DELETE to the requests collection URL', () => {
    service.clearRequests('tok-1').subscribe();
    const req = http.expectOne('/api/tokens/tok-1/requests');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  // ── exportRequest ─────────────────────────────────────────────────────────

  it('exportRequest creates an anchor with the correct href and download name', () => {
    const anchors: HTMLAnchorElement[] = [];
    const origCreate = document.createElement.bind(document);
    vi.spyOn(document, 'createElement').mockImplementation((tag: string) => {
      const el = origCreate(tag);
      if (tag === 'a') {
        vi.spyOn(el as HTMLAnchorElement, 'click').mockImplementation(() => {});
        anchors.push(el as HTMLAnchorElement);
      }
      return el;
    });

    service.exportRequest('tok-1', 'req-42');

    expect(anchors).toHaveLength(1);
    expect(anchors[0].href).toContain('/api/tokens/tok-1/requests/req-42/export');
    expect(anchors[0].download).toBe('request-req-42.json');

    vi.restoreAllMocks();
  });

  // ── updateNote ─────────────────────────────────────────────────────────────

  it('updateNote sends PATCH with note value', () => {
    service.updateNote('tok-1', 'req-1', 'my note').subscribe();
    const req = http.expectOne('/api/tokens/tok-1/requests/req-1/note');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ note: 'my note' });
    req.flush(null);
  });

  it('updateNote sends PATCH with null note to clear it', () => {
    service.updateNote('tok-1', 'req-1', null).subscribe();
    const req = http.expectOne('/api/tokens/tok-1/requests/req-1/note');
    expect(req.request.body).toEqual({ note: null });
    req.flush(null);
  });
});
