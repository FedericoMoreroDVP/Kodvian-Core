export function homeRoute(user: { developerId?: string; permissions: string[] } | null): string {
  if (!user) return '/login';
  if (user.permissions.includes('dashboard.read')) return '/dashboard';
  if (user.developerId && user.permissions.includes('developer.work.read')) return '/mi-trabajo';
  if (user.permissions.includes('projects.read')) return '/proyectos';
  if (user.permissions.includes('clients.read')) return '/clientes';
  if (user.permissions.includes('tasks.read')) return '/tareas';
  if (user.permissions.includes('team.read')) return '/equipo';
  return '/login';
}
