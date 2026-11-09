# Runbook: certificado de cifrado IA

Este certificado protege el anillo de claves de ASP.NET Core Data Protection en PostgreSQL y, por extensión, las API keys de IA cifradas. El PFX, su Base64 y su password son secretos: no los agregues a archivos, Git, logs ni chats.

## 1. Verificar el PFX antes de cargarlo

En una consola local segura, confirma que el PFX abre con la password y contiene clave privada. Sustituye solo los placeholders.

```powershell
openssl pkcs12 -in .\AiEncryption.pfx -info -noout -passin pass:TU_PASSWORD
```

El comando debe terminar correctamente y mostrar un `Key bag` o `Shrouded Keybag`. No quites `-noout`: evitará imprimir el certificado o la clave.

Alternativa sin OpenSSL, en Windows PowerShell:

```powershell
$pfxPath = (Resolve-Path .\AiEncryption.pfx)
$pfxPassword = 'TU_PASSWORD'
$flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pfxPath, $pfxPassword, $flags)
if (-not $certificate.HasPrivateKey) { throw 'El archivo no contiene una clave privada.' }
'PFX abierto y con clave privada.'
```

Si la password contiene una comilla simple, pídela sin mostrarla:

```powershell
$securePassword = Read-Host 'Password del PFX' -AsSecureString
$pfxPassword = [System.Net.NetworkCredential]::new('', $securePassword).Password
```

Ejecuta después las tres últimas líneas del bloque anterior. Cierra la consola al terminar.

## 2. Generar y comprobar el Base64

Genera el Base64 en memoria y cópialo al portapapeles. `ToBase64String` no inserta saltos de línea ni espacios.

```powershell
$pfxPath = (Resolve-Path .\AiEncryption.pfx)
$base64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($pfxPath))
if ($base64 -match '\s') { throw 'El Base64 contiene espacios o saltos de línea.' }
$base64 | Set-Clipboard
'Base64 copiado al portapapeles.'
```

Antes de pegarlo en Railway, valida que decodifica y que sigue siendo un PFX con clave privada. No imprimas `$base64`.

```powershell
$pfxPassword = 'TU_PASSWORD'
$pfxBytes = [Convert]::FromBase64String($base64)
$flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pfxBytes, $pfxPassword, $flags)
if (-not $certificate.HasPrivateKey) { throw 'El Base64 no representa un PFX con clave privada.' }
'Base64 decodifica a un PFX válido con clave privada.'
```

Mantén el portapapeles y la sesión local protegidos; el Base64 equivale al PFX privado.

## 3. Cargar en Railway Production

1. En Railway, abre el proyecto y el **servicio de la API** de Kodvian Core, no el plugin de PostgreSQL.
2. Selecciona el ambiente **Production** y abre **Variables**.
3. Crea `AiEncryption__CertificateBase64` y pega el Base64 de una sola línea.
4. Crea `AiEncryption__CertificatePassword` y pega la password del PFX.
5. Guarda ambas variables y ejecuta un redeploy del servicio API desde Railway.
6. Comprueba los logs de arranque sin copiar ni exponer los valores de las variables.

Los nombres usan doble guion bajo (`__`) porque .NET los convierte a `AiEncryption:CertificateBase64` y `AiEncryption:CertificatePassword`. Railway almacena el valor literal introducido en su UI: pega passwords con `$`, llaves u otros caracteres especiales tal cual, sin añadir comillas, escapes ni expansión de PowerShell. Si se automatiza fuera de la UI, usa el mecanismo de secretos de esa herramienta y una entrada literal, nunca interpolación de shell.

## 4. Probar Production localmente sin archivos de secretos

