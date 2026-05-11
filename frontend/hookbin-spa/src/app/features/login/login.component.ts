import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    FormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatIconModule,
  ],
  template: `
    <div class="login-container">
      <mat-card class="login-card">
        <mat-card-header>
          <mat-card-title>Hookbin</mat-card-title>
          <mat-card-subtitle>Sign in to continue</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <form (ngSubmit)="onSubmit()">
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Username</mat-label>
              <input
                matInput
                name="username"
                [(ngModel)]="username"
                required
                autocomplete="username"
                data-testid="username"
              />
            </mat-form-field>
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Password</mat-label>
              <input
                matInput
                [type]="hidePassword() ? 'password' : 'text'"
                name="password"
                [(ngModel)]="password"
                required
                autocomplete="current-password"
                data-testid="password"
              />
              <button
                mat-icon-button
                matSuffix
                type="button"
                (click)="hidePassword.set(!hidePassword())"
                [attr.aria-label]="hidePassword() ? 'Show password' : 'Hide password'"
              >
                <mat-icon>{{ hidePassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
              </button>
            </mat-form-field>
            @if (error()) {
              <p class="error-message">{{ error() }}</p>
            }
          </form>
        </mat-card-content>
        <mat-card-actions align="end">
          <button
            mat-flat-button
            color="primary"
            (click)="onSubmit()"
            [disabled]="loading() || !username || !password"
            data-testid="login-submit"
          >
            @if (loading()) {
              <mat-spinner diameter="20" />
            } @else {
              Sign In
            }
          </button>
        </mat-card-actions>
      </mat-card>
    </div>
  `,
  styles: [
    `
      .login-container {
        display: flex;
        align-items: center;
        justify-content: center;
        min-height: 100vh;
        padding: 16px;
      }
      .login-card {
        width: 100%;
        max-width: 400px;
      }
      .full-width {
        width: 100%;
        margin-bottom: 8px;
      }
      .error-message {
        color: var(--mat-sys-error);
        font-size: 14px;
        margin: 0 0 8px;
      }
      mat-card-content {
        padding-top: 16px;
      }
    `,
  ],
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  username = '';
  password = '';
  readonly loading = signal(false);
  readonly error = signal('');
  readonly hidePassword = signal(true);

  async onSubmit(): Promise<void> {
    if (!this.username || !this.password || this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    try {
      await this.auth.login(this.username, this.password);
      await this.router.navigate(['/dashboard']);
    } catch {
      this.error.set('Invalid username or password.');
    } finally {
      this.loading.set(false);
    }
  }
}
