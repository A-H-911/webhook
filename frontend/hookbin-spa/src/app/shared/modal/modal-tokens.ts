import { InjectionToken } from '@angular/core';
import { ModalRef } from './modal-ref';

export const MODAL_REF = new InjectionToken<ModalRef<unknown>>('MODAL_REF');
export const MODAL_DATA = new InjectionToken<unknown>('MODAL_DATA');
