# Administración — Usuarios

## Funcionalidad

Ruta `/administracion`, exclusiva para usuarios que incluyan el rol **Administrador**.
El listado contiene nombre, correo, estado de acceso, roles y la acción **Modificar roles**.
Incluye búsqueda por nombre/correo y paginación de 10, 20 o 50 filas.

El diálogo permite seleccionar varios roles. Ejemplos: Administrador + Desarrollador,
Administrador + Analista, Analista + Desarrollador y los tres combinados.
Operativo sigue disponible. Solo lectura es exclusivo y no se combina con otro rol.
Se exige al menos un rol válido y activo. Cancelar no modifica la cuenta; un error conserva la selección.

## API

Todos los endpoints requieren `AdministratorOnly`:

- `GET /api/users?pageNumber=1&pageSize=20&search=texto`: listado real paginado.
- `GET /api/users/roles`: catálogo de roles admitidos.
- `PUT /api/users/{id}/roles`: `{ "roles": ["Administrador", "Desarrollador"], "expectedVersion": "UUID" }`.

Cada usuario del listado devuelve `id`, `fullName`, `email`, `isActive`, `roles`,
`developerId` y `sessionVersion`. La versión permite detectar un formulario desactualizado.
Los cambios de roles se efectúan únicamente mediante esta operación administrativa.
Las altas de Equipo continúan asignando su rol operativo inicial, sin aceptar roles arbitrarios del cliente.

## Datos y permisos

- `UserRoles` relaciona usuarios y roles mediante clave compuesta `(UserId, RoleId)`.
- Los permisos efectivos son la unión sin duplicados de los permisos de los roles activos.
- `User.DeveloperId` conserva la identidad operativa/remunerable; cambiar los roles no cambia ese identificador.
- Al agregar Analista o Desarrollador a una cuenta sin perfil, se crea uno o se vincula un perfil externo con el mismo correo si es único y no está vinculado a otra cuenta. Se rechazan coincidencias ambiguas.
- Una persona con Analista + Desarrollador aparece en ambas categorías de Equipo, utilizando la misma cuenta y perfil.
- Retirar un rol conserva tareas, evidencias, asignaciones y contratos. Los selectores de nuevas asignaciones usan los roles actuales; las ediciones de asignaciones existentes pueden conservar al responsable anterior.

## Protección de cuentas

- Solo un administrador puede modificar una cuenta administradora desde los formularios de Equipo (incluidos contraseña, correo y actividad).
- La edición normal conserva los roles; editar un desarrollador no vuelve a asignarle un rol único.
- Las ediciones de perfiles vinculados a varias cuentas se rechazan para evitar modificar una cuenta equivocada.
- Debe permanecer al menos un administrador activo. Se comprueba al retirar roles y al desactivar acceso desde Equipo.
- Todas las mutaciones de cuentas/roles adquieren el mismo bloqueo transaccional de PostgreSQL mediante `pg_advisory_xact_lock`. La identidad, versión de sesión y autorización del actor se vuelven a validar después de adquirirlo.
- La transacción usa el aislamiento predeterminado Read Committed. Una segunda operación concurrente ve el resultado de la primera antes de decidir si puede continuar.

## Sesiones y navegación

- Los tokens incluyen todos los roles y el claim `session_version`.
- Cada petición autenticada comprueba en la base de datos que la cuenta esté activa, tenga roles activos y conserve esa versión.
- Cambiar roles rota la versión y revoca las sesiones anteriores en su siguiente petición. Las ediciones de cuentas desde Equipo también rotan la versión.
- Si el administrador cambia sus propios roles, la interfaz limpia la sesión y vuelve al ingreso.
- Los administradores entran a Inicio. Si también tienen Desarrollador, mantienen Mi trabajo en el menú.
- El menú y el guard excluyen Administración para usuarios no administradores, incluso si conservan el permiso histórico `administration.read`.

## Migración y despliegue

`20260916205633_UserMultipleRoles` crea la tabla puente, copia el `RoleId` de cada
usuario existente, genera la versión de sesión y después elimina la columna de rol único.
El seed del administrador utiliza `UserRoles`. Las sesiones emitidas antes de esta actualización
no contienen la versión y requieren un nuevo inicio de sesión.

El retroceso de la migración exige exactamente un rol por usuario; se rechaza si pudiera perder combinaciones.
La migración fue generada y su SQL revisado; no se aplicó a una base real durante la implementación.

## Pruebas

- `UserRolesTests`: combinaciones, exclusividad de Solo lectura, cambio de función, conservación del perfil y asignaciones, protección administrativa, último administrador, edición de Equipo, sesiones, acceso a proyectos y listado paginado.
- `UserRolesApiTests`: peticiones HTTP con JWT real contra TestServer para comprobar 401/403/200 y rechazo de un token emitido antes del cambio de roles. Persistencia EF InMemory.
- `RoleQueryTranslationTests`: traducción de las consultas de Equipo a PostgreSQL con Npgsql, sin ejecutar contra una base.
- Frontend: selección múltiple, errores recuperables, búsqueda/paginación, cierre de sesión propia, menú y guard administrativo.

La migración y la serialización de cambios concurrentes deben comprobarse además en un entorno PostgreSQL de prueba; los tests InMemory no verifican bloqueos reales.
