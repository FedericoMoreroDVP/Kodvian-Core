# Finanzas — situación de la empresa

## Uso

Las pantallas Inicio y `/finanzas` muestran un resumen mensual compacto: ingresos
cobrados, gastos pagados y resultado del mes, con selector ARS/USD. Finanzas prioriza
el listado de movimientos; clasificación, categoría, cliente, proveedor y base de
fechas están disponibles en **Más filtros**.

La columna **Socio / responsable**, junto a la descripción, muestra el socio asociado
al movimiento. Usa el nombre actual de Equipo o de la cuenta vinculada, o el nombre
de la ficha manual; conserva la referencia aunque el socio esté inactivo. Cuando no
hay socio asignado muestra **—**. No representa al usuario que cargó el registro.

El botón **Visión financiera** abre el módulo `/vision-financiera`, con Resumen,
Socios y Compromisos. Allí se consultan el histórico y los gráficos, y se accede a
la configuración del punto de partida. Reemplaza al modal histórico anterior.
Los enlaces antiguos `/finanzas?accion=historico` redirigen al nuevo módulo.

Los indicadores diarios usan una consulta reducida compartida que no calcula ni
devuelve socios, categorías, saldos iniciales o evolución histórica. La consulta
completa se realiza al entrar a Visión financiera. Los indicadores y gráficos abren
diálogos de movimientos paginados, preservando período, moneda y sección.

1. Abrir **Visión financiera** y registrar los socios desde **Configurar → Gestionar socios**; su condición es independiente del rol de usuario.
2. Cargar los gastos anteriores con moneda, fecha original y fecha efectiva de pago.
3. Revisar fechas históricas y las monedas de contratos/pagos.
4. Vincular pagos históricos con los egresos ya cargados desde Equipo del proyecto → Pagos → Revisar y vincular.
5. Completar **Punto de partida** cuando se conozcan la fecha y los saldos iniciales.

Los campos desconocidos se dejan vacíos: `null` no equivale a saldo cero. El historial
se presenta como provisional mientras no esté confirmado o haya fechas/pagos por revisar.
La confirmación del usuario no elimina automáticamente esos avisos.

## Cálculos

Todos los importes se calculan por moneda. No hay conversión automática ni un total ARS + USD.

- **Ingresos cobrados:** movimientos activos, operativos, de ingreso, con estado Cobrado y fecha efectiva hasta el corte de caja.
- **Gastos pagados:** movimientos activos, operativos, de egreso, con estado Pagado. Incluye gastos afrontados por socios.
- **Resultado operativo de caja:** ingresos cobrados menos gastos pagados; no es una ganancia contable integral.
- **Aportes:** entradas AporteSocio y gastos pagados directamente por un socio como aporte.
- **Retiros:** salidas RetiroSocio, sin afectar el resultado operativo.
- **Variación de caja:** entradas menos salidas efectivas financiadas por Empresa; incluye aportes, retiros, reintegros y cambios de moneda.
- **Saldo neto registrado (`recordedCashBalance`):** acumulado de entradas menos salidas efectivas de Empresa desde el primer registro hasta el corte, aunque no haya configuración de saldos iniciales. Incluye cobros de meses anteriores; no es un saldo bancario confirmado.
- **Saldo ajustado al punto de partida (`balance`):** saldo inicial de esa moneda + variación desde la fecha configurada hasta el corte. Si falta fecha o saldo, se devuelve `null`, pero el acumulado registrado sigue visible.
- El saldo inicial corresponde al inicio de la fecha configurada, antes de los movimientos de ese día. No se deben registrar otra vez como movimientos los importes ya incluidos en él.
- Sin un filtro explícito de fecha inicial, el histórico conserva todos los movimientos: configurar el punto de partida no oculta cobros anteriores.
- Un filtro de período afecta cobros, gastos y resultado del período. El saldo neto registrado conserva el acumulado hasta el corte, y el saldo ajustado conserva su base inicial. La evolución mensual muestra el saldo neto registrado al cierre.
- **Pendientes:** incluye Pendiente y Vencido, sin afectar la caja. Sin fecha final de consulta se incluyen también obligaciones futuras ya registradas; con rango se usa la fecha del movimiento.
- Se excluyen anulados e inactivos. Los cobros y pagos con fecha futura no aumentan ni reducen la caja actual.

El resumen ofrece evolución mensual, gastos por categoría, saldos de socios y accesos
a los movimientos de cobros/gastos usando su fecha efectiva. Los totales no dependen
de la página del listado. Las consultas agregan en la base de datos y el resumen usa
una transacción de lectura Repeatable Read para mantener coherencia entre sus cifras.

Ejemplo de regresión: cinco cobros de mayo a julio por ARS 5.200.000 y un gasto
de septiembre por ARS 400.000 muestran ARS 4.800.000 acumulados, incluso sin saldo
inicial. En septiembre los indicadores mensuales muestran ingresos cero, gastos
ARS 400.000 y resultado -ARS 400.000; ese resultado mensual no sustituye al histórico.
Si se informa ARS 5.200.000 como saldo al inicio de septiembre, el saldo ajustado
también será ARS 4.800.000, sin sumar otra vez los cobros anteriores a esa fecha.

