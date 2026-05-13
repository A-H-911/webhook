import { Subject, Observable } from 'rxjs';
import { OverlayRef } from '@angular/cdk/overlay';

export class ModalRef<R = unknown> {
  private readonly resultSubject = new Subject<R | null>();

  constructor(private readonly overlayRef: OverlayRef) {}

  close(result: R | null = null): void {
    this.overlayRef.dispose();
    this.resultSubject.next(result);
    this.resultSubject.complete();
  }

  afterClosed(): Observable<R | null> {
    return this.resultSubject.asObservable();
  }
}
