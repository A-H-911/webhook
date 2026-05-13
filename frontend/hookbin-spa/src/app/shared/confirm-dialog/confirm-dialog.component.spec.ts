import { TestBed } from '@angular/core/testing';
import { ConfirmDialogComponent, ConfirmDialogData } from './confirm-dialog.component';
import { MODAL_DATA, MODAL_REF } from '../modal/modal-tokens';
import { ModalRef } from '../modal/modal-ref';

function setup(data: ConfirmDialogData = { message: 'Are you sure?' }) {
  const modalRef = { close: vi.fn() } as unknown as ModalRef<unknown>;
  TestBed.configureTestingModule({
    imports: [ConfirmDialogComponent],
    providers: [
      { provide: MODAL_DATA, useValue: data },
      { provide: MODAL_REF, useValue: modalRef },
    ],
  });
  const fixture = TestBed.createComponent(ConfirmDialogComponent);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, modalRef };
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

  it('cancel closes with false', () => {
    const { component, modalRef } = setup({ message: 'Cancel?' });
    component.cancel();
    expect(modalRef.close).toHaveBeenCalledWith(false);
  });

  it('confirm closes with true', () => {
    const { component, modalRef } = setup({ message: 'Confirm?' });
    component.confirm();
    expect(modalRef.close).toHaveBeenCalledWith(true);
  });
});
