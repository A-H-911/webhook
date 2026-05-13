import { Component, inject } from '@angular/core';
import {
  FormsModule,
  ReactiveFormsModule,
  FormControl,
  ValidatorFn,
  AbstractControl,
  ValidationErrors,
} from '@angular/forms';
import { ModalRef } from '../../shared/modal/modal-ref';
import { MODAL_REF, MODAL_DATA } from '../../shared/modal/modal-tokens';
import { CustomResponse, SetCustomResponseDto } from '../../core/models/token.model';

function jsonValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const val: string = control.value ?? '';
    if (!val.trim()) return null;
    try {
      JSON.parse(val);
      return null;
    } catch {
      return { invalidJson: true };
    }
  };
}

type DialogResult = { action: 'save'; dto: SetCustomResponseDto } | { action: 'reset' };

@Component({
  selector: 'app-custom-response-dialog',
  standalone: true,
  imports: [FormsModule, ReactiveFormsModule],
  template: `
    <div class="dialog">
      <div class="dialog-header">
        <h2>Custom Response</h2>
        <p class="dialog-sub">Tell incoming requests exactly what to expect back.</p>
      </div>
      <div class="dialog-body">
        <div class="field">
          <label class="field-label">STATUS</label>
          <div class="select-wrapper">
            <select class="field-select" [(ngModel)]="statusCode">
              @for (code of statusCodes; track code) {
                <option [value]="code">{{ code }}</option>
              }
            </select>
          </div>
        </div>
        <div class="field">
          <label class="field-label">CONTENT-TYPE</label>
          <input
            class="field-input"
            type="text"
            [(ngModel)]="contentType"
            placeholder="application/json"
          />
        </div>
        <div class="field">
          <label class="field-label">RESPONSE BODY</label>
          <textarea
            class="field-textarea"
            [(ngModel)]="body"
            rows="5"
            placeholder='{"ok": true}'
          ></textarea>
        </div>
        <div class="field">
          <label class="field-label"
            >EXTRA HEADERS <span class="field-hint">(JSON OBJECT)</span></label
          >
          <textarea
            class="field-textarea"
            [formControl]="headersControl"
            rows="3"
            placeholder='{"X-Custom": "value"}'
          ></textarea>
          @if (headersControl.hasError('invalidJson')) {
            <span class="field-error">Must be valid JSON object</span>
          }
        </div>
      </div>
      <div class="dialog-footer">
        @if (data) {
          <button class="btn-ghost-warn" (click)="reset()">Remove</button>
        }
        <div style="flex:1"></div>
        <button class="btn-ghost" (click)="cancel()">Cancel</button>
        <button class="btn-primary" (click)="save()" [disabled]="headersControl.invalid">
          Save response
        </button>
      </div>
    </div>
  `,
  styles: [
    `
      .dialog {
        background: var(--panel);
        border-radius: var(--radius-lg);
        width: 560px;
      }
      .dialog-header {
        padding: 20px 24px 0;
        h2 {
          font-size: 16px;
          font-weight: 600;
          color: var(--text);
          margin: 0 0 4px;
        }
      }
      .dialog-sub {
        font-size: 13px;
        color: var(--text-muted);
        margin: 0;
      }
      .dialog-body {
        padding: 16px 24px;
        display: flex;
        flex-direction: column;
        gap: 14px;
      }
      .field {
        display: flex;
        flex-direction: column;
        gap: 5px;
      }
      .field-label {
        font-size: 11px;
        font-weight: 600;
        letter-spacing: 0.06em;
        color: var(--text-muted);
        font-family: var(--font-mono);
      }
      .field-hint {
        font-weight: 400;
        color: var(--text-faint);
        text-transform: lowercase;
      }
      .select-wrapper {
        position: relative;

        &::after {
          content: '';
          position: absolute;
          right: 10px;
          top: 50%;
          transform: translateY(-50%);
          width: 0;
          height: 0;
          border-left: 4px solid transparent;
          border-right: 4px solid transparent;
          border-top: 5px solid var(--text-muted);
          pointer-events: none;
        }
      }
      .field-select {
        width: 100%;
        height: 34px;
        padding: 0 30px 0 10px;
        background: var(--bg-2);
        border: 1px solid var(--border);
        border-radius: var(--radius);
        color: var(--text);
        font-size: 13px;
        font-family: var(--font-mono);
        outline: none;
        appearance: none;
        cursor: pointer;
        box-sizing: border-box;
        transition: border-color 150ms ease;

        &:focus {
          border-color: var(--accent);
        }
      }
      .field-input,
      .field-textarea {
        background: var(--bg-2);
        border: 1px solid var(--border);
        border-radius: var(--radius);
        color: var(--text);
        font-size: 13px;
        font-family: var(--font-mono);
        outline: none;
        transition: border-color 150ms ease;
        &:focus {
          border-color: var(--accent);
        }
      }
      .field-input {
        height: 34px;
        padding: 0 10px;
        box-sizing: border-box;
        width: 100%;
      }
      .field-textarea {
        padding: 8px 10px;
        resize: vertical;
        box-sizing: border-box;
        width: 100%;
      }
      .field-error {
        font-size: 12px;
        color: var(--red);
      }
      .dialog-footer {
        display: flex;
        align-items: center;
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
        &:hover {
          background: var(--bg-hover);
          color: var(--text);
        }
      }
      .btn-ghost-warn {
        display: inline-flex;
        align-items: center;
        height: 34px;
        padding: 0 14px;
        border: 1px solid rgba(239, 68, 68, 0.3);
        border-radius: var(--radius);
        background: transparent;
        color: var(--red);
        font-size: 13px;
        font-weight: 500;
        cursor: pointer;
        &:hover {
          background: rgba(239, 68, 68, 0.08);
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
export class CustomResponseDialogComponent {
  readonly data = inject<CustomResponse | null>(MODAL_DATA);
  private readonly ref = inject<ModalRef<DialogResult | null>>(MODAL_REF);

  readonly statusCodes = [200, 201, 202, 204, 301, 400, 401, 404, 500];

  statusCode = this.data?.statusCode ?? 200;
  contentType = this.data?.contentType ?? 'application/json';
  body = this.data?.body ?? '{"ok": true}';
  readonly headersControl = new FormControl(this.data?.headers ?? '{}', [jsonValidator()]);

  cancel(): void {
    this.ref.close(null);
  }

  save(): void {
    if (this.headersControl.invalid) return;
    const rawHeaders = (this.headersControl.value ?? '').trim() || '{}';
    const dto: SetCustomResponseDto = {
      statusCode: this.statusCode,
      contentType: this.contentType.trim() || 'application/json',
      body: this.body.trim() || null,
      headers: rawHeaders,
    };
    this.ref.close({ action: 'save', dto } satisfies DialogResult);
  }

  reset(): void {
    this.ref.close({ action: 'reset' } satisfies DialogResult);
  }
}
