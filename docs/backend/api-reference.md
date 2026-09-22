# API Reference

Esta referencia lista los endpoints detectados en `backend/src/Kodvian.Core.Api/Controllers`.

## Auth

Controller: `backend/src/Kodvian.Core.Api/Controllers/AuthController.cs`

Base route: `/api/auth`

| Metodo | Ruta | Descripcion |
|---|---|---|
| POST | `/api/auth/login` | Login anonimo, rate-limited, crea cookie `auth_token`. |
| POST | `/api/auth/logout` | Cierra sesion y elimina cookie. |
| GET | `/api/auth/me` | Devuelve usuario actual y permisos. |

## Health

Controller: `backend/src/Kodvian.Core.Api/Controllers/HealthController.cs`

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/health` | Health response de API. |
| GET | `/healthz` | Healthcheck mapeado en `Program.cs` para Railway. |

## Dashboard

Controller: `backend/src/Kodvian.Core.Api/Controllers/DashboardController.cs`

Base route: `/api/dashboard`

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/dashboard/overview` | KPIs, tareas prioritarias, cobranzas proximas y movimientos recientes. |

## Clients

Controller: `backend/src/Kodvian.Core.Api/Controllers/ClientsController.cs`

Base route: `/api/clients`

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/clients` | Listado paginado de clientes. |
| GET | `/api/clients/{id}` | Detalle de cliente. |
| POST | `/api/clients` | Alta de cliente. |
| PUT | `/api/clients/{id}` | Edicion de cliente. |
| PATCH | `/api/clients/{id}/status` | Cambio de estado. |

## Projects

Controller: `backend/src/Kodvian.Core.Api/Controllers/ProjectsController.cs`

Base route: `/api/projects`

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/projects` | Listado paginado de proyectos. |
| GET | `/api/projects/{id}` | Detalle de proyecto. |
| GET | `/api/projects/lookups` | Datos auxiliares para formularios. `responsibles` devuelve analistas activos con `developerId` remunerable. |
| POST | `/api/projects` | Alta de proyecto. |
| PUT | `/api/projects/{id}` | Edicion de proyecto. |
| GET | `/api/projects/document-types` | Tipos de documento de proyecto. |
| GET | `/api/projects/{id}/documents` | Documentos del proyecto. |
| POST | `/api/projects/{id}/documents` | Alta de documento con PDF. |
| POST | `/api/projects/{id}/documents/{documentId}/versions` | Nueva version de documento. |
| GET | `/api/projects/{id}/documents/{documentId}/versions` | Versiones de un documento. |
| GET | `/api/projects/{id}/documents/{documentId}` | Descarga version vigente. |
| GET | `/api/projects/{id}/documents/{documentId}/versions/{versionId}` | Descarga version especifica. |
| DELETE | `/api/projects/{id}/documents/{documentId}` | Baja logica de documento. |

## Tasks

Controller: `backend/src/Kodvian.Core.Api/Controllers/TasksController.cs`

Base route: `/api/tasks`

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/tasks` | Listado paginado de tareas. |
| GET | `/api/tasks/{id}` | Detalle de tarea. |
| GET | `/api/tasks/kanban` | Tareas agrupadas para kanban. |
| GET | `/api/tasks/lookups` | Datos auxiliares para formularios. |
| POST | `/api/tasks` | Alta de tarea. |
| PUT | `/api/tasks/{id}` | Edicion de tarea. |
| PATCH | `/api/tasks/{id}/status` | Cambio de estado. |

## My Work

Controller: `backend/src/Kodvian.Core.Api/Controllers/MyWorkController.cs`

Base route: `/api/my-work`

Endpoints exclusivos para usuarios con rol `Desarrollador` y claim `developer_id`.

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/my-work/overview` | Resumen de proyectos y tareas asignadas al desarrollador autenticado. |
| GET | `/api/my-work/projects` | Proyectos asociados por contrato o tarea asignada. |
| GET | `/api/my-work/tasks` | Tareas asignadas al desarrollador autenticado. |
| GET | `/api/my-work/tasks/kanban` | Tareas propias agrupadas para tablero kanban. |
| GET | `/api/my-work/tasks/{id}` | Detalle de tarea propia. |
| PATCH | `/api/my-work/tasks/{id}/status` | Cambio de estado de tarea propia. |

