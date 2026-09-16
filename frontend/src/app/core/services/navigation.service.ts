import { Injectable } from '@angular/core';

import { CurrentUser } from '../auth/auth.models';
import { NavigationItem } from '../../shared/models/navigation-item.model';

@Injectable({
  providedIn: 'root'
})
export class NavigationService {
  private readonly items: NavigationItem[] = [
    { label: 'Inicio', icon: 'home', route: '/dashboard', permission: 'dashboard.read' },
    { label: 'Mi trabajo', icon: 'work', route: '/mi-trabajo', permission: 'developer.work.read' },
    { label: 'Clientes', icon: 'groups', route: '/clientes', permission: 'clients.read' },
    { label: 'Equipo', icon: 'engineering', route: '/equipo', permission: 'team.read' },
    { label: 'Proyectos', icon: 'folder_open', route: '/proyectos', permission: 'projects.read' },
    { label: 'Tareas', icon: 'task', route: '/tareas', permission: 'tasks.read' },
    { label: 'Finanzas', icon: 'payments', route: '/finanzas', permission: 'finances.read' },
    { label: 'Administración', icon: 'admin_panel_settings', route: '/administracion', permission: 'administration.read' }
  ];

  getItems(user: CurrentUser | null): NavigationItem[] {
    if (!user) {
      return [];
    }

    return this.items.filter(item => (!item.permission || user.permissions.includes(item.permission))
      && (item.route !== '/administracion' || user.roles.includes('Administrador')));
  }
}
