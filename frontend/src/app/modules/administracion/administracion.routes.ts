import { Routes } from '@angular/router';

import { administrationGuard } from '../../core/guards/administration.guard';
import { AdministracionPageComponent } from './administracion-page.component';

export const ADMINISTRACION_ROUTES: Routes = [
  { path: '', component: AdministracionPageComponent, canActivate: [administrationGuard] },
  { path: 'configuracion-ia', loadComponent: () => import('./ai-provider-configuration-page.component').then(m => m.AiProviderConfigurationPageComponent), canActivate: [administrationGuard] },
  { path: 'uso-ia', loadComponent: () => import('./ai-usage-page.component').then(m => m.AiUsagePageComponent), canActivate: [administrationGuard] }
];
