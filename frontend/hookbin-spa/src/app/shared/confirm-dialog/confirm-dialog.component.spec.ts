import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { ConfirmDialogComponent, ConfirmDialogData } from './confirm-dialog.component';

function setup(data: ConfirmDialogData = { message: 'Are you sure?' }) {
  TestBed.configureTestingModule({
    imports: [ConfirmDialogComponent],
    providers: [
      { provide: MAT_DIALOG_DATA, useValue: data },
      { provide: MatDialogRef, useValue: {} },
    ],
  });
  const fixture = TestBed.createComponent(ConfirmDialogComponent);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance };
}

describe('ConfirmDialogComponent', () => {
  it('exposes injected data', () => {
    const { component } = setup({ message: 'Delete?' });
    expect(component.data.message).toBe('Delete?');
  });

  it('uses default confirmLabel when not provided', () => {
    const { component } = setup({ message: 'Confirm?' });
    expect(component.data.confirmLabel).toBeUndefined();
  });

  it('uses provided confirmLabel', () => {
    const { component } = setup({ message: 'Remove?', confirmLabel: 'Remove' });
    expect(component.data.confirmLabel).toBe('Remove');
  });

  it('renders message in the template', () => {
    const { fixture } = setup({ message: 'Are you absolutely sure?' });
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('Are you absolutely sure?');
  });
});
