# Autenticacion Y Autorizacion

Kodvian Core usa JWT bearer authentication con soporte para cookie HTTP-only.

## Archivos principales

- `backend/src/Kodvian.Core.Api/Program.cs`
- `backend/src/Kodvian.Core.Api/Controllers/AuthController.cs`
- `backend/src/Kodvian.Core.Application/Auth/**`
- `backend/src/Kodvian.Core.Application/Common/Security/**`
- `backend/src/Kodvian.Core.Infrastructure/Auth/JwtOptions.cs`
- `backend/src/Kodvian.Core.Infrastructure/Services/AuthService.cs`
- `backend/src/Kodvian.Core.Infrastructure/Services/TokenService.cs`
- `backend/src/Kodvian.Core.Infrastructure/Services/PasswordHasherService.cs`

## Login

Flujo:

1. El frontend llama `POST /api/auth/login`.
2. Backend valida credenciales.
3. Backend genera JWT.
4. Backend guarda el token en cookie `auth_token`.
5. El response devuelve informacion del usuario y permisos.

La cookie configurada por backend es:

- `HttpOnly = true`.
- `SameSite = Strict`.
- `Secure = true` fuera de Development o si la request es HTTPS.
- `Path = /`.

## Sesion actual

Endpoint:

- `GET /api/auth/me`

Devuelve el usuario autenticado, `roles`, `developerId` y permisos. El campo `role` se conserva como etiqueta compuesta para compatibilidad. El frontend usa `roles` para identificar combinaciones y restaura la sesión al recargar.

## Logout

Endpoint:

- `POST /api/auth/logout`

Elimina la cookie `auth_token`.

## Configuracion JWT

En `Program.cs`, el backend valida al iniciar:

- `Jwt:Key` requerido.
- `Jwt:Key` debe tener al menos 32 caracteres.
- En produccion no puede empezar con `SET_`.
- `Jwt:Issuer` requerido.
- `Jwt:Audience` requerido.

La validacion del token incluye issuer, audience, lifetime y signing key. El `ClockSkew` configurado es de 1 minuto.

`SessionTokenValidation` ejecuta `ISessionValidator` en `OnTokenValidated`: consulta el estado activo y `Users.SessionVersion`. La ausencia o diferencia del claim `session_version` invalida el token con 401. Cambiar roles o editar cuentas desde Equipo rota la versión; las sesiones anteriores dejan de funcionar en la siguiente petición.

## Roles

Definidos en `RoleNames.cs`:

- `Administrador`.
- `Operativo`.
- `Solo lectura`.
- `Analista`.
- `Desarrollador`.

Un usuario tiene uno o más roles mediante `UserRoles`. Solo lectura es exclusivo. Los permisos se calculan por unión de los roles activos. El JWT contiene un claim de rol por cada rol asignado, además de la versión de sesión.

## Permisos

Definidos en `PermissionCodes.cs`:

- `dashboard.read`.
- `clients.read`.
- `clients.write`.
- `projects.read`.
- `projects.write`.
- `projects.documents.read`.
- `projects.documents.write`.
- `projects.documents.delete`.
- `tasks.read`.
- `tasks.write`.
- `team.read`.
- `team.write`.
- `finances.read`.
- `finances.write`.
- `administration.read`.
- `administration.write`.
- `developer.work.read`.
- `developer.tasks.status.write`.

## Mapa rol-permisos

Definido en `RolePermissionMap.cs`.

`Administrador`:

- Gestión general, finanzas, dashboard y administración. Los permisos de Mi trabajo se agregan al combinarlo con Desarrollador.

`Operativo`:

- Clientes read/write.
- Proyectos read/write.
- Documentos de proyecto read/write/delete.
- Tareas read/write.
- Equipo read/write.

`Solo lectura`:

- Clientes read.
- Proyectos read.
- Documentos de proyecto read.
- Tareas read.
- Equipo read.
- Conserva el permiso histórico `administration.read`, pero la pantalla y API de Usuarios exigen el rol Administrador y no están disponibles para Solo lectura.

`Analista`:

- Clientes read/write.
- Proyectos read/write.
- Documentos de proyecto read/write/delete.
- Tareas read/write.
- Equipo read/write.
- Sin permisos de finanzas, cobros, pagos, contratos economicos ni administracion.

`Desarrollador`:

- Mi trabajo read.
- Cambio de estado de tareas propias.

## Policies backend

Policies declaradas en `Program.cs`:

- `AdministrationRead`.
- `DashboardRead`.
- `ClientsRead`.
- `ClientsWrite`.
- `ProjectsRead`.
- `ProjectsWrite`.
- `TasksRead`.
- `TasksWrite`.
- `TeamRead`.
- `TeamWrite`.
- `AdministratorOnly`.
- `FinancesRead`.
- `FinancesWrite`.
- `ProjectsDocumentsRead`.
- `ProjectsDocumentsWrite`.
- `ProjectsDocumentsDelete`.
- `DeveloperWorkRead`.
- `DeveloperTasksStatusWrite`.

## Acceso de desarrolladores

Los usuarios con rol `Desarrollador` deben estar vinculados a un `Developer` mediante `Users.DeveloperId`. El JWT incluye el claim `developer_id`, usado por `/api/my-work` para filtrar proyectos y tareas asignadas.

El rol `Desarrollador` no recibe permisos generales de dashboard, clientes, proyectos, tareas o finanzas. Su acceso funcional pasa por endpoints dedicados de `Mi trabajo`.

## Acceso de analistas

El rol `Analista` gestiona operacion de clientes, equipo, proyectos, documentos y tareas. No tiene permisos financieros. Los endpoints de finanzas, pagos de desarrolladores, contratos economicos y ledger quedan restringidos a `Administrador`.

## Caveats

- La visibilidad de botones en frontend no reemplaza autorizacion backend.
- Para nuevas acciones de escritura o datos sensibles, crear policy o verificar permiso explicitamente.
- `UsersController` aplica `AdministratorOnly`, lista usuarios y permite cambiar roles. El servicio vuelve a comprobar al actor y su versión antes de escribir.
- Un administrador puede combinarse con Analista o Desarrollador. Los formularios de Equipo no pueden sobrescribir esos roles ni permitir que un no administrador modifique cuentas administradoras.