## Clasificación y financiación

Los socios pueden tomar nombre y correo de una persona existente de Equipo o de una
cuenta sin perfil, o registrarse manualmente. La vinculación mantiene el mismo PartnerId
y todo su historial. Se realiza desde Visión financiera → Configurar → Gestionar socios.
La condición de socio y su actividad financiera son independientes de los roles y del acceso al sistema.

`Nature` admite Operacion, AporteSocio, RetiroSocio, ReintegroSocio y CambioMoneda.
`Funding` admite Empresa, SocioAporte y SocioReintegrable.

- Operacion/Egreso pagado por Empresa reduce resultado y caja.
- Operacion/Egreso con SocioAporte reduce resultado y registra aporte no monetario; no reduce caja de la empresa.
- Operacion/Egreso con SocioReintegrable reduce resultado, genera saldo a devolver al socio y no reduce caja de la empresa.
- ReintegroSocio reduce ese saldo y la caja, sin registrar otra vez el gasto.
- Se rechazan reintegros que superen los gastos reintegrables registrados y ediciones que dejen un exceso de reintegros.
- Aportes, retiros y gastos afrontados por socios requieren un socio. Las operaciones de empresa comunes no lo requieren.

Los aportes, retiros y reintegros se registran cuando se hacen efectivos. La categoría
describe el concepto; la clasificación determina cómo participa en los cálculos.

### Atribuir un egreso existente a un socio

Editar el movimiento y conservar **Egreso → Operación de la empresa**. En **Quién
afrontó el gasto**, elegir **Un socio como aporte (sin devolución)** y seleccionar
el socio. El estado debe ser **Pagado**, con su fecha efectiva original revisada.
Guardar actualiza el mismo registro: no se crea un ingreso adicional.

Por ejemplo, “Pago Marketing” de ARS 400.000 continúa siendo un gasto de ARS 400.000,
suma ese importe a los aportes del socio y deja de descontarlo de la caja empresarial.
**Aporte de dinero de un socio** se ofrece únicamente para ingresos de dinero a la empresa.

Al cambiar el tipo, el formulario ajusta la clasificación y el estado Cobrado/Pagado
y exige volver a seleccionar una categoría compatible. En edición se permite conservar
la categoría original aunque esté archivada. Las validaciones se muestran junto a
Guardar y enfocan el campo a corregir; los errores del servidor también permanecen
visibles allí, conservando los datos para reintentar.

Los movimientos asociados a proyectos cancelados no admiten nuevas altas ni modificaciones.
La eliminación de un acuerdo desde el proyecto anula sus pagos y egresos vinculados;
por eso dejan de afectar gastos, caja y pendientes, pero se conservan como anulados para
mantener el historial verificable.

## Cambios de moneda

Una operación de cambio crea exactamente dos movimientos vinculados por ExchangeId:
egreso en la moneda entregada e ingreso en la moneda recibida, con importes reales y
fecha efectiva. No participa en el resultado operativo ni en los porcentajes del equipo.
Las comisiones se cargan como gastos operativos separados.

La operación admite reintento con el mismo RequestId. Para corregirla, se anulan ambas
partes y se registra una nueva; no se permite editar una sola parte desde el CRUD general.

## Contratos y pagos del equipo

- Los porcentajes se aplican a ingresos operativos registrados del proyecto, por su fecha de movimiento, dentro de las fechas del acuerdo: Pendiente, Cobrado y Vencido.
- Se calcula y redondea a dos decimales por mes y moneda. Sin ingresos, la obligación por porcentaje es cero. Se excluyen aportes, cambios, anulados e inactivos.
- No se genera un pago automático al registrar un ingreso. El pago se registra cuando ocurre.
- El monto fijo exige moneda y es una obligación total del acuerdo, no una deuda que se repite cada año. Sus pagos se acumulan durante toda la vida del acuerdo.
- Cada pago registra Amount/Currency reales y AppliedAmount/AppliedCurrency de la obligación cancelada. Con la misma moneda ambos importes deben coincidir.
- Ejemplo: pagar ARS 450.000 y cancelar USD 300 implica una cotización aplicada de 1500 ARS/USD. La cotización se deriva de esos importes, sin consultas externas.
- Los nuevos pagos no pueden superar el saldo de la obligación por moneda/período. Primero se revisan los pagos históricos no clasificados del acuerdo.
- La revisión de un pago histórico conserva el hecho pagado aunque arroje un saldo a favor; no inventa una nueva obligación para justificarlo.
- Un pago nuevo genera un egreso pagado o vincula uno existente compatible. Un pago histórico requiere vincular su egreso existente; si falta, se carga primero en Finanzas.
- La vinculación exige egreso operativo de Empresa, activo, sin otro pago asociado, con mismo importe y moneda, y mismo proyecto o sin proyecto. Si ya está pagado, debe coincidir la fecha efectiva. Si está pendiente o vencido, se marca pagado dentro de la misma transacción, conservando su fecha original.
- La relación es única. Registrar, editar y anular pago/egreso se realiza en una transacción. El egreso vinculado se edita desde el pago, no desde el CRUD general de Finanzas.
- Las correcciones conservan la fecha original y descripción del movimiento, actualizando su fecha efectiva e importes.
- Las anulaciones conservan los registros y excluyen ambos de los cálculos.
- Contratos con pagos no pueden cambiar de miembro, modalidad ni moneda confirmada; se crea otro acuerdo.

