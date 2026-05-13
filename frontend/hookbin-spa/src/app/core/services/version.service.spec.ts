import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { VersionService } from './version.service';

describe('VersionService', () => {
  let service: VersionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(VersionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts with empty version', () => {
    expect(service.version()).toBe('');
  });

  it('load sends GET /api/version', () => {
    service.load();
    const req = http.expectOne('/api/version');
    expect(req.request.method).toBe('GET');
    req.flush({ version: '1.2.3' });
  });

  it('load populates the version signal from the response', () => {
    service.load();
    http.expectOne('/api/version').flush({ version: '0.42.1' });
    expect(service.version()).toBe('0.42.1');
  });

  it('load can be called multiple times', () => {
    service.load();
    http.expectOne('/api/version').flush({ version: '1.0.0' });

    service.load();
    http.expectOne('/api/version').flush({ version: '2.0.0' });

    expect(service.version()).toBe('2.0.0');
  });
});
