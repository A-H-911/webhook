import { Injectable, Injector, Type, inject } from '@angular/core';
import { Overlay } from '@angular/cdk/overlay';
import { ComponentPortal } from '@angular/cdk/portal';
import { ModalRef } from './modal-ref';
import { MODAL_REF, MODAL_DATA } from './modal-tokens';

@Injectable({ providedIn: 'root' })
export class ModalService {
  private readonly overlay = inject(Overlay);
  private readonly injector = inject(Injector);

  open<T, R = unknown>(component: Type<T>, config?: { data?: unknown }): ModalRef<R> {
    const overlayRef = this.overlay.create({
      hasBackdrop: true,
      backdropClass: 'modal-backdrop',
      panelClass: 'modal-panel',
      positionStrategy: this.overlay.position().global().centerHorizontally().centerVertically(),
      scrollStrategy: this.overlay.scrollStrategies.block(),
    });

    const modalRef = new ModalRef<R>(overlayRef);

    const childInjector = Injector.create({
      providers: [
        { provide: MODAL_REF, useValue: modalRef },
        { provide: MODAL_DATA, useValue: config?.data ?? null },
      ],
      parent: this.injector,
    });

    const portal = new ComponentPortal(component, null, childInjector);
    overlayRef.attach(portal);

    overlayRef.backdropClick().subscribe(() => modalRef.close(null as R));
    overlayRef.keydownEvents().subscribe((e) => {
      if (e.key === 'Escape') modalRef.close(null as R);
    });

    return modalRef;
  }
}
