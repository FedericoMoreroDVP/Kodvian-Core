# Guía: conectar OpenAI o Anthropic (Claude) a Oficina Inteligente

Esta guía explica, paso a paso, cómo obtener una clave de API de **OpenAI** o de **Anthropic (Claude)** y cargarla en Kodvian Core para que funcione el chat de Oficina Inteligente.

Kodvian usa **un solo proveedor a la vez** para toda la instalación. Todas las consultas de los usuarios habilitados salen con esa clave y se cobran en esa cuenta.

## Antes de empezar

- Necesitás un usuario con rol **Administrador** en Kodvian Core.
- El servidor tiene que tener configurado el certificado de cifrado (`AiEncryption__CertificateBase64` y `AiEncryption__CertificatePassword`). Sin eso la API no arranca fuera de Development. Ver [Operación con asistente](operacion-con-asistente.md#diagnóstico-de-inicio-aiencryption) y el [runbook del certificado](runbook-certificado-ia.md).
- La clave de API se paga por uso, aparte de cualquier suscripción. **Una suscripción de ChatGPT Plus o de claude.ai (Pro/Max) no incluye clave de API.**

## Opción A: OpenAI

### 1. Crear la cuenta y cargar crédito

1. Entrá a [platform.openai.com](https://platform.openai.com) e iniciá sesión.
2. En **Settings → Billing**, cargá un método de pago y crédito. Sin saldo, la clave existe pero las consultas fallan.

### 2. Poner un límite de gasto

1. En **Settings → Limits** (del proyecto que vas a usar), configurá un límite mensual.
2. Kodvian todavía no tiene un tope de gasto propio, así que este límite es la protección real contra consumos inesperados.

### 3. Crear la clave

1. En **API keys**, elegí **Create new secret key** dentro del proyecto.
2. Copiala en el momento: empieza con `sk-` y OpenAI no la vuelve a mostrar.
3. Guardala solo en un gestor de contraseñas. Nunca en el repositorio, en un `.env` compartido ni en un chat.

### 4. Elegir el modelo

Usá el identificador exacto de un modelo habilitado para ese proyecto (se ve en **Settings → Limits** o en la documentación de modelos de OpenAI).

## Opción B: Anthropic (Claude)

### 1. Crear la cuenta en la Console y cargar crédito

1. Entrá a [console.anthropic.com](https://console.anthropic.com). Es una cuenta distinta de claude.ai, aunque puede usar el mismo correo.
2. En **Settings → Billing** (o **Plans & Billing**), cargá un método de pago y crédito. Sin saldo, la prueba de conexión puede pasar pero las consultas fallan.

### 2. Poner un límite de gasto

1. En **Settings → Limits**, configurá un límite de gasto mensual para la organización o para un workspace dedicado a Kodvian.
2. Recomendado: crear un workspace "Kodvian Core" para separar el consumo y la clave de otros usos.

### 3. Crear la clave

1. En **Settings → API Keys**, elegí **Create Key** (dentro del workspace elegido).
2. Copiala en el momento: empieza con `sk-ant-` y la Console no la vuelve a mostrar.
3. Guardala solo en un gestor de contraseñas.

### 4. Elegir el modelo

Se escribe el identificador exacto, sin fecha al final:

| Modelo | Identificador | Cuándo usarlo |
|---|---|---|
| Claude Opus 5.5 | `claude-opus-5-5` | Recomendado: mejor calidad de respuesta. |
| Claude Sonnet 5.5 | `claude-sonnet-5-5` | Más económico, muy bueno para consultas operativas. |
| Claude Haiku 5.5 | `claude-haiku-5-5` | El más económico y rápido, para consultas simples. |

Los precios vigentes están en la página de precios de Anthropic.

## Cargar la clave en Kodvian Core

1. Iniciá sesión como Administrador y andá a **Administración → Configurar proveedor de IA**.
2. En **Proveedor**, elegí **OpenAI** o **Anthropic (Claude)**. Para Anthropic el modelo se completa con `claude-opus-5-5`; podés cambiarlo.
3. Pegá la **clave API** y tocá **Guardar configuración**. La clave se guarda cifrada y nunca se vuelve a mostrar; solo queda visible el final (por ejemplo `••••a1b2`).
4. Tocá **Probar conexión**.
   - Con OpenAI se envía una solicitud mínima, que puede generar un costo muy pequeño.
   - Con Anthropic se consulta el modelo en la API, sin generar texto ni costo.
5. Si la prueba dice "La conexión con el proveedor y el modelo funciona", revisá las condiciones de privacidad y retención del proveedor, tildá la confirmación y tocá **Aprobar uso de datos reales**.
6. Andá a **Oficina Inteligente** y hacé una consulta, por ejemplo "¿Qué tareas vencen en los próximos siete días?".

Sin la aprobación del paso 5, el chat queda bloqueado. La aprobación queda atada al proveedor y al modelo.

## Cambiar de proveedor o de modelo

- **Cambiar de proveedor** (por ejemplo de OpenAI a Anthropic) exige cargar la clave del nuevo proveedor: la clave guardada pertenece al anterior y Kodvian no la reutiliza.
- Cambiar de proveedor, de modelo o de clave **invalida la verificación y la aprobación**: hay que volver a probar la conexión y aprobar el uso de datos reales.
- Las conversaciones anteriores se conservan; las nuevas consultas usan el proveedor actual.

## Rotar o revocar una clave

1. Creá una clave nueva en la consola del proveedor.
2. Cargala en Kodvian (campo **Nueva clave API**), guardá, probá la conexión y volvé a aprobar.
3. Recién entonces revocá la clave anterior en la consola del proveedor.

Para cortar el acceso de inmediato: **Revocar aprobación** en Kodvian, o **Quitar proveedor**, y revocar la clave en la consola del proveedor.

## Problemas comunes

| Mensaje en Kodvian | Causa probable | Qué hacer |
|---|---|---|
| "El proveedor rechazó la clave…" | Clave mal copiada, revocada, o de otro proveedor (por ejemplo una clave `sk-ant-` con OpenAI elegido). | Verificá el proveedor elegido y volvé a pegar la clave sin espacios. |
| "La clave es válida, pero el modelo indicado no existe…" | Identificador de modelo mal escrito o sin acceso para esa cuenta. | Corregí el identificador (sin fecha al final en Claude). |
| "Se alcanzó el límite de uso…" | Límite de solicitudes del proveedor o límite de gasto alcanzado. | Esperá unos minutos o revisá los límites en la consola del proveedor. |
| "El proveedor no pudo completar la consulta…" | Muchas veces es **falta de saldo**. | Cargá crédito en Billing y reintentá. |
| "Claude no respondió esta consulta por sus políticas de uso…" | El modelo declinó esa consulta puntual. | Reformulá la pregunta. |
| "Ingresa la clave API del nuevo proveedor…" | Cambiaste de proveedor sin cargar clave. | Pegá la clave del proveedor nuevo. |
| "Se alcanzó el límite temporal de consultas…" | Límite propio de Kodvian: 10 consultas por minuto por instalación. | Esperá un minuto. |
| La API no arranca y el log menciona `AiEncryption` | Falta o está mal el certificado de cifrado. | Ver [Diagnóstico de inicio: AiEncryption](operacion-con-asistente.md#diagnóstico-de-inicio-aiencryption). |

## Qué se envía al proveedor

Solo la pregunta del usuario, el historial reciente de esa conversación y los datos mínimos que devuelven las herramientas de solo lectura (ver [Oficina Inteligente](modules/oficina-inteligente.md)). No se envían contactos, CUIT, montos mensuales de clientes, notas, enlaces de reuniones ni archivos.
