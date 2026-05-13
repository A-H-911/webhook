import { TestBed, ComponentFixture } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { DashboardComponent } from './dashboard.component';
import { TokenService } from '../../core/services/token.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { ModalService } from '../../shared/modal/modal.service';
import { ModalRef } from '../../shared/modal/modal-ref';
import { ToastService } from '../../shared/toast/toast.service';
import { BreadcrumbService } from '../../core/services/breadcrumb.service';
import { TokenListItem, TokensPage } from '../../core/models/token.model';
import { signal } from '@angular/core';

function listItem(overrides: Partial<TokenListItem> = {}): TokenListItem {
  return {
    id: 'tok-1',
    token: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
    name: 'webhook-1',
    webhookUrl: 'https://hookbin.example/webhook/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
    description: null,
    createdAt: '2026-01-01T00:00:00Z',
    isActive: true,
    hasCustomResponse: false,
    lifetimeRequestCount: 10,
    requestCount24h: 3,
    sparkline24h: [1, 2, 3],
    lastReceivedAt: null,
    ...overrides,
  };
}

function emptyPage(): TokensPage {
  return { items: [], total: 0, hasMore: false };
}

function pageOf(items: TokenListItem[], hasMore = false): TokensPage {
  return { items, total: items.length, hasMore };
}

function makeFakeModalRef<T>(result: T) {
  return {
    afterClosed: () => of(result),
    close: vi.fn(),
  } as unknown as ModalRef<T>;
}

class FakeIntersectionObserver {
  observe = vi.fn();
  disconnect = vi.fn();
  unobserve = vi.fn();
  takeRecords = vi.fn().mockReturnValue([]);
  root = null;
  rootMargin = '';
  thresholds = [];
  constructor(public callback: IntersectionObserverCallback) {}
}

function setup(opts: { tokensPage?: TokensPage; metricsLive?: number } = {}) {
  const tokenService = {
    getTokens: vi.fn().mockReturnValue(of(opts.tokensPage ?? emptyPage())),
    createToken: vi.fn().mockReturnValue(of({ id: 'new-tok', name: 'created' })),
    deleteToken: vi.fn().mockReturnValue(of(void 0)),
  };
  const dashboardService = {
    getMetrics: vi.fn().mockReturnValue(
      of({
        totalEndpoints: 0,
        newEndpointsLast7d: 0,
        requestsCapturedAllTime: 0,
        requestsCapturedLast24h: 0,
        liveEndpoints: opts.metricsLive ?? 0,
      }),
    ),
    metrics: signal({
      totalEndpoints: 0,
      newEndpointsLast7d: 0,
      requestsCapturedAllTime: 0,
      requestsCapturedLast24h: 0,
      liveEndpoints: opts.metricsLive ?? 0,
    }),
  };
  const router = { navigate: vi.fn().mockResolvedValue(true) };
  const modal = { open: vi.fn() };
  const toast = { show: vi.fn() };
  const breadcrumb = { setDashboard: vi.fn(), setToken: vi.fn(), clear: vi.fn() };

  vi.stubGlobal(
    'IntersectionObserver',
    FakeIntersectionObserver as unknown as typeof IntersectionObserver,
  );

  TestBed.configureTestingModule({
    imports: [DashboardComponent],
    providers: [
      { provide: TokenService, useValue: tokenService },
      { provide: DashboardService, useValue: dashboardService },
      { provide: Router, useValue: router },
      { provide: ModalService, useValue: modal },
      { provide: ToastService, useValue: toast },
      { provide: BreadcrumbService, useValue: breadcrumb },
    ],
  });

  const fixture: ComponentFixture<DashboardComponent> = TestBed.createComponent(DashboardComponent);
  return {
    fixture,
    component: fixture.componentInstance,
    tokenService,
    dashboardService,
    router,
    modal,
    toast,
    breadcrumb,
  };
}

