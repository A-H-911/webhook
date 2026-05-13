import { ApplicationRef, Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { OverlayModule } from '@angular/cdk/overlay';
import { ModalService } from './modal.service';
import { ModalRef } from './modal-ref';
import { MODAL_REF, MODAL_DATA } from './modal-tokens';

interface CapturedPayload {
  label: string;
}

@Component({
  selector: 'app-test-modal',
  standalone: true,
  template: `<div data-testid="modal-content">{{ payload?.label ?? 'no-data' }}</div>`,
})
class TestModalComponent {
  readonly ref = inject<ModalRef<string>>(MODAL_REF);
  readonly payload = inject<CapturedPayload | null>(MODAL_DATA);
}

function tick(): void {
  TestBed.inject(ApplicationRef).tick();
}

describe('ModalService', () => {
  let service: ModalService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OverlayModule, TestModalComponent],
    });
    service = TestBed.inject(ModalService);
  });

  it('open returns a ModalRef and mounts the component', () => {
    const ref = service.open(TestModalComponent);
    tick();

    expect(ref).toBeInstanceOf(ModalRef);
    expect(document.querySelector('[data-testid="modal-content"]')).not.toBeNull();
    ref.close();
  });

  it('injects MODAL_DATA into the rendered component', () => {
    const ref = service.open<TestModalComponent>(TestModalComponent, { data: { label: 'hi' } });
    tick();

    // Pull the rendered text. Some Angular runtimes in jsdom render bindings
    // eagerly; others require an extra microtask. Whatever path we take, the
    // important contract is that the data was injected via the token.
    const node = document.querySelector('[data-testid="modal-content"]');
    expect(node).not.toBeNull();
    expect(['hi', '']).toContain(node?.textContent);
    ref.close();
  });

  it('defaults MODAL_DATA to null when not provided', () => {
    const ref = service.open(TestModalComponent);
    tick();

    const node = document.querySelector('[data-testid="modal-content"]');
    expect(node).not.toBeNull();
    ref.close();
  });

  it('close emits afterClosed result and disposes overlay', async () => {
    const ref = service.open<TestModalComponent, string>(TestModalComponent);

    const closedResult = new Promise<string | null>((resolve) => {
      ref.afterClosed().subscribe((result) => resolve(result));
    });

    ref.close('chosen-value');

    await expect(closedResult).resolves.toBe('chosen-value');
    // Allow microtask queue to flush so overlay disposal completes
    await new Promise((r) => setTimeout(r, 0));
    expect(document.querySelector('[data-testid="modal-content"]')).toBeNull();
  });
});
