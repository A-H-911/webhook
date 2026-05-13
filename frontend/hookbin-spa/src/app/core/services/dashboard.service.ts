import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { DashboardMetrics } from '../models/dashboard.model';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);

  readonly metrics = signal<DashboardMetrics | null>(null);

  getMetrics(): Observable<DashboardMetrics> {
    return this.http
      .get<DashboardMetrics>('/api/dashboard/metrics')
      .pipe(tap((m) => this.metrics.set(m)));
  }
}
