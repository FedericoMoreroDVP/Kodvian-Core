# Oficina Inteligente

## Alcance del piloto

La primera fase ofrece conversaciones privadas para Administradores y consultas L0 de solo lectura. Las herramientas disponibles se filtran por permiso de dominio; la IA no tiene acceso directo a SQL, archivos ni red arbitraria. Las escrituras no están expuestas.

## Configuración y aprobación de privacidad

Desde Administración → Configurar proveedor de IA, un Administrador elige el proveedor (OpenAI o Anthropic), el modelo y la clave API. La clave se cifra en backend y nunca se devuelve al frontend. Con OpenAI la prueba de conexión envía una solicitud mínima con `store: false` y puede generar un costo pequeño; con Anthropic consulta el modelo en la Models API, sin costo. Paso a paso: [guía de conexión](../guia-conectar-proveedor-ia.md).

Antes de habilitar consultas con datos reales, el Administrador debe probar la conexión, revisar las condiciones de privacidad y retención del proveedor y configurar un límite de gasto en el proyecto API. La aprobación queda ligada al proveedor/modelo y se invalida al cambiar la clave o el modelo; también puede revocarse en cualquier momento. Sin aprobación vigente, las consultas quedan bloqueadas.

`store: false` evita almacenar el estado de la respuesta por esa llamada, pero no equivale a Zero Data Retention ni modifica los controles de seguridad propios del proveedor. En el piloto se deben usar datos sintéticos hasta revisar y aprobar las condiciones contractuales aplicables.

## Herramientas L0

- `dashboard.get_snapshot`: KPIs operativos y colección financiera por moneda.
- `finance.get_summary`: ingresos, egresos y pendientes en totales separados por moneda.
- `projects.list`: estado, prioridad, fechas, avance y cliente.
- `tasks.list`: estado, prioridad, vencimiento y proyecto; sin descripción ni horas.
- `clients.list`: nombre comercial y estado; sin contacto, CUIT, monto mensual ni notas.
- `meetings.list`: título, proyecto, horario, estado y cantidad de participantes; sin enlaces, ubicaciones ni nombres de participantes.

Las listas usan un máximo de 25 registros. Los períodos predeterminados son 30 días y el máximo es 90, en la zona `America/Argentina/Buenos_Aires`. Las respuestas muestran período, fuente, corte y enlace interno cuando corresponde.

## Historial y auditoría

- Las conversaciones y mensajes permanecen hasta que su propietario los elimina.
- El borrado elimina conversación, mensajes y ejecuciones asociadas.
- La auditoría conserva solo actor, herramienta, decisión, resultado técnico, identificadores de correlación y fecha durante 90 días. No almacena pregunta, respuesta ni payloads de herramientas.
- La aplicación purga auditoría vencida diariamente.
- Las solicitudes tienen un límite de 10 por minuto por instalación; cada ejecución permite hasta 12 invocaciones L0 y limita los tokens de salida por respuesta.

## API

Las rutas `/api/ai/conversations...` requieren `ai.office.use` y rol Administrador. Cada consulta vuelve a comprobar la vigencia de sesión, las políticas de dominio y el catálogo allowlist. El backend limita a 10 solicitudes de mensaje por minuto por instalación y a 12 llamadas de herramienta por ejecución. El límite mensual monetario debe configurarse en el proyecto API del proveedor; todavía no hay una cuota monetaria aplicada por Kodvian.

## Verificación

- Backend: pruebas de cifrado de claves, aprobación de privacidad, propiedad de conversación, idempotencia de solicitud, minimización de campos y permisos.
- Frontend: pruebas de uso de historial, fuentes y renderizado seguro como texto.
- Configuración operativa de Railway: [protección del proveedor IA](../backend/ai-provider-configuration.md).
