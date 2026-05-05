import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-create-token-dialog',
  standalone: true,
  imports: [FormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>New Webhook URL</h2>
    <mat-dialog-content>
      <mat-form-field appearance="outline" style="width:100%">
        <mat-label>Description (optional)</mat-label>
        <input
          matInput
          [(ngModel)]="description"
          placeholder="e.g. GitHub events"
          maxlength="200"
          (keydown.enter)="confirm()"
        />
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="null">Cancel</button>
      <button mat-flat-button color="primary" (click)="confirm()">Create</button>
    </mat-dialog-actions>
  `,
})
export class CreateTokenDialogComponent {
  private readonly ref = inject(MatDialogRef<CreateTokenDialogComponent>);
  description = '';

  confirm(): void {
    this.ref.close(this.description.trim());
  }
}
