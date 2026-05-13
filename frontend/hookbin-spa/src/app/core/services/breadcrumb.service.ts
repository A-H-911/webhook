import { Injectable, signal } from '@angular/core';

export type BreadcrumbContext = { type: 'dashboard' } | { type: 'token'; name: string } | null;

@Injectable({ providedIn: 'root' })
export class BreadcrumbService {
  readonly context = signal<BreadcrumbContext>(null);

  setDashboard(): void {
    this.context.set({ type: 'dashboard' });
  }

  setToken(name: string): void {
    this.context.set({ type: 'token', name });
  }

  clear(): void {
    this.context.set(null);
  }
}
