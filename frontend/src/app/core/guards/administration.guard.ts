import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { map } from 'rxjs';

import { AuthSessionService } from '../auth/auth-session.service';

import { homeRoute } from '../auth/home-route';

export const administrationGuard: CanActivateFn = () => {
  const session = inject(AuthSessionService);
  const router = inject(Router);

  return session.ensureSessionLoaded().pipe(
    map(user => user?.roles.includes('Administrador') ? true : router.createUrlTree([homeRoute(user)]))
  );
};
