import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="login-page">
      <div class="login-box">
        <div class="login-brand">
          <span class="brand-mark">
            <svg
              viewBox="0 0 24 24"
              fill="none"
              width="14"
              height="14"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
            >
              <path d="M9 2v6" />
              <path d="M15 2v6" />
              <path d="M6 8h12v3a6 6 0 0 1-12 0V8z" />
              <path d="M12 17v5" />
            </svg>
          </span>
          <span>Hookbin</span>
        </div>
        <h1 class="login-heading">Sign in to continue</h1>
        <p class="login-sub">Capture, inspect and replay incoming webhooks.</p>

        <form (ngSubmit)="onSubmit()" novalidate>
          <div class="field">
            <label class="field-label" for="username">USERNAME</label>
            <input
              id="username"
              class="field-input"
              type="text"
              name="username"
              [(ngModel)]="username"
              autocomplete="username"
              spellcheck="false"
              required
              data-testid="username"
            />
          </div>

          <div class="field">
            <label class="field-label" for="password">PASSWORD</label>
            <div class="field-password">
              <input
                id="password"
                class="field-input"
                [type]="hidePassword() ? 'password' : 'text'"
                name="password"
                [(ngModel)]="password"
                autocomplete="current-password"
                required
                data-testid="password"
              />
              <button
                type="button"
                class="toggle-pw"
                (click)="hidePassword.set(!hidePassword())"
                [attr.aria-label]="hidePassword() ? 'Show password' : 'Hide password'"
              >
                @if (hidePassword()) {
                  <svg viewBox="0 0 24 24" fill="none" width="16" height="16">
                    <path
                      d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"
                      stroke="currentColor"
                      stroke-width="2"
                      stroke-linecap="round"
                    />
                    <line
                      x1="1"
                      y1="1"
                      x2="23"
                      y2="23"
                      stroke="currentColor"
                      stroke-width="2"
                      stroke-linecap="round"
                    />
                  </svg>
                } @else {
                  <svg viewBox="0 0 24 24" fill="none" width="16" height="16">
                    <path
                      d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"
                      stroke="currentColor"
                      stroke-width="2"
                      stroke-linecap="round"
                    />
                    <circle cx="12" cy="12" r="3" stroke="currentColor" stroke-width="2" />
                  </svg>
                }
              </button>
            </div>
          </div>

          @if (error()) {
            <p class="field-error">{{ error() }}</p>
          }

          <button
            type="submit"
            class="submit-btn"
            [disabled]="loading() || !username || !password"
            data-testid="login-submit"
          >
            @if (loading()) {
              <span class="spinner"></span>
            } @else {
              Sign in
              <svg
                viewBox="0 0 24 24"
                fill="none"
                width="14"
                height="14"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
              >
                <path d="M9 18l6-6-6-6" />
              </svg>
            }
          </button>
        </form>
      </div>
    </div>
  `,
  styles: [
    `
      .login-page {
        display: flex;
        align-items: center;
        justify-content: center;
        min-height: 100vh;
        padding: 24px;
        background: radial-gradient(
          ellipse 80% 60% at 50% -20%,
          color-mix(in oklch, var(--accent), transparent 80%),
          var(--bg)
        );
      }

      .login-box {
        width: 100%;
        max-width: 380px;
        background: var(--panel);
        border: 1px solid var(--border);
        border-radius: var(--radius-lg);
        padding: 28px;
        box-shadow: var(--shadow-md);
      }

      .login-brand {
        display: flex;
        align-items: center;
        gap: 10px;
        color: var(--text);
        font-size: 20px;
        font-weight: 700;
        letter-spacing: -0.02em;
        margin-bottom: 24px;
      }

      .brand-mark {
        display: grid;
        place-items: center;
        width: 22px;
        height: 22px;
        border-radius: 5px;
        background: linear-gradient(
          135deg,
          var(--accent),
          color-mix(in oklch, var(--accent), #fff 30%)
        );
        box-shadow: 0 0 0 1px color-mix(in oklch, var(--accent), #000 25%) inset;
        color: #fff;
        flex-shrink: 0;
      }

      .login-heading {
        font-size: 20px;
        font-weight: 600;
        color: var(--text);
        margin: 0 0 6px;
        letter-spacing: -0.01em;
      }

      .login-sub {
        font-size: 13px;
        color: var(--text-muted);
        margin: 0 0 28px;
        line-height: 1.5;
      }

      .field {
        margin-bottom: 16px;
      }

      .field-label {
        display: block;
        font-size: 11px;
        font-weight: 600;
        letter-spacing: 0.06em;
        color: var(--text-muted);
        margin-bottom: 6px;
        font-family: var(--font-mono);
      }

      .field-input {
        width: 100%;
        height: 38px;
        padding: 0 12px;
        background: var(--bg-2);
        border: 1px solid var(--border);
        border-radius: var(--radius);
        color: var(--text);
        font-size: 14px;
        font-family: var(--font-sans);
        outline: none;
        box-sizing: border-box;
        transition: border-color 150ms ease;

        &:focus {
          border-color: var(--accent);
        }
      }

      .field-password {
        position: relative;
        display: flex;
        align-items: center;
      }

      .field-password .field-input {
        padding-right: 40px;
      }

      .toggle-pw {
        position: absolute;
        right: 10px;
        display: flex;
        align-items: center;
        background: none;
        border: none;
        color: var(--text-faint);
        cursor: pointer;
        padding: 0;

        &:hover {
          color: var(--text-muted);
        }
      }

      .field-error {
        font-size: 13px;
        color: var(--red);
        margin: -4px 0 12px;
      }

      .submit-btn {
        width: 100%;
        height: 40px;
        margin-top: 8px;
        background: var(--accent);
        color: var(--accent-fg);
        border: none;
        border-radius: var(--radius);
        font-size: 14px;
        font-weight: 600;
        cursor: pointer;
        display: flex;
        align-items: center;
        justify-content: center;
        gap: 6px;
        transition: background 150ms ease;

        &:hover:not(:disabled) {
          background: var(--accent-hover);
        }

        &:disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }
      }

      .spinner {
        width: 16px;
        height: 16px;
        border: 2px solid rgba(255, 255, 255, 0.3);
        border-top-color: white;
        border-radius: 50%;
        animation: spin 0.7s linear infinite;
      }

      @keyframes spin {
        to {
          transform: rotate(360deg);
        }
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
