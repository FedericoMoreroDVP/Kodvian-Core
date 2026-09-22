# Visión financiera

## Propósito y acceso

El módulo `/vision-financiera` permite a los socios entender los números de la empresa
mediante indicadores, gráficos y detalle bajo demanda. Se accede desde el menú lateral,
Inicio o Finanzas. Los administradores siguen entrando a Inicio al iniciar sesión.

La ruta requiere `finances.read`; la API financiera mantiene la autorización exclusiva
para administradores. Ser socio y tener permisos de administrador siguen siendo conceptos distintos.

Sustituye al modal histórico anterior. Los enlaces antiguos `/finanzas?accion=historico`
redirigen al nuevo módulo. Finanzas conserva el registro operativo de movimientos.

## Resumen

- Cuatro indicadores: cobrado, gastado, resultado operativo de caja y neto registrado al corte.
- ARS y USD se seleccionan de forma excluyente. No se suman ni se superponen en una misma escala.
- El resultado pertenece al período; el neto registrado conserva el acumulado desde el inicio hasta el corte.
- Gráfico de barras de cobros/gastos, barras horizontales de las principales cinco categorías y línea del acumulado.
- Los gráficos permiten seleccionar barras/puntos para consultar sus movimientos. Cada gráfico tiene datos tabulares o botones equivalentes utilizables con teclado.
- Los importes abreviados muestran el valor completo en ayudas o detalles. Fechas e importes utilizan formato español argentino.
- Para mantener legibilidad, se agrupa por mes hasta 24 meses, por trimestre hasta 72 y por año para períodos mayores. Se suman flujos, pero se conserva el último saldo de cada grupo; nunca se suman saldos acumulados.
- El detalle de un grupo respeta los extremos exactos del período seleccionado, incluidos meses parciales.

## Socios

Muestra aportes, retiros, gastos reintegrables, reintegros y pendientes por socio, en
la moneda seleccionada. Estos importes son acumulados al corte, independientemente de
la fecha inicial del período. Los perfiles activos sin movimientos se muestran con ceros.

Cada importe abre sus movimientos. Un gasto afrontado como aporte cuenta positivamente
en Aportes, aunque su movimiento sea un egreso. El pendiente de reintegro suma los gastos
reintegrables y resta las devoluciones.

## Compromisos

- Ingresos pendientes y vencidos por cobrar.
- Egresos pendientes y vencidos por pagar.
- Saldos de acuerdos con el equipo, paginados y con año de liquidación explícito.

Los acuerdos no se suman a los egresos pendientes porque pueden representar la misma
obligación. Los porcentajes corresponden al año consultado; los montos fijos abarcan
todo el acuerdo. El detalle conserva el año consultado.

## Configuración y revisión

El menú Configurar abre diálogos específicos para punto de partida, socios y cambios
de moneda. Los formularios conservan datos ante error y bloquean el cierre mientras guardan.
El cambio de moneda conserva su identificador al reintentar.

La franja Historial en revisión resume el estado de carga, las fechas efectivas
provisionales y los pagos históricos sin clasificar/vincular. Sus explicaciones se abren
al solicitarlo. Un saldo inicial desconocido se mantiene como `null`, no se convierte en cero.

## Estado en URL

- `periodo`: historico, mes, anio o personalizado.
- `moneda`: ARS o USD.
- `seccion`: resumen, socios o compromisos.
- `desde` / `hasta`: fechas ISO para un período personalizado.

Los parámetros permiten recargar, compartir o regresar a una selección. Cambiar solo
moneda o sección reutiliza el histórico recibido. Los cambios de período cancelan la
consulta anterior para evitar respuestas fuera de orden. Fechas inválidas se muestran
como error antes de consultar el servidor.

## API y consistencia del detalle

Se reutiliza `GET /api/finance/overview`; el desglose por categorías ahora incluye
`categoryId` y agrupa por identificador, evitando mezclar categorías con igual nombre.

`GET /api/financial-movements` admite `view` y `partnerId`, además de los filtros anteriores.
Al usar `view` se exige una moneda válida. Las vistas son:

| Vista | Contenido |
|---|---|
| OperationalIncome | Ingresos operativos cobrados |
| OperationalExpense | Gastos operativos pagados |
| OperationalResult | Ambos, con gastos negativos en el impacto |
| RecordedCash | Movimientos efectivos financiados por Empresa, con egresos negativos |
| PendingIncome / PendingExpense | Pendientes y vencidos del tipo correspondiente |
| PartnerContributions | Aportes monetarios y gastos como aporte |
| PartnerWithdrawals | Retiros |
| PartnerReimbursableExpenses | Gastos afrontados a reintegrar |
| PartnerReimbursements | Reintegros realizados |
| PartnerOutstanding | Gastos reintegrables positivos y reintegros negativos |

El DTO de listado devuelve `indicatorAmount` para explicar el aporte de cada fila al
indicador. Las vistas efectivas excluyen futuros, anulados e inactivos y filtran por
fecha efectiva; las pendientes usan la fecha del movimiento. El neto y los socios se
consultan sin fecha inicial para conservar el acumulado.

## Implementación

- Componentes y rutas en `frontend/src/app/modules/vision-financiera/`.
- Chart.js 4, con importación diferida de `chart-runtime.ts` y registro selectivo de barras y líneas.
- Los eventos frecuentes y el renderizado de gráficos se ejecutan fuera de la zona Angular; las selecciones vuelven a ella.
- Cada instancia de gráfico se destruye al salir o al cambiar su modelo.
- No se agregan entidades ni migraciones: los cambios del servidor son filtros, proyecciones y contratos de consulta.

## Verificación

- Backend: conciliación entre detalle e indicadores operativos, caja, socios y pendientes; categorías con nombres repetidos; fechas y validación de vistas.
- Frontend: URL, monedas, agrupación de flujos/saldos, configuración, reintentos, permisos de navegación, detalle y gráfico real con selección de barra y destrucción.
- Revisión en Chrome con API simulada, en escritorio 1600×1100 y móvil 390×844: selector de moneda, diálogo de movimientos, Socios, Compromisos y ausencia de desbordamiento horizontal.