Los **saldos de contratos del equipo** se consultan en una sección independiente de
Finanzas, paginada, y en el proyecto. No se suman a los egresos pendientes registrados:
pueden representar la misma obligación y no existe una vinculación de devengamientos
que permita consolidarlos automáticamente sin duplicación.

## API

Todas las rutas financieras requieren Administrador; las escrituras además usan FinancesWrite.

- `GET /api/finance/overview?from=&to=`: configuración, indicadores por moneda con recordedCashBalance, evolución mensual, categorías con identificador y saldos de socios; consumido por Visión financiera.
- `GET /api/financial-movements/monthly-summary`: totales mensuales por moneda sin el análisis histórico completo. Inicio utiliza la misma operación de servicio y devuelve ese resumen reducido en `finance`.
- `PUT /api/finance/setup`: startDate, openingArs, openingUsd, historyComplete, version.
- `GET/POST /api/finance/partners`, `PUT /api/finance/partners/{id}`: socios y actividad.
- `POST /api/finance/exchanges`: requestId, fromCurrency/fromAmount, toCurrency/toAmount, date, notes.
- `DELETE /api/finance/exchanges/{id}`: anulación de las dos partes.
- `GET /api/finance/team-obligations?year=2026&pageNumber=1&pageSize=10`: acuerdos y saldos separados por moneda.
- Movimientos: `GET/POST /api/financial-movements`, `GET/PUT /api/financial-movements/{id}`.
- Filtros de movimientos: currency, nature, projectId, unlinkedOnly, exactAmount y useSettlementDate, además de los anteriores.
- Alta manual: requestId estable por operación, usado como ID del movimiento para reintentos sin duplicados. Edición: expectedVersion tomada del detalle.
- Contratos de monto fijo: currency obligatoria. Porcentaje: sin moneda fija, se aplica por cada moneda de los ingresos.
- `POST /api/developer-contracts/{id}/payments`: monedas, importes reales/aplicados, período, requestId y existingMovementId opcional.
- `PUT /api/developer-payments/{id}`: misma estructura y expectedVersion.
- `DELETE /api/developer-payments/{id}?expectedVersion=...`: anulación coordinada.

Los endpoints anteriores de categorías, proveedores y comprobantes siguen disponibles.
Los campos escalares antiguos del resumen mensual, del dashboard y del ledger representan
solo ARS por compatibilidad; los consumidores nuevos deben usar la colección `currencies`.

## Migración

`20260922161932_FinanceHistoryCurrencies`:

- Conserva los movimientos existentes como ARS / Operacion / Empresa, según la confirmación del usuario.
- En cobrados/pagados copia MovementDate a SettlementDate como referencia provisional y marca SettlementDateEstimated. La revisión se realiza en el formulario del movimiento.
- No inventa monedas de acuerdos/pagos ni crea egresos históricos. Esos campos quedan nulos hasta revisión.
- Agrega socios, configuración, relaciones, índices únicos y versiones para detectar ediciones desactualizadas.
- No configura fecha inicial ni saldos automáticamente.
- Impide un retroceso que descartaría monedas, fechas efectivas diferentes, configuración o vínculos nuevos.

Las escrituras financieras comparten un bloqueo transaccional de PostgreSQL. Los
identificadores de operación evitan duplicaciones en reintentos y los índices únicos
protegen el vínculo pago/egreso y la identidad de cada pago.

## Verificación

- `FinanceHistoryTests`: monedas, caja/pendientes, fechas, carga retroactiva, saldos iniciales, socios, reintegros, cambios, porcentajes, pagos cruzados, vinculación histórica, correcciones, anulaciones y reintentos. Usa EF InMemory.
- Frontend: pruebas de vista histórica, estados desconocidos, filtros, clasificación de pagos históricos, fechas efectivas, moneda e identificación estable al reintentar.
- Regresiones: cobros históricos sin saldo inicial, punto de partida posterior a los cobros, equivalencia de totales mensuales y navegación al módulo [Visión financiera](vision-financiera.md).
- Compilar backend/frontend, ejecutar las suites y comprobar las diferencias del modelo EF.
- Revisar/aplicar la migración en PostgreSQL de prueba y verificar allí transacciones, bloqueos y recuperación ante fallos; InMemory no verifica esos comportamientos relacionales.
