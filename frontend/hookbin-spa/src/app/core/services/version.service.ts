import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';

@Injectable({ providedIn: 'root' })
export class VersionService {
  private readonly http = inject(HttpClient);
  readonly version = signal<string>('');

  load(): void {
    this.http.get<{ version: string }>('/api/version').subscribe({
      next: (r) => this.version.set(r.version),
    });
  }
}
