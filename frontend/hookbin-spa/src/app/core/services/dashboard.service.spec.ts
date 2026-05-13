import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { DashboardService } from './dashboard.service';
import { DashboardMetrics } from '../models/dashboard.model';

function makeMetrics(overrides: Partial<DashboardMetrics> = {}): DashboardMetrics {
  return {
    totalEndpoints: 0,
    newEndpointsLast7d: 0,
    requestsCapturedAllTime: 0,
    requestsCapturedLast24h: 0,
    liveEndpoints: 0,
    ...overrides,
  };
}

describe('DashboardService', () => {
  let service: DashboardService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DashboardService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('initial metrics signal is null', () => {
    expect(service.metrics()).toBeNull();
  });

  it('getMetrics sends GET /api/dashboard/metrics', () => {
    service.getMetrics().subscribe();
    const req = http.expectOne('/api/dashboard/metrics');
    expect(req.request.method).toBe('GET');
    req.flush(makeMetrics());
  });

  it('getMetrics populates the metrics signal', () => {
    const expected = makeMetrics({
      totalEndpoints: 7,
      requestsCapturedAllTime: 1234,
      liveEndpoints: 5,
    });

    service.getMetrics().subscribe();
    const req = http.expectOne('/api/dashboard/metrics');
    req.flush(expected);

    expect(service.metrics()).toEqual(expected);
  });

  it('getMetrics emits the payload through the observable', async () => {
    const expected = makeMetrics({ newEndpointsLast7d: 3 });

    const promise = new Promise<DashboardMetrics>((resolve) => {
      service.getMetrics().subscribe((m) => resolve(m));
    });
    http.expectOne('/api/dashboard/metrics').flush(expected);

    await expect(promise).resolves.toEqual(expected);
  });
});