## Financial Movements

Controller: `backend/src/Kodvian.Core.Api/Controllers/FinancialMovementsController.cs`

Base route: `/api/financial-movements`

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/financial-movements` | Listado paginado de movimientos. |
| GET | `/api/financial-movements/{id}` | Detalle de movimiento. |
| GET | `/api/financial-movements/monthly-summary` | Resumen mensual financiero. |
| GET | `/api/financial-movements/lookups` | Datos auxiliares de finanzas. |
| POST | `/api/financial-movements` | Alta de movimiento. |
| PUT | `/api/financial-movements/{id}` | Edicion de movimiento. |
| GET | `/api/financial-movements/{id}/receipts` | Comprobantes del movimiento. |
| POST | `/api/financial-movements/{id}/receipts` | Upload de comprobante PDF. |
| GET | `/api/financial-movements/{id}/receipts/{receiptId}` | Descarga de comprobante. |
| DELETE | `/api/financial-movements/{id}/receipts/{receiptId}` | Eliminacion de comprobante. |

## Financial Categories

Controller: `backend/src/Kodvian.Core.Api/Controllers/FinancialCategoriesController.cs`

Base route: `/api/financial-categories`

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/financial-categories` | Lista categorias financieras. |
| POST | `/api/financial-categories` | Alta de categoria. |
| PUT | `/api/financial-categories/{id}` | Edicion de categoria. |

## Providers

Controller: `backend/src/Kodvian.Core.Api/Controllers/ProvidersController.cs`

Base route: `/api/providers`

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/providers` | Lista proveedores. |
| POST | `/api/providers` | Alta de proveedor. |
| PUT | `/api/providers/{id}` | Edicion de proveedor. |

## Developers

Controller: `backend/src/Kodvian.Core.Api/Controllers/DevelopersController.cs`

Base route: `/api/developers`

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/developers` | Lista desarrolladores reales, incluyendo estado de acceso al sistema. Excluye perfiles remunerables de analistas. |
| GET | `/api/developers/{id}/contracts-summary` | Resumen anual de contratos por desarrollador. Solo administrador. |
| POST | `/api/developers` | Alta de desarrollador, opcionalmente con usuario de acceso. |
| PUT | `/api/developers/{id}` | Edicion de desarrollador y configuracion de acceso. |

## Team Users

Controller: `backend/src/Kodvian.Core.Api/Controllers/TeamUsersController.cs`

Base route: `/api/team/users`

Usuarios internos del modulo Equipo. Actualmente expone gestion de analistas y crea/sincroniza su perfil `Developer` remunerable.

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/team/users/analysts` | Lista usuarios con rol Analista, incluyendo `developerId` si tiene perfil remunerable. |
| POST | `/api/team/users/analysts` | Crea usuario Analista con contraseña inicial obligatoria y perfil remunerable asociado. |
| PUT | `/api/team/users/analysts/{id}` | Edita usuario Analista, permite cambiar contraseña y sincroniza/crea el perfil remunerable. |

## Developer Assignments

Controller: `backend/src/Kodvian.Core.Api/Controllers/ProjectDeveloperAssignmentsController.cs`

Base route: `/api`

Asignaciones operativas de equipo a proyecto, sin montos, porcentajes, modalidad de pago ni ledger.

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/projects/{projectId}/developer-assignments` | Equipo operativo asignado al proyecto. |
| POST | `/api/projects/{projectId}/developer-assignments` | Asigna un desarrollador al proyecto sin informacion economica. |
| DELETE | `/api/project-developer-assignments/{id}` | Baja logica de una asignacion operativa. |

## Developer Contracts

Controller: `backend/src/Kodvian.Core.Api/Controllers/ProjectDeveloperContractsController.cs`

