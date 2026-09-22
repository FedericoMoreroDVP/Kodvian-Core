import { Routes } from '@angular/router';

import { authGuard } from './core/guards/auth.guard';
import { permissionGuard } from './core/guards/permission.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./login-page.component').then((m) => m.LoginPageComponent)
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/main-layout.component').then((m) => m.MainLayoutComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', canActivate: [permissionGuard], data: { permission: 'dashboard.read' }, loadChildren: () => import('./modules/dashboard/dashboard.routes').then((m) => m.DASHBOARD_ROUTES) },
      { path: 'mi-trabajo', canActivate: [permissionGuard], data: { permission: 'developer.work.read' }, loadChildren: () => import('./modules/mi-trabajo/mi-trabajo.routes').then((m) => m.MI_TRABAJO_ROUTES) },
      { path: 'clientes', canActivate: [permissionGuard], data: { permission: 'clients.read' }, loadChildren: () => import('./modules/clientes/clientes.routes').then((m) => m.CLIENTES_ROUTES) },
      { path: 'desarrolladores', pathMatch: 'full', redirectTo: 'equipo' },
      { path: 'equipo', canActivate: [permissionGuard], data: { permission: 'team.read' }, loadChildren: () => import('./modules/desarrolladores/desarrolladores.routes').then((m) => m.DESARROLLADORES_ROUTES) },
      { path: 'proyectos', canActivate: [permissionGuard], data: { permission: 'projects.read' }, loadChildren: () => import('./modules/proyectos/proyectos.routes').then((m) => m.PROYECTOS_ROUTES) },
      { path: 'tareas', canActivate: [permissionGuard], data: { permission: 'tasks.read' }, loadChildren: () => import('./modules/tareas/tareas.routes').then((m) => m.TAREAS_ROUTES) },
      { path: 'finanzas', canActivate: [permissionGuard], data: { permission: 'finances.read' }, loadChildren: () => import('./modules/finanzas/finanzas.routes').then((m) => m.FINANZAS_ROUTES) },
      { path: 'vision-financiera', canActivate: [permissionGuard], data: { permission: 'finances.read' }, loadChildren: () => import('./modules/vision-financiera/vision-financiera.routes').then(m => m.VISION_FINANCIERA_ROUTES) },
      { path: 'administracion', loadChildren: () => import('./modules/administracion/administracion.routes').then((m) => m.ADMINISTRACION_ROUTES) }
    ]
  },
  { path: '**', redirectTo: 'login' }
];
