# Migraciones Y Seeding

Kodvian Core usa EF Core migrations para evolucionar el schema de PostgreSQL.

## Archivos principales

- `backend/src/Kodvian.Core.Infrastructure/Persistence/KodvianDbContext.cs`.
- `backend/src/Kodvian.Core.Infrastructure/Persistence/DbSeeder.cs`.
- `backend/src/Kodvian.Core.Infrastructure/Migrations/**`.
- `backend/src/Kodvian.Core.Api/Program.cs`.

## Migrations

Las migrations viven en Infrastructure.

`Program.cs` ejecuta:

```csharp
await dbContext.Database.MigrateAsync();
```

Esto aplica migrations pendientes al iniciar la API.

## Seeding

Se ejecutan al iniciar:

- `DbSeeder.SeedAdminUserAsync`.
- `DbSeeder.SeedFinancialCategoriesAsync`.

Roles con IDs fijos estan definidos en `KodvianDbContext`:

- Administrator.
- Operative.
- ReadOnly.
- Analyst.
- Developer.

La migración `20260916205633_UserMultipleRoles` copia `Users.RoleId` a `UserRoles` antes de eliminar la columna original y agrega `SessionVersion`. El seed crea la relación con Administrador. El despliegue requiere un nuevo inicio de sesión para los usuarios existentes. El retroceso se bloquea si una cuenta tiene múltiples roles, evitando perder esas asignaciones.

## Produccion

Fuera de Development, el seed de admin requiere variables seguras:

- `AuthSeed__AdminEmail`.
- `AuthSeed__AdminPassword`.
- `AuthSeed__AdminFullName`.

Tambien requiere JWT productivo valido:

- `Jwt__Issuer`.
- `Jwt__Audience`.
- `Jwt__Key`.

## Cuidados

- Revisar migrations antes de deployar si modifican datos o constraints.
- Evitar cambios destructivos sin plan de migracion.
- Cuidar startups concurrentes si en el futuro hay multiples replicas.
- Mantener seeds idempotentes.
- No usar credenciales de desarrollo en produccion.

## Checklist al agregar entidades

1. Crear o modificar entidad en Domain.
2. Configurar relacion, indices, precision y constraints en `KodvianDbContext`.
3. Crear migration.
4. Revisar SQL generado.
5. Actualizar DTOs, servicios y controllers.
6. Actualizar documentacion de modelo de datos y modulo.
