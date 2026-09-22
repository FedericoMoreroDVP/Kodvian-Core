# Modelo De Datos

## Extensión financiera multimoneda

- `FinanceSettings`: registro único Id=1, fecha de seguimiento, saldos ARS/USD opcionales, confirmación del historial y versión de edición.
- `Partner`: socio identificado por nombre y actividad, independiente de roles de acceso.
- `FinancialMovement`: Currency, Nature, Funding, PartnerId, SettlementDate, SettlementDateEstimated, ExchangeId y Version. Los cambios de moneda tienen dos movimientos enlazados; los importes nunca se suman entre monedas.
- `DeveloperPayment`: Currency, AppliedCurrency, AppliedAmount, FinancialMovementId único, RequestId único y Version. La baja lógica anula también el egreso vinculado.
- `ProjectDeveloperContract.Currency`: moneda de acuerdos de monto fijo; los porcentajes se calculan por moneda de los ingresos. Los datos históricos sin moneda requieren revisión.

Reglas completas y migración: [Finanzas](../modules/finanzas.md).

El modelo de datos vive en `backend/src/Kodvian.Core.Domain` y se configura en `backend/src/Kodvian.Core.Infrastructure/Persistence/KodvianDbContext.cs`.

## BaseEntity

Campos compartidos:

- `Id`.
- `FechaCreacion`.
- `FechaActualizacion`.
- `Activo`.

## Entidades principales

### Client

Representa un cliente comercial.

Relaciones:

- Tiene muchos `Project`.

Campos relevantes:

- `CommercialName` requerido.
- Datos legales/contacto.
- Estado comercial.
- Servicio, monto mensual, dia de facturacion y notas.

### Project

Representa un proyecto para un cliente.

Relaciones:

- Requiere `ClienteId` hacia `Client` con delete restrict.
- Puede tener `ResponsableId` hacia `User` con set null; funcionalmente representa el analista a cargo y debe apuntar a un usuario activo con rol `Analista` y `DeveloperId` asociado.
- Tiene muchas `TaskItem`.
- Tiene muchas `ProjectDeveloperAssignment` operativas.
- Tiene muchos `ProjectDeveloperContract`.
- Tiene documentos versionados con `ProjectDocument`.
- Tiene documentos legacy o comprobantes relacionados via `DocumentFile`.

### TaskItem

Representa una tarea operativa.

Relaciones:

- Requiere `ProyectoId` hacia `Project` con delete restrict.
- Puede tener `DeveloperId` hacia `Developer` con set null.
- Puede tener `ResponsableId` hacia `User` con set null.
- Requiere `CreadoPorId` hacia `User` con delete restrict.

### Developer

Representa un desarrollador externo, colaborador o analista remunerable asociado a proyectos/tareas.

Relaciones:

- Tiene muchos `ProjectDeveloperContract`.
- Tiene muchas `TaskItem`.
- Tiene muchas `ProjectDeveloperAssignment`.
- Puede estar vinculado a un `User` analista para permitir acuerdos y pagos al analista a cargo.
- Los perfiles de analistas se listan también como desarrolladores operativos si su usuario incluye el rol Desarrollador.

### ProjectDeveloperAssignment

Representa asignacion operativa de un desarrollador a un proyecto, sin informacion economica.

Relaciones:

- Requiere `ProjectId` hacia `Project` con delete restrict.
- Requiere `DeveloperId` hacia `Developer` con delete restrict.

Restricciones:

- `(ProjectId, DeveloperId)` es unico.
- No guarda montos, porcentajes, modalidad de pago, pagos ni ledger.

### ProjectDeveloperContract

Representa el acuerdo economico entre un proyecto y un desarrollador.

Relaciones:

- Requiere `ProjectId` hacia `Project` con delete restrict.
- Requiere `DeveloperId` hacia `Developer` con delete restrict.
- Tiene muchos `DeveloperPayment` con cascade desde contrato a pagos.

Restricciones:

- `Percentage` entre 0 y 100 si existe.
- `AgreedAmount` mayor a 0 si existe.

### DeveloperPayment

Representa un pago realizado a un desarrollador por un contrato.

Relaciones:

- Requiere `ContractId`.
- Tiene comprobantes `DocumentFile`.

### FinancialCategory

Representa una categoria financiera para ingresos o egresos.

Relaciones:

- Tiene muchos `FinancialMovement`.

### Provider

Representa un proveedor.

Relaciones:

- Tiene muchos `FinancialMovement`.

### FinancialMovement

Representa un ingreso o egreso.

Relaciones:

- Requiere `CategoryId` hacia `FinancialCategory` con restrict.
- Puede relacionarse con `Client`, `Provider` y `Project` con set null.
- Requiere `CreatedById` hacia `User` con restrict.
- Tiene comprobantes `DocumentFile`.

### DocumentFile

Representa un archivo fisico o remoto persistido por el storage.

Puede pertenecer a exactamente uno de:

- `ProjectId`.
- `FinancialMovementId`.
- `DeveloperPaymentId`.

La constraint `CK_DocumentFiles_Owner` evita archivos sin owner o con multiples owners.

### ProjectDocument

Representa un documento funcional de proyecto con versionado.

Relaciones:

- Requiere `ProjectId`.
- Requiere `CreatedById`.
- Puede tener `DeletedById`.
- Tiene muchas `ProjectDocumentVersion`.

Usa baja logica con `Activo`, `DeletedAt` y `DeletedById`.

### ProjectDocumentVersion

Representa una version de un documento de proyecto.

Relaciones:

- Requiere `ProjectDocumentId`.
- Requiere `DocumentFileId`, unico.
- Requiere `UploadedById`.

Restricciones:

- `(ProjectDocumentId, VersionNumber)` es unico.

### Role y User

`Role` define perfiles de acceso.

`User` representa usuarios autenticables:

- Email unico.
- Password hash requerido.
- Uno o más roles mediante `UserRoles`, con clave compuesta `(UserId, RoleId)`. Solo lectura es exclusivo.
- `SessionVersion` identifica la versión de sesión y se rota al cambiar roles o editar la cuenta.
- Puede vincularse a un `Developer` mediante `DeveloperId` para acceso al portal de trabajo asignado.

Relaciones nuevas:

- `Developer` tiene muchos `User` asociados.
- `User.DeveloperId` usa delete behavior `SetNull`.

## Enums

- `ClientStatus`: `Prospecto`, `Activo`, `Pausado`, `Finalizado`, `Presupuestado`.
- `ProjectStatus`: `Planificacion`, `EnCurso`, `Pausado`, `Finalizado`, `Cancelado`, `Presupuestado`.
- `ProjectPriority`: `Baja`, `Media`, `Alta`, `Urgente`.
- `TaskStatus`: `Pendiente`, `EnCurso`, `Bloqueada`, `Finalizada`, `Cancelada`.
- `TaskPriority`: `Baja`, `Media`, `Alta`, `Urgente`.
- `FinancialMovementType`: `Ingreso`, `Egreso`.
- `FinancialMovementStatus`: `Pendiente`, `Cobrado`, `Pagado`, `Vencido`, `Anulado`.
- `ContractPaymentMode`: `Percentage`, `FixedAmount`.
- `ProjectDocumentType`: `Contract`, `Scope`, `Proposal`, `Deliverable`, `Legal`, `Invoice`, `General`.

## Cuidados

- Revisar delete behavior antes de eliminar entidades relacionadas.
- Mantener precision decimal en campos monetarios.
- Agregar indices cuando se incorporen filtros frecuentes.
- Mantener DTOs separados de entidades para evitar exponer relaciones internas.