Base route: `/api`

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/projects/{projectId}/developer-contracts` | Contratos economicos de desarrolladores del proyecto. Solo administrador. |
| POST | `/api/projects/{projectId}/developer-contracts` | Alta de contrato. |
| PUT | `/api/developer-contracts/{id}` | Edicion de contrato. |
| GET | `/api/developer-contracts/{id}/ledger` | Ledger mensual del contrato. |

## Developer Payments

Controller: `backend/src/Kodvian.Core.Api/Controllers/DeveloperPaymentsController.cs`

Base route: `/api`

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/developer-contracts/{contractId}/payments` | Pagos de un contrato. Solo administrador. |
| POST | `/api/developer-contracts/{contractId}/payments` | Alta de pago. |
| GET | `/api/developer-payments/{paymentId}/receipts` | Comprobantes de pago. |
| POST | `/api/developer-payments/{paymentId}/receipts` | Upload de comprobante PDF. |
| GET | `/api/developer-payments/{paymentId}/receipts/{receiptId}` | Descarga de comprobante. |
| DELETE | `/api/developer-payments/{paymentId}/receipts/{receiptId}` | Eliminacion de comprobante. |

## Locations

Controller: `backend/src/Kodvian.Core.Api/Controllers/LocationsController.cs`

Base route: `/api/locations`

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/locations/countries` | Lista paises. |
| GET | `/api/locations/regions` | Lista regiones por pais. |
| GET | `/api/locations/cities` | Lista ciudades por pais y region. |

## Users

Controller: `backend/src/Kodvian.Core.Api/Controllers/UsersController.cs`

Base route: `/api/users`

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/users` | Listado paginado con `pageNumber`, `pageSize` y `search` por nombre/correo. Solo administradores. |
| GET | `/api/users/roles` | Catálogo de roles admitidos. Solo administradores. |
| PUT | `/api/users/{id}/roles` | Reemplaza los roles con `{ roles, expectedVersion }`. Solo administradores; conserva perfiles y asignaciones e invalida sesiones anteriores. |

Los usuarios devueltos incluyen `roles`, `isActive`, `developerId` y `sessionVersion`. `expectedVersion` se toma del listado para evitar sobrescribir cambios posteriores.

## Situación financiera y monedas

Rutas exclusivas de administradores:

- `GET /api/finance/overview?from=&to=`: histórico/período, configuración e indicadores ARS/USD. Incluye `recordedCashBalance` acumulado hasta el corte, independiente de la configuración inicial. Se consulta desde el modal histórico.
- `GET /api/financial-movements/monthly-summary`: indicadores reducidos por moneda; no carga el análisis histórico. El campo `finance` del dashboard utiliza `FinancePeriodSummaryDto` (from, to, currencies) para el mismo resumen mensual.
- `PUT /api/finance/setup`: punto de partida opcional, saldos por moneda, historial completo y versión esperada.
- `GET/POST /api/finance/partners`, `PUT /api/finance/partners/{id}`: socios.
- `POST /api/finance/exchanges`, `DELETE /api/finance/exchanges/{id}`: cambios de moneda emparejados.
- `GET /api/finance/team-obligations?year=&pageNumber=&pageSize=`: saldos de contratos separados de los egresos pendientes.
- `PUT /api/developer-payments/{id}` y `DELETE /api/developer-payments/{id}?expectedVersion=...`: corrección/anulación coordinada con Finanzas.

Los movimientos agregan moneda, clasificación, financiación, socio y fecha efectiva; su alta usa requestId y su edición expectedVersion.
Los pagos agregan monedas real/aplicada, importe aplicado, requestId y existingMovementId opcional; los históricos requieren vincular un egreso existente.
Los importes escalares antiguos de resúmenes representan solo ARS; usar `currencies` para la información completa.

Detalles: [Finanzas](../modules/finanzas.md).

## Notas de seguridad (aplicables a todas las rutas)

- Los controllers principales usan policies por modulo.
- El rol Desarrollador aislado consume `/api/my-work`; al combinarlo con otros roles obtiene la unión de sus permisos.
- El rol Analista aislado no accede a finanzas, contratos económicos, pagos ni ledger. Sí puede hacerlo una cuenta que también incluya Administrador.
- Para nuevos endpoints sensibles, agregar policy backend aunque el frontend oculte botones.
