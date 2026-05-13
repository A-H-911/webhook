import { Component, inject, OnInit, DestroyRef, computed } from '@angular/core';
import { Router, RouterOutlet, RouterLink, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { signal } from '@angular/core';
import { ThemeService } from './services/theme.service';
import { AuthService } from './core/services/auth.service';
import { VersionService } from './core/services/version.service';
import { DashboardService } from './core/services/dashboard.service';
import { BreadcrumbService } from './core/services/breadcrumb.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  protected readonly theme = inject(ThemeService);
  protected readonly auth = inject(AuthService);
  protected readonly versionService = inject(VersionService);
  protected readonly dashboardService = inject(DashboardService);
  protected readonly breadcrumb = inject(BreadcrumbService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly isDashboard = signal(false);
  readonly totalEndpoints = computed(() => this.dashboardService.metrics()?.totalEndpoints ?? 0);

  ngOnInit(): void {
    this.versionService.load();
    this.router.events
      .pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((e) => {
        const url = (e as NavigationEnd).urlAfterRedirects;
        this.isDashboard.set(url === '/dashboard' || url.startsWith('/dashboard?'));
      });
  }

  async logout(): Promise<void> {
    await this.auth.logout();
    this.router.navigate(['/login']);
  }
}
