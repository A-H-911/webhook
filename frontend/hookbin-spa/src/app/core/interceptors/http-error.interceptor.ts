import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { ToastService } from '../../shared/toast/toast.service';
import { catchError, throwError } from 'rxjs';

export const httpErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 401 && !req.url.includes('/api/auth/')) {
        router.navigate(['/login']);
        return throwError(() => err);
      }
      if (req.url.includes('/api/auth/')) {
        return throwError(() => err);
      }
      const message =
        err.error?.detail ?? err.error?.title ?? err.message ?? 'An unexpected error occurred';
      toast.show(message, 5000);
      return throwError(() => err);
    }),
  );
};
