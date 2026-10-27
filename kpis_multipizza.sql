USE DB_DATAMART_MULTIPIZZA;
GO
/* La hora real de la venta vive en FACT_VENTA.hora.
   DIM_TIEMPO.hora esta en NULL (la dimension es de grano diario). */
CREATE OR ALTER VIEW vw_Ventas AS
SELECT f.id_detalle, f.id_venta, t.fecha, t.anio, t.mes_numero, t.mes,
       t.trimestre, t.semana, t.dia, t.dia_semana,
       CAST(f.hora AS TIME(0)) AS hora,
       CASE WHEN DATEPART(hour, f.hora) < 16 THEN 'Almuerzo (12-15)'
            WHEN DATEPART(hour, f.hora) < 19 THEN 'Tarde (16-18)'
            ELSE 'Noche (19-21)' END AS turno,
       l.id_local, l.nombre_local,
       p.id_producto, p.nombre_producto, p.categoria,
       pr.id_presentacion, pr.nombre_presentacion,
       f.cantidad, f.precio_unitario, f.subtotal
FROM FACT_VENTA f
JOIN DIM_TIEMPO t        ON t.id_tiempo = f.id_tiempo
JOIN DIM_LOCAL l         ON l.id_local = f.id_local
JOIN DIM_PRODUCTO p      ON p.id_producto = f.id_producto
JOIN DIM_PRESENTACION pr ON pr.id_presentacion = f.id_presentacion;
GO

PRINT '=== KPI 1: Ventas Netas Totales ===';
SELECT ROUND(SUM(subtotal), 2) AS ventas_netas, COUNT(DISTINCT id_venta) AS tickets
FROM FACT_VENTA;

PRINT '=== KPI 2: Ticket Promedio ===';
SELECT CAST(SUM(subtotal) / COUNT(DISTINCT id_venta) AS DECIMAL(10,2)) AS ticket_promedio
FROM FACT_VENTA;

PRINT '=== KPI 3: Unidades Vendidas ===';
SELECT SUM(cantidad) AS unidades, COUNT(*) AS lineas
FROM FACT_VENTA;

PRINT '=== KPI 4a: Top 10 productos por ingresos ===';
SELECT TOP 10 nombre_producto, categoria,
       SUM(cantidad) AS unidades,
       ROUND(SUM(subtotal), 2) AS ingresos,
       CAST(100.0 * SUM(subtotal) / SUM(SUM(subtotal)) OVER () AS DECIMAL(5,1)) AS pct
FROM vw_Ventas
GROUP BY nombre_producto, categoria
ORDER BY ingresos DESC;

PRINT '=== KPI 4b: Top 10 producto + presentacion por ingresos ===';
SELECT TOP 10 nombre_producto, nombre_presentacion,
       SUM(cantidad) AS unidades,
       ROUND(SUM(subtotal), 2) AS ingresos,
       CAST(100.0 * SUM(subtotal) / SUM(SUM(subtotal)) OVER () AS DECIMAL(5,1)) AS pct
FROM vw_Ventas
GROUP BY nombre_producto, nombre_presentacion
ORDER BY ingresos DESC;

PRINT '=== KPI 4c: Ventas por tipo de presentacion ===';
SELECT nombre_presentacion,
       SUM(cantidad) AS unidades,
       ROUND(SUM(subtotal), 2) AS ingresos,
       CAST(100.0 * SUM(subtotal) / SUM(SUM(subtotal)) OVER () AS DECIMAL(5,1)) AS pct
FROM vw_Ventas
GROUP BY nombre_presentacion
ORDER BY ingresos DESC;

PRINT '=== KPI 5: Participacion por local ===';
SELECT nombre_local,
       COUNT(DISTINCT id_venta) AS tickets,
       SUM(cantidad) AS unidades,
       ROUND(SUM(subtotal), 2) AS ingresos,
       CAST(100.0 * SUM(subtotal) / SUM(SUM(subtotal)) OVER () AS DECIMAL(5,1)) AS pct
FROM vw_Ventas
GROUP BY nombre_local
ORDER BY ingresos DESC;

PRINT '=== KPI 6a: Distribucion por dia de semana ===';
SELECT dia_semana, COUNT(DISTINCT fecha) AS dias,
       COUNT(DISTINCT id_venta) AS tickets,
       ROUND(SUM(subtotal), 2) AS ingresos,
       CAST(SUM(subtotal) / COUNT(DISTINCT fecha) AS DECIMAL(10,2)) AS promedio_dia
FROM vw_Ventas
GROUP BY dia_semana
ORDER BY CASE dia_semana WHEN 'lunes' THEN 1 WHEN 'martes' THEN 2 WHEN 'miercoles' THEN 3
         WHEN 'jueves' THEN 4 WHEN 'viernes' THEN 5 WHEN 'sabado' THEN 6 ELSE 7 END;

PRINT '=== KPI 6b: Distribucion por hora ===';
SELECT DATEPART(hour, hora) AS hora,
       COUNT(DISTINCT id_venta) AS tickets,
       SUM(cantidad) AS unidades,
       ROUND(SUM(subtotal), 2) AS ingresos
FROM vw_Ventas
GROUP BY DATEPART(hour, hora)
ORDER BY hora;

PRINT '=== KPI 6b.1: Hora punta ===';
SELECT TOP 1 DATEPART(hour, hora) AS hora_punta, ROUND(SUM(subtotal), 2) AS ingresos
FROM vw_Ventas
GROUP BY DATEPART(hour, hora)
ORDER BY ingresos DESC;

PRINT '=== KPI 6c: Distribucion por turno ===';
SELECT turno,
       COUNT(DISTINCT id_venta) AS tickets,
       ROUND(SUM(subtotal), 2) AS ingresos,
       CAST(100.0 * SUM(subtotal) / SUM(SUM(subtotal)) OVER () AS DECIMAL(5,1)) AS pct
FROM vw_Ventas
GROUP BY turno
ORDER BY CASE turno WHEN 'Almuerzo (12-15)' THEN 1 WHEN 'Tarde (16-18)' THEN 2 ELSE 3 END;

PRINT '=== Extra: Ventas por mes ===';
SELECT anio, mes_numero, mes, ROUND(SUM(subtotal), 2) AS ingresos
FROM vw_Ventas
GROUP BY anio, mes_numero, mes
ORDER BY anio, mes_numero;
