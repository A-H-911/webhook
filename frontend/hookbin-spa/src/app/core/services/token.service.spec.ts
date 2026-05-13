import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { TokenService } from './token.service';
import { Token, SetCustomResponseDto } from '../models/token.model';

function makeToken(overrides: Partial<Token> = {}): Token {
  return {
    id: 'tok-1',
    token: 'aaaaaaaa-0000-0000-0000-000000000001',
    name: 'test-token',
    webhookUrl: 'https://example.com/webhook/tok-1',
    description: 'test',
    createdAt: '2026-01-01T00:00:00Z',
    isActive: true,
    customResponse: null,
    ...overrides,
  };
}

describe('TokenService', () => {
  let service: TokenService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TokenService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── getTokens ─────────────────────────────────────────────────────────────

  it('getTokens sends GET /api/tokens with pagination params', () => {
    service.getTokens().subscribe();
    const req = http.expectOne((r) => r.url === '/api/tokens');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('skip')).toBe('0');
    expect(req.request.params.get('take')).toBe('50');
    req.flush({ items: [], total: 0, hasMore: false });
  });

  // ── getToken ──────────────────────────────────────────────────────────────

  it('getToken sends GET /api/tokens/:id', () => {
    service.getToken('tok-1').subscribe();
    const req = http.expectOne('/api/tokens/tok-1');
    expect(req.request.method).toBe('GET');
    req.flush(makeToken());
  });

  // ── createToken ───────────────────────────────────────────────────────────

  it('createToken sends POST with name and null description', () => {
    service.createToken('my token').subscribe();
    const req = http.expectOne('/api/tokens');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'my token', description: null });
    req.flush(makeToken({ name: 'my token' }));
  });

  it('createToken sends POST with name and explicit description', () => {
    service.createToken('my token', 'a desc').subscribe();
    const req = http.expectOne('/api/tokens');
    expect(req.request.body).toEqual({ name: 'my token', description: 'a desc' });
    req.flush(makeToken({ name: 'my token', description: 'a desc' }));
  });

  // ── deleteToken ───────────────────────────────────────────────────────────

  it('deleteToken sends DELETE /api/tokens/:id', () => {
    service.deleteToken('tok-1').subscribe();
    const req = http.expectOne('/api/tokens/tok-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  // ── setCustomResponse ─────────────────────────────────────────────────────

  it('setCustomResponse sends PUT with the DTO body', () => {
    const dto: SetCustomResponseDto = {
      statusCode: 201,
      contentType: 'application/json',
      body: '{"ok":true}',
      headers: '{}',
    };
    service.setCustomResponse('tok-1', dto).subscribe();
    const req = http.expectOne('/api/tokens/tok-1/custom-response');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(dto);
    req.flush(null);
  });

  // ── resetCustomResponse ───────────────────────────────────────────────────

  it('resetCustomResponse sends DELETE /api/tokens/:id/custom-response', () => {
    service.resetCustomResponse('tok-1').subscribe();
    const req = http.expectOne('/api/tokens/tok-1/custom-response');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
