# Proyectos

## Resumen funcional

El modulo de proyectos centraliza la gestion de trabajos para clientes: estado, prioridad, analista a cargo, avance, documentos, equipo asignado y, solo para administradores, contratos y pagos asociados.

## Flujos principales

- Listar proyectos.
- Crear y editar proyecto.
- Asignar opcionalmente un analista a cargo del proyecto desde el formulario o desde el dialog Equipo del proyecto.
- Ver detalle.
- Pegar, guardar, modificar, abrir o quitar un enlace de Google Drive para la documentación del proyecto.
- Asignar desarrolladores operativamente sin datos economicos.
- Registrar pagos a desarrolladores solo como administrador.
- Consultar ledger de contrato solo como administrador.

## Pantallas frontend

- Ruta: `/proyectos`.
- Page: `frontend/src/app/modules/proyectos/proyectos-page.component.ts|html|scss`.
- Service: `frontend/src/app/modules/proyectos/services/proyectos.service.ts`.
- Models: `frontend/src/app/modules/proyectos/models/proyectos.models.ts`.
- Dialogs: `proyecto-form-dialog`, `proyecto-detail-dialog`, `proyecto-developers-dialog`, `contrato-desarrollador-form-dialog`, `pago-desarrollador-form-dialog`, `contrato-ledger-dialog`.

## Contratos backend

Proyectos:

- `GET /api/projects`.
- `GET /api/projects/{id}`.
- `GET /api/projects/lookups`.
- `POST /api/projects`.
- `PUT /api/projects/{id}`.

Documentación mediante Google Drive:

- `GET /api/projects/{id}/drive-link`: obtiene `{ googleDriveFolderUrl }`; devuelve `null` en ese campo si no hay enlace y 404 si no existe el proyecto.
- `PUT /api/projects/{id}/drive-link`: guarda `{ googleDriveFolderUrl: "https://drive.google.com/..." }`. Enviar `null` o texto vacío quita el enlace.
- Los endpoints mantienen el envoltorio `success`, `message`, `data`.

API anterior de archivos (ya no utilizada por la pantalla de detalle):

- `GET /api/projects/document-types`.
- `GET /api/projects/{id}/documents`.
- `POST /api/projects/{id}/documents`.
- `POST /api/projects/{id}/documents/{documentId}/versions`.
- `GET /api/projects/{id}/documents/{documentId}/versions`.
- `GET /api/projects/{id}/documents/{documentId}`.
- `GET /api/projects/{id}/documents/{documentId}/versions/{versionId}`.
- `DELETE /api/projects/{id}/documents/{documentId}`.

Contratos y pagos:

- Solo administrador.
- Acuerdos fijos con moneda explícita; porcentajes sobre ingresos operativos registrados, por separado en ARS y USD. Un porcentaje sin base de ingresos genera cero obligación.
- Pagos con moneda real y moneda/importe cancelados. Los nuevos pagos crean o vinculan un egreso; los históricos se revisan y vinculan sin generar egresos duplicados.
- Edición y anulación de pagos sincronizadas con Finanzas. El detalle y los resúmenes del equipo muestran importes por moneda y avisos de revisión. Ver [Finanzas](finanzas.md).
- Al cancelar un proyecto se desactivan automáticamente sus acuerdos activos, sin anular pagos históricos por sí solo. Desde Equipo, un administrador puede **Eliminar acuerdo**: lo quita del listado, conserva su marca de eliminación para auditoría y anula de forma coordinada sus pagos y egresos vinculados. No se pueden crear ni modificar acuerdos, pagos o movimientos financieros asociados a proyectos cancelados.

- `GET /api/projects/{projectId}/developer-contracts`.
- `POST /api/projects/{projectId}/developer-contracts`.
- `PUT /api/developer-contracts/{id}`.
- `DELETE /api/developer-contracts/{id}`: solo para un proyecto cancelado; anula el acuerdo, sus pagos activos y los egresos vinculados.
- `GET /api/developer-contracts/{id}/ledger`.
- `GET /api/developer-contracts/{contractId}/payments`.
- `POST /api/developer-contracts/{contractId}/payments`.
- `PUT /api/developer-payments/{id}`.
- `DELETE /api/developer-payments/{id}?expectedVersion=...`.
- Comprobantes bajo `/api/developer-payments/{paymentId}/receipts`.

## Modelo de datos

- `Project`.
- `Project.GoogleDriveFolderUrl`: texto opcional de hasta 2048 caracteres; migración `20260916202919_ProjectGoogleDriveLink`.
- `Client`.
- `TaskItem`.
- `ProjectDocument`.
- `ProjectDocumentVersion`.
- `DocumentFile`.
- `Developer`.
- `ProjectDeveloperAssignment`.
- `ProjectDeveloperContract`.
- `DeveloperPayment`.

