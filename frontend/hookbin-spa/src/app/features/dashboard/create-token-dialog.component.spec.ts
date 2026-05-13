import { TestBed, ComponentFixture } from '@angular/core/testing';
import { CreateTokenDialogComponent, CreateTokenResult } from './create-token-dialog.component';
import { ModalRef } from '../../shared/modal/modal-ref';
import { MODAL_REF } from '../../shared/modal/modal-tokens';

function makeRef() {
  const close = vi.fn();
  const ref = { close, afterClosed: vi.fn() } as unknown as ModalRef<CreateTokenResult | null>;
  return { ref, close };
}

function setup() {
  const { ref, close } = makeRef();
  TestBed.configureTestingModule({
    imports: [CreateTokenDialogComponent],
    providers: [{ provide: MODAL_REF, useValue: ref }],
  });
  const fixture: ComponentFixture<CreateTokenDialogComponent> = TestBed.createComponent(
    CreateTokenDialogComponent,
  );
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, close };
}

describe('CreateTokenDialogComponent', () => {
  it('cancel closes the dialog with null (DANGER ZONE invariant)', () => {
    const { component, close } = setup();

    component.cancel();

    expect(close).toHaveBeenCalledTimes(1);
    expect(close).toHaveBeenCalledWith(null);
  });

  it('confirm with empty name does not close and sets a name error', () => {
    const { component, close } = setup();
    component.name = '';

    component.confirm();

    expect(close).not.toHaveBeenCalled();
    expect(component.nameError()).toContain('required');
  });

  it('confirm with whitespace-only name does not close', () => {
    const { component, close } = setup();
    component.name = '   ';

    component.confirm();

    expect(close).not.toHaveBeenCalled();
    expect(component.nameError()).not.toBe('');
  });

  it('confirm with name > 80 characters sets length error', () => {
    const { component, close } = setup();
    component.name = 'a'.repeat(81);

    component.confirm();

    expect(close).not.toHaveBeenCalled();
    expect(component.nameError()).toContain('80');
  });

  it('confirm with valid name closes with trimmed result', () => {
    const { component, close } = setup();
    component.name = '  github-events  ';
    component.description = '  webhook events  ';

    component.confirm();

    expect(close).toHaveBeenCalledTimes(1);
    expect(close).toHaveBeenCalledWith({
      name: 'github-events',
      description: 'webhook events',
    });
  });

  it('confirm with empty description omits the description field', () => {
    const { component, close } = setup();
    component.name = 'simple-hook';
    component.description = '   ';

    component.confirm();

    expect(close).toHaveBeenCalledWith({
      name: 'simple-hook',
      description: undefined,
    });
  });

  it('confirm clears prior name error on success', () => {
    const { component } = setup();
    component.name = '';
    component.confirm();
    expect(component.nameError()).not.toBe('');

    component.name = 'valid-name';
    component.confirm();
    expect(component.nameError()).toBe('');
  });
});
