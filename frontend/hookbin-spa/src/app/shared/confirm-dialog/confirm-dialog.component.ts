import { Component, inject } from '@angular/core';
import { ModalRef } from '../modal/modal-ref';
import { MODAL_REF, MODAL_DATA } from '../modal/modal-tokens';

export interface ConfirmDialogData {
  title?: string;
  message: string;
  confirmLabel?: string;
}

@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [],
  template: `
    <div class="dialog">
      <div class="dialog-header">
        <h2>{{ data.title ?? 'Confirm' }}</h2>
      </div>
      <div class="dialog-body">
        <p>{{ data.message }}</p>
      </div>
      <div class="dialog-footer">
        <button class="btn-ghost" (click)="cancel()">Cancel</button>
        <button class="btn-danger" (click)="confirm()">
          {{ data.confirmLabel ?? 'Confirm' }}
        </button>
      </div>
    </div>
  `,
  styles: [
    `
      .dialog {
        background: var(--panel);
        border-radius: var(--radius-lg);
        width: 380px;
      }
      .dialog-header {
        padding: 20px 24px 0;
        h2 {
          font-size: 16px;
          font-weight: 600;
          color: var(--text);
          margin: 0;
        }
      }
      .dialog-body {
        padding: 12px 24px 4px;
        p {
          font-size: 13px;
          color: var(--text-muted);
          margin: 0;
          line-height: 1.5;
        }
      }
      .dialog-footer {
        display: flex;
        justify-content: flex-end;
        gap: 8px;
        padding: 16px 24px 20px;
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
        &:hover {
          background: var(--bg-hover);
          color: var(--text);
        }
      }
      .btn-danger {
        display: inline-flex;
        align-items: center;
        height: 34px;
        padding: 0 14px;
        border: none;
        border-radius: var(--radius);
        background: var(--red);
        color: #fff;
        font-size: 13px;
        font-weight: 600;
        cursor: pointer;
        &:hover {
          opacity: 0.9;
        }
      }
    `,
  ],
})
export class ConfirmDialogComponent {
  readonly data = inject<ConfirmDialogData>(MODAL_DATA);
  private readonly ref = inject<ModalRef<boolean | null>>(MODAL_REF);

  cancel(): void {
    this.ref.close(false);
  }

  confirm(): void {
    this.ref.close(true);
  }
}
