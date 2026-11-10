using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string projectPath = ProjectPicker.SelectProject();
        if (!String.IsNullOrEmpty(projectPath)) Application.Run(new ReportForm(projectPath));
    }
}

internal sealed class ChartValue
{
    public int Order;
    public string Category;
    public string Series;
    public decimal Value;
    public ChartValue(int order, string category, string series, decimal value)
    {
        Order = order; Category = category; Series = series; Value = value;
    }
}

internal sealed class LocalMetric
{
    public string Name;
    public decimal Sales;
    public long Tickets;
    public decimal Average;
    public long Units;
    public decimal Share;
}

internal sealed class ReportData
{
    public decimal Sales;
    public long Tickets;
    public long Units;
    public decimal Average;
    public DateTime? DateFrom;
    public DateTime? DateTo;
    public string PeakHour = "—";
    public decimal NightShare;
    public readonly List<ChartValue> Monthly = new List<ChartValue>();
    public readonly List<ChartValue> Weekdays = new List<ChartValue>();
    public readonly List<ChartValue> Products = new List<ChartValue>();
    public readonly List<ChartValue> Presentations = new List<ChartValue>();
    public readonly List<ChartValue> Types = new List<ChartValue>();
    public readonly List<ChartValue> Hours = new List<ChartValue>();
    public readonly List<ChartValue> Shifts = new List<ChartValue>();
    public readonly List<LocalMetric> Locals = new List<LocalMetric>();
}

internal static class ReportRepository
{
    private const string ConnectionString = "Server=localhost\\SQLEXPRESS;Database=DB_DATAMART_MULTIPIZZA;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5;";
    private const string Filter = "(@local IS NULL OR l.nombre_local = @local) AND (@month IS NULL OR t.mes_numero = @month)";

