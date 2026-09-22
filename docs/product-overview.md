# Product Overview

Kodvian Core es un panel interno para administrar operaciones basicas de una empresa de software.

La aplicacion busca centralizar informacion operativa que normalmente queda distribuida entre planillas, chats y herramientas aisladas: clientes, proyectos, tareas, desarrolladores externos, contratos, pagos, documentos y movimientos financieros.

## Usuarios previstos

- Administrador: opera todos los modulos, usuarios, finanzas, proyectos, tareas, documentos y configuraciones.
- Operativo: gestiona clientes, proyectos, tareas y documentacion operativa; puede consultar finanzas segun permisos actuales.
- Solo lectura: consulta informacion sin ejecutar acciones de escritura.

## Alcance funcional actual

- Dashboard operativo con KPIs, tareas prioritarias, cobranzas proximas y movimientos recientes.
- Gestion de clientes y estados comerciales.
- Gestion de proyectos, analistas a cargo, estado, prioridad, avance y presupuesto.
- Gestion de tareas con listado y vista kanban.
- Visión financiera interactiva para socios: indicadores, gráficos, aportes y compromisos por moneda, con detalle bajo demanda.
- Gestion de desarrolladores externos.
- Contratos de desarrolladores por proyecto, pagos, comprobantes y ledger mensual.
- Gestion financiera de ingresos, egresos, categorias, proveedores, comprobantes y resumen mensual.
- Administración de usuarios con listado, búsqueda, paginación y asignación de múltiples roles, exclusiva para administradores.
- Login, sesion autenticada y control de acceso por permisos.

## Principios del producto

- Priorizar operaciones internas sobre presentacion comercial.
- Mantener informacion suficiente en pantalla para trabajo diario.
- Evitar duplicar datos entre modulos cuando exista una relacion clara.
- Preservar trazabilidad entre cliente, proyecto, tarea, contrato, pago, comprobante y movimiento financiero.
- Usar texto visible en espanol latino.
- Mantener una experiencia compacta, clara y consistente.

## Stack principal

- Backend: .NET 8, ASP.NET Core Web API, EF Core, PostgreSQL.
- Frontend: Angular 19 standalone, Angular Material, SCSS.
- Deploy: Docker multi-stage en un solo servicio; la API sirve el SPA Angular.
- Storage: local en desarrollo, S3-compatible en produccion.

## Limites actuales

- No hay portal publico ni portal externo de clientes documentado.
- La creación y edición de datos de personas se realiza en Equipo; Administración permite modificar los roles de las cuentas existentes.
- La autorizacion fina no esta aplicada de forma uniforme en todos los controllers.
- La documentacion de Railway cubre deploy, pero no reemplaza validaciones funcionales post-release.
