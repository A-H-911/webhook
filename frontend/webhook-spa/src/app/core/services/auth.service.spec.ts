import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── initial state ─────────────────────────────────────────────────────────

  it('user signal starts as null', () => {
    expect(service.user()).toBeNull();
  });

  it('isAuthenticated starts as false', () => {
    expect(service.isAuthenticated()).toBe(false);
  });

  // ── initialize ────────────────────────────────────────────────────────────

  it('initialize sets user signal on success', async () => {
    const promise = service.initialize();
    http.expectOne('/api/auth/me').flush({ username: 'admin' });
    await promise;
    expect(service.user()).toEqual({ username: 'admin' });
    expect(service.isAuthenticated()).toBe(true);
  });

  it('initialize sets user to null when /api/auth/me returns 401', async () => {
    const promise = service.initialize();
    http.expectOne('/api/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    await promise;
    expect(service.user()).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
  });

  it('initialize does not throw on network error', async () => {
    const promise = service.initialize();
    http.expectOne('/api/auth/me').error(new ProgressEvent('error'));
    await expect(promise).resolves.toBeUndefined();
    expect(service.user()).toBeNull();
  });

  // ── login ─────────────────────────────────────────────────────────────────

  it('login POSTs credentials and sets user on success', async () => {
    const promise = service.login('admin', 'secret');
    const req = http.expectOne('/api/auth/login');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ username: 'admin', password: 'secret' });
    req.flush({ username: 'admin' });
    await promise;
    expect(service.user()).toEqual({ username: 'admin' });
    expect(service.isAuthenticated()).toBe(true);
  });

  it('login rejects and leaves user null on 401', async () => {
    const promise = service.login('admin', 'wrong');
    http.expectOne('/api/auth/login').flush(null, { status: 401, statusText: 'Unauthorized' });
    await expect(promise).rejects.toBeDefined();
    expect(service.user()).toBeNull();
  });

  // ── logout ────────────────────────────────────────────────────────────────

  it('logout POSTs to /api/auth/logout and clears user', async () => {
    // Seed an authenticated state first
    const initPromise = service.initialize();
    http.expectOne('/api/auth/me').flush({ username: 'admin' });
    await initPromise;

    const promise = service.logout();
    const req = http.expectOne('/api/auth/logout');
    expect(req.request.method).toBe('POST');
    req.flush(null);
    await promise;
    expect(service.user()).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
  });
});
