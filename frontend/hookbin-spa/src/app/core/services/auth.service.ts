import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface AuthUser {
  username: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly _user = signal<AuthUser | null>(null);
  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => this._user() !== null);

  constructor(private http: HttpClient) {}

  async initialize(): Promise<void> {
    try {
      const user = await firstValueFrom(this.http.get<AuthUser>('/api/auth/me'));
      this._user.set(user);
    } catch {
      this._user.set(null);
    }
  }

  async login(username: string, password: string): Promise<void> {
    const user = await firstValueFrom(
      this.http.post<AuthUser>('/api/auth/login', { username, password }),
    );
    this._user.set(user);
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post<void>('/api/auth/logout', {}));
    this._user.set(null);
  }
}
