import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./pages/login/login').then((m) => m.Login),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/shell').then((m) => m.Shell),
    children: [
      { path: 'approvals', loadComponent: () => import('./pages/approvals').then((m) => m.Approvals) },
      { path: 'locations', loadComponent: () => import('./pages/locations').then((m) => m.Locations) },
      { path: 'users', loadComponent: () => import('./pages/users').then((m) => m.Users) },
      { path: 'bookings', loadComponent: () => import('./pages/bookings').then((m) => m.Bookings) },
      { path: 'blackouts', loadComponent: () => import('./pages/blackouts').then((m) => m.Blackouts) },
      { path: 'settings', loadComponent: () => import('./pages/settings').then((m) => m.Settings) },
      { path: '', pathMatch: 'full', redirectTo: 'approvals' },
    ],
  },
  { path: '**', redirectTo: '' },
];
