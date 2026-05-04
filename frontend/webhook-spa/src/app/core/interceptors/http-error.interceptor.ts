import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, throwError } from 'rxjs';

export const httpErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const snackBar = inject(MatSnackBar);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      const message =
        err.error?.detail ??
        err.error?.title ??
        err.message ??
        'An unexpected error occurred';
      snackBar.open(message, 'Dismiss', { duration: 5000 });
      return throwError(() => err);
    })
  );
};
