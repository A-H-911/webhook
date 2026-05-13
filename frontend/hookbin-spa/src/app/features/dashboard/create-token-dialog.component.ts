import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ModalRef } from '../../shared/modal/modal-ref';
import { MODAL_REF } from '../../shared/modal/modal-tokens';

export interface CreateTokenResult {
  name: string;
  description?: string;
}

@Component({
  selector: 'app-create-token-dialog',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="dialog">
      <div class="dialog-header">
        <h2>New Webhook URL</h2>
      </div>
      <div class="dialog-body">
        <div class="field">
          <label class="field-label" for="token-name">NAME <span class="required">*</span></label>
          <input
            id="token-name"
            class="field-input"
            type="text"
            [(ngModel)]="name"
            placeholder="e.g. github-events"
            maxlength="80"
            (keydown.enter)="confirm()"
            data-testid="token-name"
          />
          @if (nameError()) {
            <span class="field-error">{{ nameError() }}</span>
          }
        </div>
        <div class="field">
          <label class="field-label" for="token-desc"
            >DESCRIPTION <span class="optional">(optional)</span></label
          >
          <input
            id="token-desc"
            class="field-input"
            type="text"
            [(ngModel)]="description"
            placeholder="What is this endpoint for?"
            maxlength="200"
            (keydown.enter)="confirm()"
            data-testid="token-description"
          />
        </div>
      </div>
      <div class="dialog-footer">
        <button class="btn-ghost" (click)="cancel()">Cancel</button>
        <button class="btn-primary" (click)="confirm()" [disabled]="!name.trim()">
          Create endpoint
        </button>
      </div>
    </div>
  `,
  styles: [
    `
      .dialog {
        background: var(--panel);
        border-radius: var(--radius-lg);
        width: 480px;
      }

      .dialog-header {
        padding: 20px 24px 0;

        h2 {
          font-size: 16px;
          font-weight: 600;
          color: var(--text);
          margin: 0;
          letter-spacing: -0.01em;
        }
      }

      .dialog-body {
        padding: 20px 24px;
        display: flex;
        flex-direction: column;
        gap: 16px;
      }

      .field {
        display: flex;
        flex-direction: column;
        gap: 6px;
      }

      .field-label {
        font-size: 11px;
        font-weight: 600;
        letter-spacing: 0.06em;
        color: var(--text-muted);
        font-family: var(--font-mono);
      }

      .required {
        color: var(--red);
      }
      .optional {
        font-weight: 400;
        color: var(--text-faint);
        text-transform: lowercase;
      }

      .field-input {
        height: 36px;
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

      .field-error {
        font-size: 12px;
        color: var(--red);
      }

      .dialog-footer {
        display: flex;
        justify-content: flex-end;
        gap: 8px;
        padding: 0 24px 20px;
      }

      .btn-ghost {
        display: inline-flex;
        align-items: center;
        height: 34px;
        padding: 0 14px;
        border: 1px solid var(--border);
        border-radius: var(--radius);
        background: transparent;
        color: var(--text-muted);
        font-size: 13px;
        font-weight: 500;
        cursor: pointer;
        transition: background 150ms ease;

        &:hover {
          background: var(--bg-hover);
          color: var(--text);
        }
      }

      .btn-primary {
        display: inline-flex;
        align-items: center;
        height: 34px;
        padding: 0 14px;
        border: none;
        border-radius: var(--radius);
        background: var(--accent);
        color: var(--accent-fg);
        font-size: 13px;
        font-weight: 600;
        cursor: pointer;
        transition: background 150ms ease;

        &:hover:not(:disabled) {
          background: var(--accent-hover);
        }
        &:disabled {
          opacity: 0.5;
          cursor: not-allowed;
        }
      }
    `,
  ],
})
export class CreateTokenDialogComponent {
  private readonly ref = inject<ModalRef<CreateTokenResult | null>>(MODAL_REF);

  name = '';
  description = '';
  readonly nameError = signal('');

  cancel(): void {
    this.ref.close(null);
  }

  confirm(): void {
    const trimmed = this.name.trim();
    if (!trimmed) {
      this.nameError.set('Name is required.');
      return;
    }
    if (trimmed.length > 80) {
      this.nameError.set('Name must be 80 characters or fewer.');
      return;
    }
    this.nameError.set('');
    const result: CreateTokenResult = {
      name: trimmed,
      description: this.description.trim() || undefined,
    };
    this.ref.close(result);
  }
}
