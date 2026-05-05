import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { CustomResponse, SetCustomResponseDto } from '../../core/models/token.model';

type DialogResult = { action: 'save'; dto: SetCustomResponseDto } | { action: 'reset' };

@Component({
  selector: 'app-custom-response-dialog',
  standalone: true,
  imports: [
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatSelectModule,
  ],
  template: `
    <h2 mat-dialog-title>Custom Response</h2>
    <mat-dialog-content>
      <mat-form-field appearance="outline" style="width:100%">
        <mat-label>Status Code</mat-label>
        <mat-select [(ngModel)]="statusCode">
          @for (code of statusCodes; track code) {
            <mat-option [value]="code">{{ code }}</mat-option>
          }
        </mat-select>
      </mat-form-field>

      <mat-form-field appearance="outline" style="width:100%; margin-top:8px">
        <mat-label>Content-Type</mat-label>
        <input matInput [(ngModel)]="contentType" placeholder="application/json" />
      </mat-form-field>

      <mat-form-field appearance="outline" style="width:100%; margin-top:8px">
        <mat-label>Response Body</mat-label>
        <textarea matInput [(ngModel)]="body" rows="6" placeholder='{"status":"ok"}'></textarea>
      </mat-form-field>

      <mat-form-field appearance="outline" style="width:100%; margin-top:8px">
        <mat-label>Extra Headers (JSON object)</mat-label>
        <textarea
          matInput
          [(ngModel)]="headersJson"
          rows="3"
          placeholder='{"X-Custom":"value"}'
        ></textarea>
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (data) {
        <button mat-button color="warn" (click)="reset()">Remove</button>
      }
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button color="primary" (click)="save()">Save</button>
    </mat-dialog-actions>
  `,
})
export class CustomResponseDialogComponent {
  private readonly ref = inject(MatDialogRef<CustomResponseDialogComponent>);
  readonly data = inject<CustomResponse | null>(MAT_DIALOG_DATA);

  readonly statusCodes = [200, 201, 204, 400, 401, 403, 404, 409, 422, 500, 502, 503];

  statusCode = this.data?.statusCode ?? 200;
  contentType = this.data?.contentType ?? 'application/json';
  body = this.data?.body ?? '';
  headersJson = this.data?.headers ?? '{}';

  save(): void {
    const rawHeaders = this.headersJson.trim() || '{}';
    try {
      JSON.parse(rawHeaders);
    } catch {
      return;
    }

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
