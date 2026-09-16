import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';

import { AuthSessionService } from '../auth/auth-session.service';
import { homeRoute } from '../auth/home-route';

export const permissionGuard: CanActivateFn = (route) => {
  const session = inject(AuthSessionService);
  const router = inject(Router);
  const permission = route.data?.['permission'] as string | undefined;

  if (!permission) {
    return true;
  }

  return session.ensureSessionLoaded().pipe(
    map((user) => {
      if (!user) {
        return router.createUrlTree(['/login']);
      }

      return user.permissions.includes(permission) ? true : router.createUrlTree([homeRoute(user)]);
    })
  );
};
