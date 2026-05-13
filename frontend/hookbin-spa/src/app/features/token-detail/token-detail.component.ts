import { Component, inject, signal, computed, OnInit, OnDestroy, DestroyRef } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatePipe, NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TokenService } from '../../core/services/token.service';
import { RequestService } from '../../core/services/request.service';
import { SseService } from '../../core/services/sse.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { BreadcrumbService } from '../../core/services/breadcrumb.service';
import { Token, SetCustomResponseDto } from '../../core/models/token.model';
import { RequestSummary } from '../../core/models/request-summary.model';
import { RequestDetail } from '../../core/models/request-detail.model';
import { CustomResponseDialogComponent } from '../custom-response/custom-response-dialog.component';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog/confirm-dialog.component';
import { ModalService } from '../../shared/modal/modal.service';
import { ToastService } from '../../shared/toast/toast.service';
import { JsonTreeComponent } from '../../shared/json-tree/json-tree.component';
import { JsonHighlightPipe } from '../../shared/json-highlight/json-highlight.pipe';

type BodyViewMode = 'tree' | 'pretty' | 'raw';

const PAGE_SIZE = 20;

@Component({
  selector: 'app-token-detail',
  standalone: true,
  imports: [RouterLink, DatePipe, NgClass, FormsModule, JsonTreeComponent, JsonHighlightPipe],
  templateUrl: './token-detail.component.html',
  styleUrl: './token-detail.component.scss',
})
export class TokenDetailComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly tokenService = inject(TokenService);
  private readonly requestService = inject(RequestService);
  private readonly sseService = inject(SseService);
  private readonly dashboardService = inject(DashboardService);
  private readonly modal = inject(ModalService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly breadcrumb = inject(BreadcrumbService);

  private sseSub?: Subscription;
  private readonly searchSubject = new Subject<string>();

  readonly token = signal<Token | null>(null);
  readonly requests = signal<RequestSummary[]>([]);
  readonly selectedDetail = signal<RequestDetail | null>(null);
  readonly loading = signal(false);
  readonly detailLoading = signal(false);
  readonly connected = signal(false);

  readonly selectedMethods = signal<string[]>([]);
  readonly selectedStatusGroups = signal<number[]>([]);
  readonly newRequestIds = new Set<string>();

  readonly METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'];
  readonly STATUS_GROUPS = [2, 3, 4, 5];

  page = 1;
  readonly total = signal(0);
  search = '';

  readonly totalPages = computed(() => Math.ceil(this.total() / PAGE_SIZE) || 1);
  readonly localTz = Intl.DateTimeFormat().resolvedOptions().timeZone;

  readonly parsedHeaders = computed<{ name: string; value: string }[]>(() => {
    const raw = this.selectedDetail()?.headers;
    if (!raw) return [];
    try {
      const parsed = JSON.parse(raw) as Record<string, string | string[]>;
      return Object.entries(parsed).flatMap(([name, value]) => {
        const vals = Array.isArray(value) ? value : [value];
        return vals.map((v) => ({ name, value: v }));
      });
    } catch {
      return [];
    }
  });

  readonly headerCount = computed(() => this.parsedHeaders().length);

  readonly parsedQueryParams = computed<[string, string][]>(() => {
    const qs = this.selectedDetail()?.queryString;
    if (!qs) return [];
    const clean = qs.startsWith('?') ? qs.slice(1) : qs;
    return [...new URLSearchParams(clean).entries()] as [string, string][];
  });

  readonly parsedFormValues = computed<[string, string][]>(() => {
    const detail = this.selectedDetail();
    if (!detail?.contentType?.includes('application/x-www-form-urlencoded')) return [];
    let body = detail.body ?? '';
    if (detail.isBodyBase64) {
      try {
        const bytes = Uint8Array.from(atob(body), (c) => c.charCodeAt(0));
        body = new TextDecoder('utf-8').decode(bytes);
      } catch {
        return [];
      }
    }
    return [...new URLSearchParams(body).entries()] as [string, string][];
  });

  readonly threatLinks = computed(() => {
    const ip = encodeURIComponent(this.selectedDetail()?.ipAddress ?? '');
    return {
      whois: `https://www.whois.com/whois/${ip}`,
      shodan: `https://www.shodan.io/host/${ip}`,
      virustotal: `https://www.virustotal.com/gui/ip-address/${ip}`,
      censys: `https://search.censys.io/hosts/${ip}`,
    };
  });

  noteValue = '';
  readonly noteSaving = signal(false);
  readonly bodyViewMode = signal<BodyViewMode>('tree');
  readonly bodySearch = signal('');

  readonly parsedBody = computed<unknown | null>(() => {
    const detail = this.selectedDetail();
    if (!detail || detail.body === null || detail.body === undefined) return null;
    let raw = detail.body;
    if (detail.isBodyBase64) {
      try {
        const bytes = Uint8Array.from(atob(raw), (c) => c.charCodeAt(0));
        raw = new TextDecoder('utf-8').decode(bytes);
      } catch {
        return null;
      }
    }
    try {
      return JSON.parse(raw);
    } catch {
      return null;
    }
  });

  readonly defaultExpandPath = computed<string>(() => {
    const body = this.parsedBody();
    if (!body || typeof body !== 'object') return '';
    if (Array.isArray(body)) return '';
    if ('error' in (body as Record<string, unknown>)) return '/error';
    return '';
  });

  onBodySearchChange(event: Event): void {
    const target = event.target as HTMLInputElement | null;
    this.bodySearch.set(target?.value ?? '');
  }

  get tokenId(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  ngOnInit(): void {
    this.tokenService.getToken(this.tokenId).subscribe({
      next: (t) => {
        this.token.set(t);
        this.breadcrumb.setToken(t.name);
      },
      error: () => this.router.navigate(['/dashboard']),
    });
    this.loadRequests();
    this.connectSse();

    this.searchSubject
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe((term) => {
        this.search = term;
        this.page = 1;
        this.loadRequests();
      });
  }

  ngOnDestroy(): void {
    this.sseSub?.unsubscribe();
    this.breadcrumb.clear();
  }

  private connectSse(): void {
    this.sseSub = this.sseService.connect(this.tokenId).subscribe({
      next: (event) => {
        if (event.eventType === 'connected') {
          this.connected.set(true);
        } else if (event.eventType === 'disconnected') {
          this.connected.set(false);
        } else if (event.eventType === 'new-request') {
          this.connected.set(true);
          this.newRequestIds.add(event.data.id);
          this.requests.update((list) => [event.data, ...list]);
          this.total.update((n) => n + 1);
        } else if (event.eventType === 'token-deleted') {
          this.toast.show('This webhook URL was deleted', 4000);
          this.router.navigate(['/dashboard']);
        }
      },
      error: () => this.connected.set(false),
      complete: () => this.connected.set(false),
    });
  }

  toggleMethod(method: string): void {
    this.selectedMethods.update((list) =>
      list.includes(method) ? list.filter((m) => m !== method) : [...list, method],
    );
    this.page = 1;
    this.loadRequests();
  }

  toggleStatusGroup(group: number): void {
    this.selectedStatusGroups.update((list) =>
      list.includes(group) ? list.filter((g) => g !== group) : [...list, group],
    );
    this.page = 1;
    this.loadRequests();
  }

  isNewRow(id: string): boolean {
    return this.newRequestIds.has(id);
  }

  statusGroupLabel(group: number): string {
    return `${group}xx`;
  }

  statusClass(code: number | null | undefined): string {
    if (!code) return '';
    const g = Math.floor(code / 100);
    return `s${g}`;
  }

  loadRequests(): void {
    this.loading.set(true);
    this.requestService
      .getRequests(
        this.tokenId,
        this.page,
        PAGE_SIZE,
        this.search,
        this.selectedMethods(),
        this.selectedStatusGroups(),
      )
      .subscribe({
        next: (result) => {
          this.requests.set(result.items);
          this.total.set(result.total);
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
  }

  onSearchChange(term: string): void {
    this.searchSubject.next(term);
  }

  prevPage(): void {
    if (this.page > 1) {
      this.page--;
      this.loadRequests();
    }
  }

  nextPage(): void {
    if (this.page < this.totalPages()) {
      this.page++;
      this.loadRequests();
    }
  }

  selectRequest(req: RequestSummary): void {
    this.detailLoading.set(true);
    this.selectedDetail.set(null);
    this.noteValue = '';
    this.bodyViewMode.set('tree');
    this.requestService.getRequestDetail(this.tokenId, req.id).subscribe({
      next: (detail) => {
        this.selectedDetail.set(detail);
        this.noteValue = detail.note ?? '';
        this.detailLoading.set(false);
      },
      error: () => this.detailLoading.set(false),
    });
  }

  saveNote(): void {
    const detail = this.selectedDetail();
    if (!detail) return;
    const note = this.noteValue.trim() || null;
    if (note === (detail.note ?? null)) return; // skip if unchanged
    this.noteSaving.set(true);
    this.requestService.updateNote(this.tokenId, detail.id, note).subscribe({
      next: () => {
        this.selectedDetail.update((d) => (d ? { ...d, note } : d));
        this.noteSaving.set(false);
      },
      error: () => {
        this.noteSaving.set(false);
        this.toast.show('Failed to save note');
      },
    });
  }

  deleteRequest(req: RequestSummary, event: MouseEvent): void {
    event.stopPropagation();
    this.modal
      .open<ConfirmDialogComponent, boolean | null>(ConfirmDialogComponent, {
        data: { message: 'Delete this request?', confirmLabel: 'Delete' },
      })
      .afterClosed()
      .subscribe((confirmed) => {
        if (!confirmed) return;
        this.requestService.deleteRequest(this.tokenId, req.id).subscribe(() => {
          this.requests.update((list) => list.filter((r) => r.id !== req.id));
          this.total.update((n) => n - 1);
          if (this.selectedDetail()?.id === req.id) this.selectedDetail.set(null);
          this.dashboardService.getMetrics().subscribe();
        });
      });
  }

  exportSelected(): void {
    const detail = this.selectedDetail();
    if (!detail) return;
    this.requestService.exportRequest(this.tokenId, detail.id);
  }

  clearAll(): void {
    this.modal
      .open<ConfirmDialogComponent, boolean | null>(ConfirmDialogComponent, {
        data: {
          message: 'Clear all captured requests for this webhook URL?',
          confirmLabel: 'Clear',
        },
      })
      .afterClosed()
      .subscribe((confirmed) => {
        if (!confirmed) return;
        this.requestService.clearRequests(this.tokenId).subscribe(() => {
          this.requests.set([]);
          this.total.set(0);
          this.selectedDetail.set(null);
          this.dashboardService.getMetrics().subscribe();
          this.toast.show('All requests cleared');
        });
      });
  }

  deleteToken(): void {
    this.modal
      .open<ConfirmDialogComponent, boolean | null>(ConfirmDialogComponent, {
        data: {
          message: 'Delete this webhook URL and all its requests permanently?',
          confirmLabel: 'Delete',
        },
      })
      .afterClosed()
      .subscribe((confirmed) => {
        if (!confirmed) return;
        this.tokenService
          .deleteToken(this.tokenId)
          .subscribe(() => this.router.navigate(['/dashboard']));
      });
  }

  openCustomResponse(): void {
    const t = this.token();
    if (!t) return;
    this.modal
      .open<
        CustomResponseDialogComponent,
        { action: 'save'; dto: SetCustomResponseDto } | { action: 'reset' } | null
      >(CustomResponseDialogComponent, { data: t.customResponse })
      .afterClosed()
      .subscribe((result) => {
        if (!result) return;
        if (result.action === 'reset') {
          this.tokenService.resetCustomResponse(this.tokenId).subscribe(() => {
            this.token.update((tok) => (tok ? { ...tok, customResponse: null } : tok));
            this.toast.show('Custom response removed');
          });
        } else {
          this.tokenService.setCustomResponse(this.tokenId, result.dto).subscribe(() => {
            this.token.update((tok) => (tok ? { ...tok, customResponse: { ...result.dto } } : tok));
            this.toast.show('Custom response saved');
          });
        }
      });
  }

  copyUrl(): void {
    const url = this.token()?.webhookUrl ?? '';
    navigator.clipboard
      .writeText(url)
      .then(() => this.toast.show('URL copied', 2000))
      .catch(() => this.toast.show('Copy failed — please copy manually'));
  }

  formatHeaders(raw: string): string {
    try {
      const parsed = JSON.parse(raw) as Record<string, string | string[]>;
      return Object.entries(parsed)
        .flatMap(([name, value]) => {
          const vals = Array.isArray(value) ? value : [value];
          return vals.map((v) => `${name}: ${v}`);
        })
        .join('\n');
    } catch {
      return raw;
    }
  }

  decodeBody(detail: RequestDetail): string {
    if (!detail.body) return '';
    if (detail.isBodyBase64) {
      try {
        const bytes = Uint8Array.from(atob(detail.body), (c) => c.charCodeAt(0));
        return new TextDecoder('utf-8').decode(bytes);
      } catch {
        return detail.body;
      }
    }
    try {
      return JSON.stringify(JSON.parse(detail.body), null, 2);
    } catch {
      return detail.body;
    }
  }

  showDateHeader(index: number): boolean {
    const reqs = this.requests();
    if (index === 0) return true;
    const curr = new Date(reqs[index].receivedAt).toDateString();
    const prev = new Date(reqs[index - 1].receivedAt).toDateString();
    return curr !== prev;
  }

  getDateLabel(receivedAt: string): string {
    const d = new Date(receivedAt);
    const today = new Date();
    const yesterday = new Date(today);
    yesterday.setDate(today.getDate() - 1);
    if (d.toDateString() === today.toDateString()) return 'Today';
    if (d.toDateString() === yesterday.toDateString()) return 'Yesterday';
    return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
  }
}
