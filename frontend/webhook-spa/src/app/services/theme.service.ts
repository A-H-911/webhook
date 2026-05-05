import { computed, Injectable, signal } from '@angular/core';

type ColorScheme = 'dark' | 'light';

const STORAGE_KEY = 'color-scheme';
const DEFAULT_SCHEME: ColorScheme = 'dark';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly scheme = signal<ColorScheme>(this.readPreference());
  readonly isDark = computed(() => this.scheme() === 'dark');

  constructor() {
    this.applyClass(this.scheme());
  }

  toggle(): void {
    const next: ColorScheme = this.scheme() === 'dark' ? 'light' : 'dark';
    this.scheme.set(next);
    this.applyClass(next);
    this.writePreference(next);
  }

  private readPreference(): ColorScheme {
    try {
      return localStorage.getItem(STORAGE_KEY) === 'light' ? 'light' : DEFAULT_SCHEME;
    } catch {
      return DEFAULT_SCHEME;
    }
  }

  private writePreference(scheme: ColorScheme): void {
    try {
      localStorage.setItem(STORAGE_KEY, scheme);
    } catch {
      // ignore storage errors
    }
  }

  private applyClass(scheme: ColorScheme): void {
    document.documentElement.classList.toggle('dark-theme', scheme === 'dark');
  }
}