Enums:

- `ProjectStatus`.
- `ProjectPriority`.
- `ProjectDocumentType`.
- `ContractPaymentMode`.

## Permisos

- `projects.read` y `projects.write` para proyectos.
- `projects.documents.read` para consultar el enlace y `projects.documents.write` para guardarlo, modificarlo o quitarlo; ambos endpoints requieren también `projects.read`.
- La operación de escritura requiere lectura de documentación. La URL se consulta por un endpoint independiente y no se expone en el detalle genérico del proyecto.
- `projects.documents.delete` permanece asociado a la API anterior de archivos.
- `ProjectDeveloperAssignment` usa permisos operativos de proyectos, sin datos economicos.
- Contratos economicos, pagos y ledger son solo administrador.
- El frontend evalua permisos de documentos en el detalle.
- El backend tiene policies especificas para documentos de proyecto.

## Estados de UI

- Listado con filtros/paginacion.
- Dialog de detalle.
- Estados del enlace: cargando, sin enlace, enlace guardado, guardando y error con reintento.
- Los usuarios de consulta ven el botón Abrir en Google Drive; el formulario y Quitar enlace requieren escritura.
- Estados de contratos/pagos/comprobantes.
- Errores de upload/download.

## Reglas de negocio

- Todo proyecto pertenece a un cliente.
- El analista a cargo usa `Project.ResponsableId` y solo puede apuntar a un usuario activo con rol `Analista` y perfil `Developer` asociado.
- El perfil `Developer` del analista permite crear acuerdos economicos y pagos con `ProjectDeveloperContract` y `DeveloperPayment`.
- El porcentaje de avance debe ser coherente con estado y tareas.
- Un proyecto puede guardar un único enlace opcional de Google Drive, pegado manualmente.
- Solo se aceptan direcciones HTTPS del dominio exacto `drive.google.com`, con una ruta, sin credenciales ni puertos alternativos. Se quitan espacios externos y se conservan los parámetros compartidos, incluidos `resourcekey` y `usp`.
- La validación del formato se ejecuta en frontend y backend; no comprueba la existencia o los permisos de la carpeta en Google Drive.
- Abrir el enlace utiliza una pestaña nueva con `noopener noreferrer`. Los archivos y permisos se administran directamente en Drive.
- Guardar el enlace modifica únicamente ese campo y la fecha de actualización; la edición general del proyecto conserva el enlace.
- Quitar el enlace solo elimina su asociación con el proyecto, sin operar sobre Google Drive.
- El detalle ya no muestra subida de archivos, tipos, notas ni historial de versiones. No se realiza transferencia automática de archivos.
- La asignacion operativa de equipo no contiene montos, porcentajes ni modalidad de pago.
- Los contratos pueden ser por porcentaje o monto fijo.
- Los pagos deben asociarse a un contrato.

## Riesgos y cuidados

- La API anterior de documentos conserva su esquema y comportamiento; la nueva migración solo agrega la columna del enlace, sin borrar tablas ni archivos.
- Validar permisos backend para acciones sensibles.
- Evitar inconsistencias entre presupuesto de proyecto, contratos y pagos.

## Tests requeridos

Pruebas implementadas para el enlace de Drive:

- `ProjectDriveLinkTests`: validación de URL, parámetros compartidos, guardado, lectura, sustitución, eliminación, proyecto inexistente y conservación del enlace al editar el proyecto. Persistencia comprobada con EF InMemory.
- `proyecto-detail-dialog.component.spec.ts`: consumo de GET/PUT, apertura en pestaña nueva, permisos visibles, errores recuperables, estado vacío y validación de URL.

Cobertura general del módulo:

- Backend: tests de `ProjectService` para listado, detalle, lookups, alta y edicion.
- Backend: tests de documentos para alta, versionado, descarga, listado y baja logica.
- Backend: tests de permisos para `projects.documents.read/write/delete` con acceso permitido y denegado.
- Backend: tests de contratos para porcentaje, monto fijo, constraints, edicion y ledger.
- Backend: tests de pagos y comprobantes para alta, upload, descarga y eliminacion.
- Backend: integration tests de endpoints principales de proyectos, documentos, contratos y pagos.
- Frontend: specs de `ProyectosService` para endpoints, FormData, downloads y params.
- Frontend: specs de pagina para filtros, paginacion, loading, empty, error y alta/edicion.
- Frontend: specs de dialogs de proyecto, detalle, documentos, contratos, pagos y ledger.
- Frontend: specs de permisos visibles para acciones de documentos.

## Mejoras futuras

- Vista de timeline de proyecto.
- Historial de estado/avance.
- Documentos con preview controlado.
- Alertas por pagos pendientes o presupuesto excedido.
