import {
  Component,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
  AfterViewInit,
  viewChild,
  ElementRef,
} from '@angular/core';
import { Router } from '@angular/router';
import { DatePipe } from '@angular/common';
import { forkJoin } from 'rxjs';
import { TokenService } from '../../core/services/token.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { TokenListItem } from '../../core/models/token.model';
import { SparklineComponent } from '../../shared/sparkline/sparkline.component';
import { CreateTokenDialogComponent } from './create-token-dialog.component';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog/confirm-dialog.component';
import { ModalService } from '../../shared/modal/modal.service';
import { ToastService } from '../../shared/toast/toast.service';
import { BreadcrumbService } from '../../core/services/breadcrumb.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [DatePipe, SparklineComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit, OnDestroy, AfterViewInit {
  private readonly tokenService = inject(TokenService);
  private readonly dashboardService = inject(DashboardService);
  private readonly router = inject(Router);
  private readonly modal = inject(ModalService);
  private readonly toast = inject(ToastService);
  private readonly breadcrumb = inject(BreadcrumbService);

  private observer: IntersectionObserver | null = null;
  private skip = 0;
  private readonly take = 50;

  readonly sentinel = viewChild<ElementRef<HTMLElement>>('sentinel');

  readonly tokens = signal<TokenListItem[]>([]);
  readonly loading = signal(false);
  readonly loadingMore = signal(false);
  readonly refreshing = signal(false);
  readonly hasMore = signal(false);
  readonly total = signal(0);

  readonly metrics = computed(() => this.dashboardService.metrics());

  readonly liveCount = computed(() => this.metrics()?.liveEndpoints ?? 0);

  ngOnInit(): void {
    this.breadcrumb.setDashboard();
    this.load();
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
    this.breadcrumb.clear();
  }

  ngAfterViewInit(): void {
    this.setupObserver();
  }

  private setupObserver(): void {
    const el = this.sentinel()?.nativeElement;
    if (!el) return;
    this.observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting && this.hasMore() && !this.loadingMore() && !this.loading()) {
          this.loadMore();
        }
      },
      { threshold: 0.1 },
    );
    this.observer.observe(el);
  }

  load(): void {
    this.loading.set(true);
    this.skip = 0;
    forkJoin([
      this.tokenService.getTokens(0, this.take),
      this.dashboardService.getMetrics(),
    ]).subscribe({
      next: ([page]) => {
        this.tokens.set(page.items);
        this.hasMore.set(page.hasMore);
        this.total.set(page.total);
        this.skip = page.items.length;
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  refresh(): void {
    if (this.refreshing()) return;
    this.refreshing.set(true);
    this.skip = 0;
    forkJoin([
      this.tokenService.getTokens(0, this.take),
      this.dashboardService.getMetrics(),
    ]).subscribe({
      next: ([page]) => {
        this.tokens.set(page.items);
        this.hasMore.set(page.hasMore);
        this.total.set(page.total);
        this.skip = page.items.length;
        setTimeout(() => this.refreshing.set(false), 500);
      },
      error: () => this.refreshing.set(false),
    });
  }

  private loadMore(): void {
    this.loadingMore.set(true);
    this.tokenService.getTokens(this.skip, this.take).subscribe({
      next: (page) => {
        this.tokens.update((list) => [...list, ...page.items]);
        this.hasMore.set(page.hasMore);
        this.skip += page.items.length;
        this.loadingMore.set(false);
      },
      error: () => this.loadingMore.set(false),
    });
  }

  openCreate(): void {
    const ref = this.modal.open<
      CreateTokenDialogComponent,
      { name: string; description?: string } | null
    >(CreateTokenDialogComponent);
    ref.afterClosed().subscribe((result) => {
      if (result == null) return;
      this.tokenService.createToken(result.name, result.description).subscribe((token) => {
        this.total.update((n) => n + 1);
        this.dashboardService.getMetrics().subscribe();
        this.toast.show('Webhook URL created');
        this.router.navigate(['/tokens', token.id]);
      });
    });
  }

  open(token: TokenListItem): void {
    this.router.navigate(['/tokens', token.id]);
  }

  delete(token: TokenListItem, event: MouseEvent): void {
    event.stopPropagation();
    const ref = this.modal.open<ConfirmDialogComponent, boolean | null>(ConfirmDialogComponent, {
      data: {
        title: `Delete "${token.name}"?`,
        message: "The URL and every captured request will be deleted. This can't be undone.",
        confirmLabel: 'Delete URL',
      },
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (!confirmed) return;
      this.tokenService.deleteToken(token.id).subscribe(() => {
        this.tokens.update((list) => list.filter((t) => t.id !== token.id));
        this.total.update((n) => n - 1);
        this.dashboardService.getMetrics().subscribe();
        this.toast.show('Deleted');
      });
    });
  }

  copyUrl(token: TokenListItem, event: MouseEvent): void {
    event.stopPropagation();
    navigator.clipboard.writeText(token.webhookUrl).then(() => this.toast.show('URL copied', 2000));
  }

  truncatedUrl(token: TokenListItem): string {
    const url = token.webhookUrl;
    const idx = url.lastIndexOf('/');
    if (idx < 0) return url;
    const prefix = url.substring(0, idx + 1);
    const id = token.token;
    return `${prefix}${id.substring(0, 8)}…${id.substring(id.length - 4)}`;
  }

  hostPart(token: TokenListItem): string {
    try {
      const url = new URL(token.webhookUrl);
      const idx = url.pathname.lastIndexOf('/');
      return url.host + url.pathname.substring(0, idx + 1);
    } catch {
      return '';
    }
  }

  pathPart(token: TokenListItem): string {
    const id = token.token;
    return `${id.substring(0, 8)}…${id.substring(id.length - 4)}`;
  }

  isActive(token: TokenListItem): boolean {
    if (!token.lastReceivedAt) return false;
    return Date.now() - new Date(token.lastReceivedAt).getTime() < 5 * 60 * 1000;
  }
}