En una sola sesión de PowerShell, define variables temporales. Además del certificado, Production exige configuración funcional de base de datos y JWT; usa valores de prueba válidos para un entorno local aislado y no los guardes en `.env` ni en archivos de configuración.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:AiEncryption__CertificateBase64 = $base64
$env:AiEncryption__CertificatePassword = 'TU_PASSWORD'
$env:DATABASE_URL = 'URL_DE_BASE_DE_DATOS_LOCAL_DE_PRUEBA'
$env:Jwt__Issuer = 'issuer-local-prueba'
$env:Jwt__Audience = 'audience-local-prueba'
$env:Jwt__Key = 'CLAVE_DE_PRUEBA_DE_AL_MENOS_32_CARACTERES'
dotnet run --project .\backend\src\Kodvian.Core.Api\Kodvian.Core.Api.csproj
```

Detén la API y elimina las variables de esta sesión al finalizar:

```powershell
Remove-Item Env:ASPNETCORE_ENVIRONMENT, Env:AiEncryption__CertificateBase64, Env:AiEncryption__CertificatePassword, Env:DATABASE_URL, Env:Jwt__Issuer, Env:Jwt__Audience, Env:Jwt__Key -ErrorAction SilentlyContinue
```

Ejecuta las pruebas específicas del arranque desde la raíz del repositorio:

```powershell
dotnet test .\backend\tests\Kodvian.Core.Application.Tests\Kodvian.Core.Application.Tests.csproj --filter FullyQualifiedName~AiEncryptionStartupTests
```

## 5. Interpretar errores de AiEncryption

| Mensaje exacto | Causa | Solución |
|---|---|---|
| `AiEncryption:CertificateBase64 is required outside Development.` | Falta `AiEncryption__CertificateBase64` en un ambiente que no es Development. | Carga el Base64 PFX en una sola línea y redeploya. |
| `AiEncryption:CertificatePassword is required when AiEncryption:CertificateBase64 is configured.` | El Base64 existe, pero falta o está vacía `AiEncryption__CertificatePassword`. | Carga la password correcta y redeploya. |
| `AiEncryption:CertificateBase64 is not valid Base64.` | El valor tiene caracteres inválidos, fue truncado o contiene formato ajeno a Base64. | Regenera con `ToBase64String`, verifica que no tenga espacios/saltos y reemplázalo. |
| `AiEncryption:CertificateBase64 could not be opened with AiEncryption:CertificatePassword.` | La password no corresponde, el PFX está dañado o el Base64 representa otro archivo. | Verifica localmente el PFX y su Base64 con los comandos anteriores; carga el par correcto. |
| `AiEncryption:CertificateBase64 must contain a PFX certificate with a private key.` | Se cargó un certificado público u otro contenido sin clave privada. | Exporta/carga el PFX que incluya clave privada y valida `HasPrivateKey` antes del redeploy. |

Estos son los mensajes actuales de `DependencyInjectionExtensions`. Fuera de Development, el proceso falla antes de completar el registro de servicios si falla cualquiera de estas validaciones.

## 6. Dejar el ambiente listo para el asistente

Desde la raíz, sin modificar ni exponer secretos:

```powershell
dotnet build .\backend\Kodvian.Core.slnx
dotnet test .\backend\tests\Kodvian.Core.Application.Tests\Kodvian.Core.Application.Tests.csproj --filter FullyQualifiedName~AiEncryptionStartupTests
git status --short
```

Comunica al asistente el resultado de build, pruebas y estado de Git. No incluyas valores de variables, PFX, Base64, passwords ni contenido de archivos `.env`.

## 7. Rotar sin perder API keys existentes

La implementación actual solo admite un certificado: `DependencyInjectionExtensions` carga `AiEncryption:CertificateBase64`, abre un `X509Certificate2` y llama a `ProtectKeysWithCertificate(certificate)`. No carga un certificado anterior ni ofrece fallback para descifrar. Por ello, **no reemplaces directamente** las variables en Railway: las claves del anillo ya cifradas con el certificado anterior podrían quedar ilegibles y, con ellas, las API keys de IA.

Plan de rotación:

1. Conserva el PFX anterior y su password en el almacén seguro de respaldo, etiquetados con fecha y vigencia; verifica localmente que ambos abren, sin mostrar su contenido.
2. Realiza una copia de seguridad recuperable de PostgreSQL y verifica el procedimiento de restauración junto con el PFX anterior.
3. Implementa y prueba en un entorno aislado un cambio de Data Protection que pueda descifrar las claves existentes con el certificado anterior y volver a protegerlas con el nuevo. Debe coexistir la capacidad de descifrado anterior durante la transición.
4. Con una copia de producción, valida que una API key cifrada antes de la rotación se lee correctamente después de re-proteger el anillo con el nuevo certificado.
5. Despliega primero ese cambio de compatibilidad; solo después actualiza ambos valores de certificado como una operación coordinada y redeploya.
6. Verifica arranque y la lectura de una configuración IA existente. Conserva el certificado anterior hasta que la validación, el período de reversión y la política de retención concluyan.
7. Documenta la fecha, responsable y ubicación segura de ambos certificados. Revoca y destruye el anterior únicamente cuando ya no existan datos ni respaldos que dependan de él, según la política de la organización.

## Resumen

Este runbook documenta la validación local segura del PFX, generación y comprobación de su Base64, carga manual en Railway Production, prueba local temporal, los cinco errores reales de arranque, preparación de build/pruebas/Git y una rotación que no sustituye el certificado anterior sin compatibilidad de descifrado.
