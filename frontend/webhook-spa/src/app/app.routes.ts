import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent)
  },
  {
    path: 'tokens/:id',
    loadComponent: () =>
      import('./features/token-detail/token-detail.component').then(m => m.TokenDetailComponent)
  },
  { path: '**', redirectTo: 'dashboard' }
];
