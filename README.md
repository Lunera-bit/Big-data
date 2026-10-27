# Big-data — Data Mart MultiPizza

Dashboard de Power BI sobre un data mart en modelo estrella para analizar la
trazabilidad transaccional de ventas de MultiPizza (México, Pasacana y
Universitaria), Lima, 2026.

**Título del proyecto:** Data Mart para la toma de decisiones frente a la
ineficiente trazabilidad transaccional de ventas en la empresa MultiPizza,
Lima, 2026.

## Contenido

- `POWER BI.pbip`: proyecto de Power BI (abre este archivo).
- `POWER BI.Report/`: páginas, visuales, tema e iconos.
- `POWER BI.SemanticModel/`: modelo tabular (tablas, relaciones, medidas, roles).
- `DB_DATAMART_MULTIPIZZA_completo.sql`: creación de la base, tablas, todos los
  datos, restricciones, índices y vista `vw_Ventas`.
- `kpis_multipizza.sql`: consultas de los KPI contra la vista.

## Requisitos

- Power BI Desktop (julio 2026 o superior).
- SQL Server Express con la instancia `localhost\SQLEXPRESS`.

## Cómo abrir

1. Ejecutar `DB_DATAMART_MULTIPIZZA_completo.sql` en SQL Server Management Studio.
2. Abrir `POWER BI.pbip` con Power BI Desktop.
3. Pulsar **Actualizar** (`Ctrl+R`) e ingresar las credenciales de Windows si
   Power BI las solicita.
4. Si la instancia de SQL Server no se llama `localhost\SQLEXPRESS`, cambiarla
   en **Inicio > Transformar datos > Origen** (las cinco consultas usan ese
   servidor).

El modelo es de tipo **Import**: el repositorio no incluye la caché de datos
(`.pbi/cache.abf`), por eso es necesario ejecutar el script y actualizar.

## KPIs incluidos

- Ventas Netas Totales (S/).
- Ticket Promedio (S/).
- Total de Unidades Vendidas.
- Ranking de Top Productos y presentaciones.
- Participación de Ventas por Local (%).
- Distribución Temporal por día, hora y turno.

## Datos

- 3 locales, 27 productos, 77 presentaciones y 77 días (agosto–octubre 2026).
- 8,863 líneas de venta y 5,931 tickets.
- Ventas netas: S/ 372,697.70; ticket promedio: S/ 62.84; 13,309 unidades.

## Autor

Lunera-bit — 2026.