    public static ReportData Load(string local, int? month)
    {
        ReportData data = new ReportData();
        using (SqlConnection connection = new SqlConnection(ConnectionString))
        using (SqlCommand command = connection.CreateCommand())
        {
            command.CommandTimeout = 15;
            command.CommandText = @"
SELECT COALESCE(SUM(f.subtotal),0), COUNT_BIG(DISTINCT f.id_venta), COALESCE(SUM(CONVERT(bigint,f.cantidad)),0), MIN(t.fecha), MAX(t.fecha)
FROM dbo.FACT_VENTA f JOIN dbo.DIM_TIEMPO t ON t.id_tiempo=f.id_tiempo JOIN dbo.DIM_LOCAL l ON l.id_local=f.id_local
WHERE " + Filter + @";

SELECT t.mes_numero,t.mes,l.nombre_local,SUM(f.subtotal)
FROM dbo.FACT_VENTA f JOIN dbo.DIM_TIEMPO t ON t.id_tiempo=f.id_tiempo JOIN dbo.DIM_LOCAL l ON l.id_local=f.id_local
WHERE " + Filter + @"
GROUP BY t.mes_numero,t.mes,l.nombre_local ORDER BY t.mes_numero,CASE l.nombre_local WHEN 'Mexico' THEN 1 WHEN 'Pasacana' THEN 2 ELSE 3 END;

SELECT CASE t.dia_semana WHEN 'lunes' THEN 1 WHEN 'martes' THEN 2 WHEN 'miercoles' THEN 3 WHEN 'jueves' THEN 4 WHEN 'viernes' THEN 5 WHEN 'sabado' THEN 6 ELSE 7 END,
       t.dia_semana,l.nombre_local,SUM(f.subtotal)
FROM dbo.FACT_VENTA f JOIN dbo.DIM_TIEMPO t ON t.id_tiempo=f.id_tiempo JOIN dbo.DIM_LOCAL l ON l.id_local=f.id_local
WHERE " + Filter + @"
GROUP BY t.dia_semana,l.nombre_local ORDER BY 1,CASE l.nombre_local WHEN 'Mexico' THEN 1 WHEN 'Pasacana' THEN 2 ELSE 3 END;

;WITH totals AS (
 SELECT TOP (10) p.id_producto,p.nombre_producto,SUM(f.subtotal) AS total
 FROM dbo.FACT_VENTA f JOIN dbo.DIM_TIEMPO t ON t.id_tiempo=f.id_tiempo JOIN dbo.DIM_LOCAL l ON l.id_local=f.id_local JOIN dbo.DIM_PRODUCTO p ON p.id_producto=f.id_producto
 WHERE " + Filter + @"
 GROUP BY p.id_producto,p.nombre_producto ORDER BY SUM(f.subtotal) DESC,p.nombre_producto
), ranked AS (SELECT ROW_NUMBER() OVER (ORDER BY total DESC,nombre_producto) AS rn,id_producto,nombre_producto FROM totals)
SELECT r.rn,r.nombre_producto,l.nombre_local,SUM(f.subtotal)
FROM ranked r JOIN dbo.FACT_VENTA f ON f.id_producto=r.id_producto JOIN dbo.DIM_TIEMPO t ON t.id_tiempo=f.id_tiempo JOIN dbo.DIM_LOCAL l ON l.id_local=f.id_local
WHERE " + Filter + @"
GROUP BY r.rn,r.nombre_producto,l.nombre_local ORDER BY r.rn,CASE l.nombre_local WHEN 'Mexico' THEN 1 WHEN 'Pasacana' THEN 2 ELSE 3 END;

;WITH totals AS (
 SELECT TOP (10) pr.id_presentacion,CONCAT(p.nombre_producto,N' - ',pr.nombre_presentacion) AS etiqueta,SUM(f.subtotal) AS total
 FROM dbo.FACT_VENTA f JOIN dbo.DIM_TIEMPO t ON t.id_tiempo=f.id_tiempo JOIN dbo.DIM_LOCAL l ON l.id_local=f.id_local JOIN dbo.DIM_PRESENTACION pr ON pr.id_presentacion=f.id_presentacion JOIN dbo.DIM_PRODUCTO p ON p.id_producto=pr.id_producto
 WHERE " + Filter + @"
 GROUP BY pr.id_presentacion,p.nombre_producto,pr.nombre_presentacion ORDER BY SUM(f.subtotal) DESC,etiqueta
), ranked AS (SELECT ROW_NUMBER() OVER (ORDER BY total DESC,etiqueta) AS rn,id_presentacion,etiqueta FROM totals)
SELECT r.rn,r.etiqueta,l.nombre_local,SUM(f.subtotal)
FROM ranked r JOIN dbo.FACT_VENTA f ON f.id_presentacion=r.id_presentacion JOIN dbo.DIM_TIEMPO t ON t.id_tiempo=f.id_tiempo JOIN dbo.DIM_LOCAL l ON l.id_local=f.id_local
WHERE " + Filter + @"
GROUP BY r.rn,r.etiqueta,l.nombre_local ORDER BY r.rn,CASE l.nombre_local WHEN 'Mexico' THEN 1 WHEN 'Pasacana' THEN 2 ELSE 3 END;

;WITH totals AS (
 SELECT TOP (10) pr.nombre_presentacion,SUM(f.subtotal) AS total
 FROM dbo.FACT_VENTA f JOIN dbo.DIM_TIEMPO t ON t.id_tiempo=f.id_tiempo JOIN dbo.DIM_LOCAL l ON l.id_local=f.id_local JOIN dbo.DIM_PRESENTACION pr ON pr.id_presentacion=f.id_presentacion
 WHERE " + Filter + @"
 GROUP BY pr.nombre_presentacion ORDER BY SUM(f.subtotal) DESC,pr.nombre_presentacion
), ranked AS (SELECT ROW_NUMBER() OVER (ORDER BY total DESC,nombre_presentacion) AS rn,nombre_presentacion FROM totals)
SELECT r.rn,r.nombre_presentacion,l.nombre_local,SUM(f.subtotal)
FROM ranked r JOIN dbo.DIM_PRESENTACION pr ON pr.nombre_presentacion=r.nombre_presentacion JOIN dbo.FACT_VENTA f ON f.id_presentacion=pr.id_presentacion JOIN dbo.DIM_TIEMPO t ON t.id_tiempo=f.id_tiempo JOIN dbo.DIM_LOCAL l ON l.id_local=f.id_local
WHERE " + Filter + @"
GROUP BY r.rn,r.nombre_presentacion,l.nombre_local ORDER BY r.rn,CASE l.nombre_local WHEN 'Mexico' THEN 1 WHEN 'Pasacana' THEN 2 ELSE 3 END;

;WITH allLocals AS (
 SELECT l.id_local,l.nombre_local,SUM(f.subtotal) AS ventas,COUNT_BIG(DISTINCT f.id_venta) AS tickets,SUM(CONVERT(bigint,f.cantidad)) AS unidades
 FROM dbo.FACT_VENTA f JOIN dbo.DIM_TIEMPO t ON t.id_tiempo=f.id_tiempo JOIN dbo.DIM_LOCAL l ON l.id_local=f.id_local
 WHERE (@month IS NULL OR t.mes_numero=@month)
 GROUP BY l.id_local,l.nombre_local
), totalSales AS (SELECT SUM(ventas) AS total FROM allLocals)
SELECT a.nombre_local,a.ventas,a.tickets,a.ventas/NULLIF(a.tickets,0),a.unidades,a.ventas/NULLIF(ts.total,0)
FROM allLocals a CROSS JOIN totalSales ts WHERE (@local IS NULL OR a.nombre_local=@local)
ORDER BY CASE a.nombre_local WHEN 'Mexico' THEN 1 WHEN 'Pasacana' THEN 2 ELSE 3 END;

SELECT DATEPART(HOUR,f.hora),RIGHT('0'+CONVERT(varchar(2),DATEPART(HOUR,f.hora)),2)+':00',l.nombre_local,SUM(f.subtotal)
FROM dbo.FACT_VENTA f JOIN dbo.DIM_TIEMPO t ON t.id_tiempo=f.id_tiempo JOIN dbo.DIM_LOCAL l ON l.id_local=f.id_local
WHERE " + Filter + @"
GROUP BY DATEPART(HOUR,f.hora),l.nombre_local ORDER BY DATEPART(HOUR,f.hora),CASE l.nombre_local WHEN 'Mexico' THEN 1 WHEN 'Pasacana' THEN 2 ELSE 3 END;

SELECT CASE WHEN DATEPART(HOUR,f.hora)<16 THEN 1 WHEN DATEPART(HOUR,f.hora)<19 THEN 2 ELSE 3 END,
       CASE WHEN DATEPART(HOUR,f.hora)<16 THEN N'Almuerzo (12-15)' WHEN DATEPART(HOUR,f.hora)<19 THEN N'Tarde (16-18)' ELSE N'Noche (19-21)' END,
       l.nombre_local,SUM(f.subtotal)
FROM dbo.FACT_VENTA f JOIN dbo.DIM_TIEMPO t ON t.id_tiempo=f.id_tiempo JOIN dbo.DIM_LOCAL l ON l.id_local=f.id_local
WHERE " + Filter + @"
GROUP BY CASE WHEN DATEPART(HOUR,f.hora)<16 THEN 1 WHEN DATEPART(HOUR,f.hora)<19 THEN 2 ELSE 3 END,
         CASE WHEN DATEPART(HOUR,f.hora)<16 THEN N'Almuerzo (12-15)' WHEN DATEPART(HOUR,f.hora)<19 THEN N'Tarde (16-18)' ELSE N'Noche (19-21)' END,
         l.nombre_local
ORDER BY 1,CASE l.nombre_local WHEN 'Mexico' THEN 1 WHEN 'Pasacana' THEN 2 ELSE 3 END;

;WITH hourly AS (
 SELECT DATEPART(HOUR,f.hora) AS hora,SUM(f.subtotal) AS ventas
 FROM dbo.FACT_VENTA f JOIN dbo.DIM_TIEMPO t ON t.id_tiempo=f.id_tiempo JOIN dbo.DIM_LOCAL l ON l.id_local=f.id_local
 WHERE " + Filter + @" GROUP BY DATEPART(HOUR,f.hora)
)
SELECT COALESCE((SELECT TOP (1) RIGHT('0'+CONVERT(varchar(2),hora),2)+':00' FROM hourly ORDER BY ventas DESC),'—'),
       COALESCE(SUM(CASE WHEN DATEPART(HOUR,f.hora)>=19 THEN f.subtotal ELSE 0 END)/NULLIF(SUM(f.subtotal),0),0)
FROM dbo.FACT_VENTA f JOIN dbo.DIM_TIEMPO t ON t.id_tiempo=f.id_tiempo JOIN dbo.DIM_LOCAL l ON l.id_local=f.id_local
WHERE " + Filter + ";";

            command.Parameters.Add("@local", SqlDbType.VarChar, 100).Value = String.IsNullOrEmpty(local) ? (object)DBNull.Value : local;
            command.Parameters.Add("@month", SqlDbType.Int).Value = month.HasValue ? (object)month.Value : DBNull.Value;
            connection.Open();
            using (SqlDataReader reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    data.Sales = Convert.ToDecimal(reader.GetValue(0));
                    data.Tickets = Convert.ToInt64(reader.GetValue(1));
                    data.Units = Convert.ToInt64(reader.GetValue(2));
                    if (!reader.IsDBNull(3)) data.DateFrom = reader.GetDateTime(3);
                    if (!reader.IsDBNull(4)) data.DateTo = reader.GetDateTime(4);
                    data.Average = data.Tickets == 0 ? 0 : data.Sales / data.Tickets;
                }
                ReadPoints(reader, data.Monthly, true, "month");
                ReadPoints(reader, data.Weekdays, true, "day");
                ReadPoints(reader, data.Products, true, "text");
                ReadPoints(reader, data.Presentations, true, "text");
                ReadPoints(reader, data.Types, true, "text");
                if (reader.NextResult())
                {
                    while (reader.Read())
                    {
                        LocalMetric metric = new LocalMetric();
                        metric.Name = reader.GetString(0);
                        metric.Sales = Convert.ToDecimal(reader.GetValue(1));
                        metric.Tickets = Convert.ToInt64(reader.GetValue(2));
                        metric.Average = Convert.ToDecimal(reader.GetValue(3));
                        metric.Units = Convert.ToInt64(reader.GetValue(4));
                        metric.Share = Convert.ToDecimal(reader.GetValue(5));
                        data.Locals.Add(metric);
                    }
                }
                ReadPoints(reader, data.Hours, true, "hour");
                ReadPoints(reader, data.Shifts, true, "text");
                if (reader.NextResult() && reader.Read())
                {
                    data.PeakHour = Convert.ToString(reader.GetValue(0));
                    data.NightShare = Convert.ToDecimal(reader.GetValue(1));
                }
            }
        }
        return data;
    }

    private static void ReadPoints(SqlDataReader reader, List<ChartValue> target, bool hasOrder, string kind)
    {
        if (!reader.NextResult()) return;
        while (reader.Read())
        {
            int order = hasOrder ? Convert.ToInt32(reader.GetValue(0)) : 0;
            int categoryColumn = hasOrder ? 1 : 0;
            string category = Convert.ToString(reader.GetValue(categoryColumn));
            string series = Convert.ToString(reader.GetValue(categoryColumn + 1));
            decimal value = Convert.ToDecimal(reader.GetValue(categoryColumn + 2));
            if (kind == "month") category = MonthName(order);
            else if (kind == "day") category = DayName(order);
            else if (kind == "hour") category = order.ToString("00") + ":00";
            target.Add(new ChartValue(order, category, series, value));
        }
    }

    private static string MonthName(int month)
    {
        string[] names = { "", "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
        return month > 0 && month < names.Length ? names[month] : "Mes";
    }

    private static string DayName(int day)
    {
        string[] names = { "", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado", "domingo" };
        return day > 0 && day < names.Length ? names[day] : "Día";
    }
}
