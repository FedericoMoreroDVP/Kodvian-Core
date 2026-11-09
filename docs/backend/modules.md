# Modulos Backend

Este documento mapea cada modulo funcional con sus archivos backend principales.

## Auth

- Controller: `backend/src/Kodvian.Core.Api/Controllers/AuthController.cs`.
- Application: `backend/src/Kodvian.Core.Application/Auth/**`.
- Security: `backend/src/Kodvian.Core.Application/Common/Security/**`.
- Services: `AuthService`, `TokenService`, `PasswordHasherService`.

## Dashboard

- Controller: `DashboardController.cs`.
- Abstraction: `IDashboardService.cs`.
- DTOs: `backend/src/Kodvian.Core.Application/Dashboard/**`.
- Service: `DashboardService.cs`.

## Clientes

- Controller: `ClientsController.cs`.
- Abstraction: `IClientService.cs`.
- DTOs/requests: `backend/src/Kodvian.Core.Application/Clients/**`.
- Service: `ClientService.cs`.
- Entity: `Client.cs`.
- Enum: `ClientStatus.cs`.

## Proyectos y documentos

- Controller: `ProjectsController.cs`.
- Abstraction: `IProjectService.cs`.
- DTOs/requests: `backend/src/Kodvian.Core.Application/Projects/**`.
- Service: `ProjectService.cs`.
- Entities: `Project`, `ProjectDocument`, `ProjectDocumentVersion`, `DocumentFile`.
- Enums: `ProjectStatus`, `ProjectPriority`, `ProjectDocumentType`.
- Storage: `IFileStorageService`, `LocalFileStorageService`, `S3FileStorageService`.

## Tareas

- Controller: `TasksController.cs`.
- Abstraction: `ITaskService.cs`.
- DTOs/requests: `backend/src/Kodvian.Core.Application/Tasks/**`.
- Service: `TaskService.cs`.
- Entity: `TaskItem.cs`.
- Enums: `TaskStatus.cs`, `TaskPriority.cs`.

## Finanzas

- Controllers: `FinancialMovementsController.cs`, `FinancialCategoriesController.cs`, `ProvidersController.cs`.
- Abstractions: `IFinancialMovementService.cs`, `IFinancialCategoryService.cs`, `IProviderService.cs`.
- DTOs/requests: `backend/src/Kodvian.Core.Application/Finances/**`.
- Services: `FinancialMovementService.cs`, `FinancialCategoryService.cs`, `ProviderService.cs`.
- Entities: `FinancialMovement`, `FinancialCategory`, `Provider`, `DocumentFile`.
- Enums: `FinancialMovementType`, `FinancialMovementStatus`.

## Desarrolladores, contratos y pagos

- Controllers: `DevelopersController.cs`, `ProjectDeveloperContractsController.cs`, `DeveloperPaymentsController.cs`.
- Abstractions: `IDeveloperService.cs`, `IProjectDeveloperContractService.cs`, `IDeveloperPaymentService.cs`.
- DTOs/requests: `backend/src/Kodvian.Core.Application/Developers/**`.
- Services: `DeveloperService.cs`, `ProjectDeveloperContractService.cs`, `DeveloperPaymentService.cs`.
- Entities: `Developer`, `ProjectDeveloperContract`, `DeveloperPayment`, `DocumentFile`.
- Enum: `ContractPaymentMode.cs`.

## Locations

- Controller: `LocationsController.cs`.
- Abstraction: `ILocationService.cs`.
- DTOs: `backend/src/Kodvian.Core.Application/Locations/**`.
- Service: `LocationService.cs`.

## Administracion

- Controller: `UsersController.cs`.
- DTO: `UserListItemDto.cs`.
- Security: `RoleNames`, `PermissionCodes`, `RolePermissionMap`.
- Entities: `User`, `Role`.

## Oficina Inteligente

- Controllers: `AiProviderConfigurationController`, `AiConversationsController`.
- Application: `backend/src/Kodvian.Core.Application/Ai/**`.
- Infrastructure: `AiProviderConfigurationService`, `OpenAiResponsesClient`, `AiConversationService`, `AiToolCatalog`, `AiAuditRetentionWorker`.
- Persistence: `AiProviderConfiguration`, `AiConversation`, `AiMessage`, `AiRun`, `AiAuditEvent`.
- El proveedor requiere clave cifrada, conexión verificada y aprobación vigente del Administrador para datos reales. Tools L0 revalidan permisos y sesión.

## Regla de mantenimiento

Cuando se agregue un modulo:

1. Crear entidad y enum si aplica.
2. Crear DTOs/requests en Application.
3. Crear interface de servicio.
4. Implementar servicio en Infrastructure.
5. Registrar DI.
6. Exponer controller.
7. Crear migration.
8. Actualizar documentacion backend, frontend y modulo funcional.
