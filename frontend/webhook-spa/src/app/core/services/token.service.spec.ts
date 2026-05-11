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

  it('getTokens sends GET /api/tokens', () => {
    service.getTokens().subscribe();
    const req = http.expectOne('/api/tokens');
    expect(req.request.method).toBe('GET');
    req.flush([makeToken()]);
  });

  // ── getToken ──────────────────────────────────────────────────────────────

  it('getToken sends GET /api/tokens/:id', () => {
    service.getToken('tok-1').subscribe();
    const req = http.expectOne('/api/tokens/tok-1');
    expect(req.request.method).toBe('GET');
    req.flush(makeToken());
  });

  // ── createToken ───────────────────────────────────────────────────────────

  it('createToken sends POST with description', () => {
    service.createToken('my token').subscribe();
    const req = http.expectOne('/api/tokens');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ description: 'my token' });
    req.flush(makeToken({ description: 'my token' }));
  });

  it('createToken sends POST with null description when omitted', () => {
    service.createToken().subscribe();
    const req = http.expectOne('/api/tokens');
    expect(req.request.body).toEqual({ description: null });
    req.flush(makeToken({ description: null }));
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
