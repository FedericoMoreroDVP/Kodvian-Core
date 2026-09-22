import { Routes } from '@angular/router';

export const VISION_FINANCIERA_ROUTES: Routes = [
  { path: '', loadComponent: () => import('./vision-financiera-page.component').then(m => m.VisionFinancieraPageComponent) }
];
