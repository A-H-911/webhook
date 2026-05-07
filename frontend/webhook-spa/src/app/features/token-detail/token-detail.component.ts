import { Component, inject, signal, computed, OnInit, OnDestroy, DestroyRef } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDividerModule } from '@angular/material/divider';
import { MatDialog } from '@angular/material/dialog';

import { TokenService } from '../../core/services/token.service';
import { RequestService } from '../../core/services/request.service';
import { SseService } from '../../core/services/sse.service';
import { Token, SetCustomResponseDto } from '../../core/models/token.model';
import { RequestSummary } from '../../core/models/request-summary.model';
import { RequestDetail } from '../../core/models/request-detail.model';
import { CustomResponseDialogComponent } from '../custom-response/custom-response-dialog.component';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog/confirm-dialog.component';

const PAGE_SIZE = 20;

@Component({
  selector: 'app-token-detail',
  standalone: true,
  imports: [
    RouterLink,
    DatePipe,
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatInputModule,
    MatFormFieldModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    MatDividerModule,
  ],
  templateUrl: './token-detail.component.html',
  styleUrl: './token-detail.component.scss',
})
export class TokenDetailComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly tokenService = inject(TokenService);
  private readonly requestService = inject(RequestService);
  private readonly sseService = inject(SseService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);

  private sseSub?: Subscription;
  private readonly searchSubject = new Subject<string>();

  readonly token = signal<Token | null>(null);
  readonly requests = signal<RequestSummary[]>([]);
  readonly selectedDetail = signal<RequestDetail | null>(null);
  readonly loading = signal(false);
  readonly detailLoading = signal(false);
  readonly connected = signal(false);

  page = 1;
  readonly total = signal(0);
  search = '';

  readonly totalPages = computed(() => Math.ceil(this.total() / PAGE_SIZE) || 1);
  readonly localTz = Intl.DateTimeFormat().resolvedOptions().timeZone;

  get tokenId(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  ngOnInit(): void {
    this.tokenService.getToken(this.tokenId).subscribe({
      next: (t) => this.token.set(t),
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
          this.requests.update((list) => [event.data, ...list]);
          this.total.update((n) => n + 1);
        } else if (event.eventType === 'token-deleted') {
          this.snackBar.open('This webhook URL was deleted', 'OK', { duration: 4000 });
          this.router.navigate(['/dashboard']);
        }
      },
      error: () => this.connected.set(false),
      complete: () => this.connected.set(false),
    });
  }

  loadRequests(): void {
    this.loading.set(true);
    this.requestService.getRequests(this.tokenId, this.page, PAGE_SIZE, this.search).subscribe({
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
    this.requestService.getRequestDetail(this.tokenId, req.id).subscribe({
      next: (detail) => {
        this.selectedDetail.set(detail);
        this.detailLoading.set(false);
      },
      error: () => this.detailLoading.set(false),
    });
  }

  deleteRequest(req: RequestSummary, event: MouseEvent): void {
    event.stopPropagation();
    this.dialog
      .open(ConfirmDialogComponent, {
        width: '340px',
        data: { message: 'Delete this request?', confirmLabel: 'Delete' },
      })
      .afterClosed()
      .subscribe((confirmed: boolean) => {
        if (!confirmed) return;
        this.requestService.deleteRequest(this.tokenId, req.id).subscribe(() => {
          this.requests.update((list) => list.filter((r) => r.id !== req.id));
          this.total.update((n) => n - 1);
          if (this.selectedDetail()?.id === req.id) this.selectedDetail.set(null);
        });
      });
  }

  exportSelected(): void {
    const detail = this.selectedDetail();
    if (!detail) return;
    this.requestService.exportRequest(this.tokenId, detail.id);
  }

  clearAll(): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        width: '340px',
        data: {
          message: 'Clear all captured requests for this webhook URL?',
          confirmLabel: 'Clear',
        },
      })
      .afterClosed()
      .subscribe((confirmed: boolean) => {
        if (!confirmed) return;
        this.requestService.clearRequests(this.tokenId).subscribe(() => {
          this.requests.set([]);
          this.total.set(0);
          this.selectedDetail.set(null);
          this.snackBar.open('All requests cleared', 'OK', { duration: 3000 });
        });
      });
  }

  deleteToken(): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        width: '340px',
        data: {
          message: 'Delete this webhook URL and all its requests permanently?',
          confirmLabel: 'Delete',
        },
      })
      .afterClosed()
      .subscribe((confirmed: boolean) => {
        if (!confirmed) return;
        this.tokenService
          .deleteToken(this.tokenId)
          .subscribe(() => this.router.navigate(['/dashboard']));
      });
  }

  openCustomResponse(): void {
    const t = this.token();
    if (!t) return;
    const ref = this.dialog.open(CustomResponseDialogComponent, {
      width: '560px',
      data: t.customResponse,
    });
    ref
      .afterClosed()
      .subscribe((result?: { action: 'save'; dto: SetCustomResponseDto } | { action: 'reset' }) => {
        if (!result) return;
        if (result.action === 'reset') {
          this.tokenService.resetCustomResponse(this.tokenId).subscribe(() => {
            this.token.update((tok) => (tok ? { ...tok, customResponse: null } : tok));
            this.snackBar.open('Custom response removed', 'OK', { duration: 3000 });
          });
        } else {
          this.tokenService.setCustomResponse(this.tokenId, result.dto).subscribe(() => {
            this.token.update((tok) => (tok ? { ...tok, customResponse: { ...result.dto } } : tok));
            this.snackBar.open('Custom response saved', 'OK', { duration: 3000 });
          });
        }
      });
  }

  copyUrl(): void {
    const url = this.token()?.webhookUrl ?? '';
    navigator.clipboard
      .writeText(url)
      .then(() => this.snackBar.open('URL copied', 'OK', { duration: 2000 }))
      .catch(() =>
        this.snackBar.open('Copy failed — please copy manually', 'OK', { duration: 3000 }),
      );
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
