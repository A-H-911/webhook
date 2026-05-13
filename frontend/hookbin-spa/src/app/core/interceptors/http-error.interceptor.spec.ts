import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors, HttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { httpErrorInterceptor } from './http-error.interceptor';
import { ToastService } from '../../shared/toast/toast.service';

// Pins the DANGER ZONE invariant from CLAUDE.md:
//   "Interceptor excludes /api/auth/ from the 401→/login redirect.
//    POST /api/auth/login returns 401 on bad credentials. Redirecting to /login
//    on that 401 creates an infinite redirect loop in the login form."
describe('httpErrorInterceptor', () => {
  let http: HttpClient;
  let httpController: HttpTestingController;
  let router: { navigate: ReturnType<typeof vi.fn> };
  let toast: { show: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    router = { navigate: vi.fn().mockResolvedValue(true) };
    toast = { show: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([httpErrorInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
        { provide: ToastService, useValue: toast },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpController = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpController.verify());

  it('401 on a NON-auth endpoint navigates to /login', async () => {
    const promise = new Promise<void>((resolve) => {
      http.get('/api/tokens').subscribe({
        error: () => {
          expect(router.navigate).toHaveBeenCalledWith(['/login']);
          resolve();
        },
      });
    });
    httpController
      .expectOne('/api/tokens')
      .flush('unauth', { status: 401, statusText: 'Unauthorized' });
    await promise;
  });

  it('401 on /api/auth/login does NOT navigate to /login (DANGER ZONE — prevents redirect loop)', async () => {
    const promise = new Promise<void>((resolve) => {
      http.post('/api/auth/login', { username: 'a', password: 'b' }).subscribe({
        error: () => {
          expect(router.navigate).not.toHaveBeenCalled();
          resolve();
        },
      });
    });
    httpController
      .expectOne('/api/auth/login')
      .flush('bad creds', { status: 401, statusText: 'Unauthorized' });
    await promise;
  });

  it('401 on /api/auth/me does NOT navigate to /login', async () => {
    const promise = new Promise<void>((resolve) => {
      http.get('/api/auth/me').subscribe({
        error: () => {
          expect(router.navigate).not.toHaveBeenCalled();
          resolve();
        },
      });
    });
    httpController
      .expectOne('/api/auth/me')
      .flush('no session', { status: 401, statusText: 'Unauthorized' });
    await promise;
  });

  it('401 on /api/auth/* does NOT show a toast (auth errors are handled by the form)', async () => {
    const promise = new Promise<void>((resolve) => {
      http.post('/api/auth/login', {}).subscribe({
        error: () => {
          expect(toast.show).not.toHaveBeenCalled();
          resolve();
        },
      });
    });
    httpController
      .expectOne('/api/auth/login')
      .flush('x', { status: 401, statusText: 'Unauthorized' });
    await promise;
  });

  it('500 on a non-auth endpoint shows a toast', async () => {
    const promise = new Promise<void>((resolve) => {
      http.get('/api/tokens').subscribe({
        error: () => {
          expect(toast.show).toHaveBeenCalled();
          resolve();
        },
      });
    });
    httpController
      .expectOne('/api/tokens')
      .flush({ detail: 'boom' }, { status: 500, statusText: 'Internal Server Error' });
    await promise;
  });

  it('500 on a non-auth endpoint does NOT navigate', async () => {
    const promise = new Promise<void>((resolve) => {
      http.get('/api/tokens').subscribe({
        error: () => {
          expect(router.navigate).not.toHaveBeenCalled();
          resolve();
        },
      });
    });
    httpController
      .expectOne('/api/tokens')
      .flush({ detail: 'boom' }, { status: 500, statusText: 'fail' });
    await promise;
  });

  it('2xx response does not invoke toast or router', async () => {
    const promise = new Promise<void>((resolve) => {
      http.get('/api/tokens').subscribe(() => {
        expect(router.navigate).not.toHaveBeenCalled();
        expect(toast.show).not.toHaveBeenCalled();
        resolve();
      });
    });
    httpController.expectOne('/api/tokens').flush({ items: [] });
    await promise;
  });
});
