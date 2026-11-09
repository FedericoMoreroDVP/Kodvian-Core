# Configuración segura del proveedor de IA

## Estado actual

La configuración del proveedor es por instalación y solo la administra un usuario con rol Administrador. Se admiten OpenAI y Anthropic (Claude), uno por vez. Paso a paso para conectarlos: [guía de conexión](../guia-conectar-proveedor-ia.md). La clave se protege con ASP.NET Core Data Protection y se guarda cifrada en PostgreSQL; la API nunca la devuelve completa.

El anillo de claves de Data Protection persiste en la tabla `DataProtectionKeys`. En producción se cifra con un certificado PFX cuya clave privada se provisiona como variable runtime del servicio. El certificado y su contraseña no se guardan en la base de datos ni en el repositorio.

Los cambios de configuración, pruebas de conexión y eliminaciones dejan un evento de auditoría con actor, modelo, acción y resultado. No registra la clave ni su contenido cifrado.

## Variables necesarias en Railway Production

- `AiEncryption__CertificateBase64`: archivo PFX codificado en Base64.
- `AiEncryption__CertificatePassword`: contraseña del PFX.

La API falla al iniciar fuera de Development si no están configuradas. Configurarlas en Railway antes de desplegar la migración; se leen en runtime y no deben agregarse al Dockerfile.

Para preparar un PFX, usar un entorno seguro con OpenSSL. Guardar la clave privada y la contraseña fuera del repositorio, convertir el PFX a Base64 y copiar el resultado directamente a Railway. No imprimirlo en logs ni compartirlo por chat. La variable Base64 contiene material privado equivalente al certificado.

## Rotación y recuperación

El certificado protege el anillo de claves que cifra las credenciales del proveedor. Cambiarlo sin migración puede volver ilegibles las claves almacenadas. Antes de rotarlo, preparar una migración que permita descifrar el anillo anterior y re-protegerlo con el nuevo certificado, verificar descifrado de la credencial y respaldar PostgreSQL. No reemplazar estas variables manualmente como una rotación rutinaria hasta que ese procedimiento esté implementado.

Un respaldo de PostgreSQL sin el PFX y su contraseña no permite recuperar las credenciales de IA. El certificado y la contraseña requieren almacenamiento de respaldo seguro fuera de Railway.

## Configuración desde Kodvian Core

Ruta: Administración → Configurar proveedor de IA.

- El Administrador selecciona un identificador de modelo, carga la clave y guarda.
- Al guardar una nueva clave o modelo se invalida la verificación anterior.
- La prueba usa la API Responses con una instrucción fija y `store: false`; puede generar un costo pequeño de API. No envía datos de negocio.
- La configuración y prueba de conexión no habilitan conversaciones: un Administrador debe enviar un acuse explícito con la versión vigente de las condiciones de privacidad/retención y presupuesto. Cambiar clave o modelo invalida esa aprobación; puede revocarse sin quitar la credencial.
- La aprobación también confirma que se configuró un límite monetario en el proyecto API. La versión actual limita solicitudes y tokens por ejecución, pero no impone un límite mensual en USD dentro de Kodvian.
- La clave nunca aparece en respuestas API, logs ni interfaz después de guardarse. Para reemplazarla se introduce una nueva; para quitarla se usa la acción correspondiente.
- El proveedor se valida contra `api.openai.com`; no se aceptan URLs arbitrarias.

La disponibilidad de modelos, permisos y facturación depende de la cuenta API del cliente. El uso de conversaciones y herramientas L0 solo se permite con conexión verificada y aprobación vigente. Ver [Oficina Inteligente](../modules/oficina-inteligente.md) para catálogo, retención y pruebas.
