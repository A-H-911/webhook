import { TestBed } from '@angular/core/testing';
import { BreadcrumbService } from './breadcrumb.service';

describe('BreadcrumbService', () => {
  let service: BreadcrumbService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(BreadcrumbService);
  });

  it('starts with null context', () => {
    expect(service.context()).toBeNull();
  });

  it('setDashboard switches context to dashboard', () => {
    service.setDashboard();
    expect(service.context()).toEqual({ type: 'dashboard' });
  });

  it('setToken stores the token name', () => {
    service.setToken('my-hook');
    expect(service.context()).toEqual({ type: 'token', name: 'my-hook' });
  });

  it('setToken overwrites previous context', () => {
    service.setDashboard();
    service.setToken('newer');
    expect(service.context()).toEqual({ type: 'token', name: 'newer' });
  });

  it('clear resets context to null', () => {
    service.setToken('something');
    service.clear();
    expect(service.context()).toBeNull();
  });

  it('clear is idempotent', () => {
    service.clear();
    service.clear();
    expect(service.context()).toBeNull();
  });
});