describe('DashboardComponent', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // ── lifecycle ──────────────────────────────────────────────────────────────

  it('ngOnInit sets the breadcrumb to dashboard', () => {
    const { fixture, breadcrumb } = setup();
    fixture.detectChanges();

    expect(breadcrumb.setDashboard).toHaveBeenCalledTimes(1);
  });

  it('ngOnDestroy clears the breadcrumb', () => {
    const { fixture, breadcrumb } = setup();
    fixture.detectChanges();
    fixture.destroy();

    expect(breadcrumb.clear).toHaveBeenCalledTimes(1);
  });

  // ── load() ─────────────────────────────────────────────────────────────────

  it('load populates tokens / total / hasMore from the forkJoin response', () => {
    const items = [listItem({ id: 'a' }), listItem({ id: 'b' })];
    const { fixture, component } = setup({ tokensPage: pageOf(items, false) });
    fixture.detectChanges();

    expect(component.tokens()).toEqual(items);
    expect(component.total()).toBe(2);
    expect(component.hasMore()).toBe(false);
    expect(component.loading()).toBe(false);
  });

  it('load clears loading flag when the request errors', () => {
    const { fixture, component, tokenService } = setup();
    tokenService.getTokens.mockReturnValueOnce(throwError(() => new Error('boom')));

    fixture.detectChanges();

    expect(component.loading()).toBe(false);
  });

  // ── refresh() ──────────────────────────────────────────────────────────────

  it('refresh re-fetches tokens and metrics', () => {
    const { fixture, component, tokenService, dashboardService } = setup();
    fixture.detectChanges();
    tokenService.getTokens.mockClear();
    dashboardService.getMetrics.mockClear();

    component.refresh();

    expect(tokenService.getTokens).toHaveBeenCalledTimes(1);
    expect(dashboardService.getMetrics).toHaveBeenCalledTimes(1);
  });

  it('refresh is a no-op while already refreshing', () => {
    const { fixture, component, tokenService } = setup();
    fixture.detectChanges();
    component.refreshing.set(true);
    tokenService.getTokens.mockClear();

    component.refresh();

    expect(tokenService.getTokens).not.toHaveBeenCalled();
  });

  it('refresh clears the refreshing flag immediately on error', () => {
    const { fixture, component, tokenService } = setup();
    fixture.detectChanges();
    tokenService.getTokens.mockReturnValueOnce(throwError(() => new Error('boom')));

    component.refresh();

    expect(component.refreshing()).toBe(false);
  });

  // ── openCreate() ───────────────────────────────────────────────────────────

  it('openCreate ignores null result (Cancel)', () => {
    const { fixture, component, modal, tokenService, router } = setup();
    fixture.detectChanges();
    modal.open.mockReturnValueOnce(makeFakeModalRef<null>(null));
    tokenService.createToken.mockClear();
    router.navigate.mockClear();

    component.openCreate();

    expect(tokenService.createToken).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('openCreate with valid result creates token, toasts, and navigates', () => {
    const { fixture, component, modal, tokenService, toast, router } = setup();
    fixture.detectChanges();
    modal.open.mockReturnValueOnce(
      makeFakeModalRef<{ name: string; description?: string }>({
        name: 'new-hook',
        description: 'desc',
      }),
    );
    tokenService.createToken.mockReturnValueOnce(of({ id: 'new-id', name: 'new-hook' }));

    component.openCreate();

    expect(tokenService.createToken).toHaveBeenCalledWith('new-hook', 'desc');
    expect(toast.show).toHaveBeenCalledWith('Webhook URL created');
    expect(router.navigate).toHaveBeenCalledWith(['/tokens', 'new-id']);
  });

  // ── open() / delete() / copyUrl() ──────────────────────────────────────────

  it('open navigates to the token detail route', () => {
    const { fixture, component, router } = setup();
    fixture.detectChanges();
    router.navigate.mockClear();

    component.open(listItem({ id: 'open-target' }));

    expect(router.navigate).toHaveBeenCalledWith(['/tokens', 'open-target']);
  });

  it('delete stops propagation, opens confirm dialog, and skips deletion on Cancel', () => {
    const { fixture, component, modal, tokenService } = setup();
    fixture.detectChanges();
    modal.open.mockReturnValueOnce(makeFakeModalRef<boolean | null>(null));
    const event = { stopPropagation: vi.fn() } as unknown as MouseEvent;
    tokenService.deleteToken.mockClear();

    component.delete(listItem(), event);

    expect(event.stopPropagation).toHaveBeenCalled();
    expect(tokenService.deleteToken).not.toHaveBeenCalled();
  });

  it('delete removes the token, decrements total, and toasts on confirm', () => {
    const items = [listItem({ id: 'to-delete' }), listItem({ id: 'keep' })];
    const { fixture, component, modal, tokenService, toast } = setup({
      tokensPage: pageOf(items),
    });
    fixture.detectChanges();
    modal.open.mockReturnValueOnce(makeFakeModalRef<boolean>(true));
    const event = { stopPropagation: vi.fn() } as unknown as MouseEvent;

    component.delete(items[0], event);

    expect(tokenService.deleteToken).toHaveBeenCalledWith('to-delete');
    expect(component.tokens().map((t) => t.id)).toEqual(['keep']);
    expect(component.total()).toBe(1);
    expect(toast.show).toHaveBeenCalledWith('Deleted');
  });

  it('delete refreshes dashboard metrics after confirm', () => {
    const items = [listItem({ id: 'to-delete' }), listItem({ id: 'keep' })];
    const { fixture, component, modal, dashboardService } = setup({
      tokensPage: pageOf(items),
    });
    fixture.detectChanges();
    modal.open.mockReturnValueOnce(makeFakeModalRef<boolean>(true));
    const event = { stopPropagation: vi.fn() } as unknown as MouseEvent;
    dashboardService.getMetrics.mockClear();

    component.delete(items[0], event);

    expect(dashboardService.getMetrics).toHaveBeenCalledTimes(1);
  });

  it('copyUrl writes the URL to the clipboard and toasts', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText },
    });

    const { fixture, component, toast } = setup();
    fixture.detectChanges();
    const event = { stopPropagation: vi.fn() } as unknown as MouseEvent;
    const token = listItem({ webhookUrl: 'https://example.com/webhook/xyz' });

    component.copyUrl(token, event);
    // settle the clipboard promise + toast
    await Promise.resolve();
    await Promise.resolve();

    expect(event.stopPropagation).toHaveBeenCalled();
    expect(writeText).toHaveBeenCalledWith('https://example.com/webhook/xyz');
    expect(toast.show).toHaveBeenCalledWith('URL copied', 2000);
  });

  // ── pure formatters ────────────────────────────────────────────────────────

  it('truncatedUrl shortens the token portion of the URL', () => {
    const { component } = setup();
    const token = listItem({
      webhookUrl: 'https://hookbin.example/webhook/12345678-aaaa-bbbb-cccc-deadbeef1234',
      token: '12345678-aaaa-bbbb-cccc-deadbeef1234',
    });

    const result = component.truncatedUrl(token);

    expect(result).toBe('https://hookbin.example/webhook/12345678…1234');
  });

  it('hostPart extracts the host and webhook path prefix', () => {
    const { component } = setup();
    const token = listItem({ webhookUrl: 'https://hookbin.example/webhook/abc' });

    expect(component.hostPart(token)).toBe('hookbin.example/webhook/');
  });

  it('hostPart returns an empty string for malformed URLs', () => {
    const { component } = setup();
    const token = listItem({ webhookUrl: 'not-a-url' });

    expect(component.hostPart(token)).toBe('');
  });

  it('pathPart shortens the token id with ellipsis', () => {
    const { component } = setup();
    const token = listItem({ token: '12345678-aaaa-bbbb-cccc-deadbeef1234' });

    expect(component.pathPart(token)).toBe('12345678…1234');
  });

  it('isActive returns false when lastReceivedAt is null', () => {
    const { component } = setup();
    expect(component.isActive(listItem({ lastReceivedAt: null }))).toBe(false);
  });

  it('isActive returns true for activity within the last 5 minutes', () => {
    const { component } = setup();
    const recent = new Date(Date.now() - 60_000).toISOString();
    expect(component.isActive(listItem({ lastReceivedAt: recent }))).toBe(true);
  });

  it('isActive returns false for activity older than 5 minutes', () => {
    const { component } = setup();
    const stale = new Date(Date.now() - 10 * 60_000).toISOString();
    expect(component.isActive(listItem({ lastReceivedAt: stale }))).toBe(false);
  });
});
