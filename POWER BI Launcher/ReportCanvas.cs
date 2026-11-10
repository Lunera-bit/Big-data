using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

internal sealed class DashboardCanvas : Control
{
    private sealed class TooltipRegion
    {
        public RectangleF Bounds;
        public bool IsDonut;
        public PointF Center;
        public float InnerRadius;
        public float OuterRadius;
        public float StartAngle;
        public float SweepAngle;
        public string Key;
        public string Heading;
        public string Label;
        public string Detail;
        public string ValueText;

        public bool Contains(Point p)
        {
            if (!IsDonut) return Bounds.Contains(p.X, p.Y);
            double dx = p.X - Center.X;
            double dy = p.Y - Center.Y;
            double radius = Math.Sqrt(dx * dx + dy * dy);
            if (radius < InnerRadius || radius > OuterRadius) return false;
            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            if (angle < 0) angle += 360.0;
            double start = StartAngle % 360.0;
            if (start < 0) start += 360.0;
            double delta = (angle - start + 360.0) % 360.0;
            return delta <= SweepAngle;
        }

        public static TooltipRegion ForBar(RectangleF bounds, string heading, string category, string series, decimal value)
        {
            string local = series == "Mexico" ? "México" : series;
            string detail = String.IsNullOrEmpty(series) ? "" : "Local: " + local;
            return Create(bounds, heading, category, detail, "Ventas netas: S/ " + value.ToString("#,##0.00", CultureInfo.InvariantCulture));
        }

        public static TooltipRegion ForBar(RectangleF bounds, string heading, string category, string series, decimal value, string detail)
        {
            return Create(bounds, heading, category, detail, "Ventas netas: S/ " + value.ToString("#,##0.00", CultureInfo.InvariantCulture));
        }

        public static TooltipRegion ForDonut(Rectangle bounds, float innerRadius, float start, float sweep, string heading, string label, string detail, string value)
        {
            TooltipRegion region = Create(bounds, heading, label, detail, value);
            region.IsDonut = true;
            region.Center = new PointF(bounds.X + bounds.Width / 2F, bounds.Y + bounds.Height / 2F);
            region.InnerRadius = innerRadius;
            region.OuterRadius = bounds.Width / 2F;
            region.StartAngle = start;
            region.SweepAngle = sweep;
            return region;
        }

        public static TooltipRegion ForInfo(RectangleF bounds, string heading, string label, string detail, string value)
        {
            return Create(bounds, heading, label, detail, value);
        }

        private static TooltipRegion Create(RectangleF bounds, string heading, string label, string detail, string value)
        {
            TooltipRegion region = new TooltipRegion();
            region.Bounds = bounds;
            region.Heading = heading;
            region.Label = label;
            region.Detail = detail;
            region.ValueText = value;
            region.Key = heading + "|" + label + "|" + detail + "|" + value;
            return region;
        }
    }

    private const int CanvasWidth = 1280;
    private const int CanvasHeight = 720;
    private static readonly Color[] SeriesColors = {
        Color.FromArgb(22, 135, 248), Color.FromArgb(27, 38, 159), Color.FromArgb(238, 103, 49)
    };
    private static readonly string[] PageNames = { "Resumen", "Locales", "Productos", "Horarios" };
    private static readonly string[] PageTitles = {
        "Data Mart para la toma de decisiones frente a la ineficiente trazabilidad transaccional de ventas en la empresa MultiPizza, Lima, 2026",
        "MultiPizza · Locales", "MultiPizza · Productos", "MultiPizza · Horarios"
    };
    private static readonly string[] PageSubtitles = {
        "MultiPizza · Dashboard de Ventas · 3 locales · agosto – octubre 2026",
        "Participación y resumen de ventas por local · 3 locales",
        "Top productos, presentaciones y tipos · agosto – octubre 2026",
        "Demanda por hora y turno · 12:00 – 21:00"
    };
    private static readonly string[] LocalNames = { "Todos", "Mexico", "Pasacana", "Universitaria" };
    private static readonly int[] PeriodMonths = { 0, 8, 9, 10 };
    private static readonly string[] PeriodNames = { "Todo el periodo", "Agosto 2026", "Septiembre 2026", "Octubre 2026" };

    private int pageIndex;
    private int localIndex;
    private int monthIndex;
    private double animation = 1.0;
    private double animationRaw = 1.0;
    private int metricCardIndex;
    private readonly Stopwatch animationClock = new Stopwatch();
    private readonly List<TooltipRegion> tooltipRegions = new List<TooltipRegion>();
    private Point mouseCanvas;
    private string currentTooltipKey;
    public ReportData Data = new ReportData();
    public bool Loading;
    public string ErrorMessage;
    public DateTime LastUpdated;
    public int PageIndex { get { return pageIndex; } set { pageIndex = Math.Max(0, Math.Min(3, value)); StartAnimation(); } }
    public int LocalIndex { get { return localIndex; } set { localIndex = Math.Max(0, Math.Min(3, value)); StartAnimation(); } }
    public int MonthIndex { get { return monthIndex; } set { monthIndex = Math.Max(0, Math.Min(3, value)); StartAnimation(); } }
    public event Action<int> PageSelected;
    public event Action<int> LocalSelected;
    public event Action<int> PeriodSelected;
    public event Action RefreshRequested;
    public event Action OpenPowerBIRequested;

    public DashboardCanvas()
    {
        BackColor = Color.White;
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Timer timer = new Timer();
        timer.Interval = 15;
        timer.Tick += delegate
        {
            if (animation < 1.0)
            {
                animationRaw = Math.Min(1.0, animationClock.Elapsed.TotalMilliseconds / 540.0);
                animation = EaseOut(animationRaw);
                if (animationRaw >= 1.0) animationClock.Stop();
                Invalidate();
            }
        };
        timer.Start();
    }

    public void StartAnimation()
    {
        animation = 0.0;
        animationRaw = 0.0;
        animationClock.Restart();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(Color.FromArgb(248, 250, 253));
        float scale = Math.Min((float)Width / CanvasWidth, (float)Height / CanvasHeight);
        float offsetX = (Width - CanvasWidth * scale) / 2F;
        float offsetY = (Height - CanvasHeight * scale) / 2F;
        e.Graphics.TranslateTransform(offsetX, offsetY);
        e.Graphics.ScaleTransform(scale, scale);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        DrawReport(e.Graphics);
    }

    private void DrawReport(Graphics g)
    {
        tooltipRegions.Clear();
        metricCardIndex = 0;
        using (Brush background = new SolidBrush(Color.White)) g.FillRectangle(background, 0, 0, CanvasWidth, CanvasHeight);
        DrawTabs(g);
        DrawHeader(g);
        if (pageIndex == 0) DrawSummary(g);
        else if (pageIndex == 1) DrawLocales(g);
        else if (pageIndex == 2) DrawProducts(g);
        else DrawSchedules(g);
        DrawGlobalFilters(g);
        if (!String.IsNullOrEmpty(ErrorMessage)) DrawError(g);
        DrawHoverTooltip(g);
    }

    private void DrawTabs(Graphics g)
    {
        for (int i = 0; i < 4; i++)
        {
            Rectangle r = new Rectangle(16 + i * 312, 3, 304, 40);
            FillRound(g, r, 9, i == pageIndex ? Color.FromArgb(18, 99, 214) : Color.FromArgb(244, 247, 253));
            using (Font font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold))
            using (Brush brush = new SolidBrush(i == pageIndex ? Color.White : Color.FromArgb(13, 110, 253)))
                DrawCentered(g, PageNames[i], font, brush, r);
        }
    }

    private void DrawHeader(Graphics g)
    {
        using (Brush white = new SolidBrush(Color.White)) g.FillRectangle(white, 0, 46, CanvasWidth, 57);
        using (Font titleFont = new Font("Segoe UI Semibold", pageIndex == 0 ? 14.2F : 17F, FontStyle.Bold))
        using (Brush titleBrush = new SolidBrush(Color.FromArgb(35, 45, 61)))
            g.DrawString(PageTitles[pageIndex], titleFont, titleBrush, 48, 65);
        using (Font subFont = new Font("Segoe UI", 9F))
        using (Brush subBrush = new SolidBrush(Color.FromArgb(93, 108, 131)))
            g.DrawString(GetSubtitle(), subFont, subBrush, 48, 87);
        using (Pen line = new Pen(Color.FromArgb(13, 110, 253), 2F)) g.DrawLine(line, 12, 106, 1268, 106);
        Rectangle open = new Rectangle(1113, 69, 143, 27);
        FillRound(g, open, 8, Color.FromArgb(235, 243, 255));
        using (Font f = new Font("Segoe UI Semibold", 8F, FontStyle.Bold))
        using (Brush b = new SolidBrush(Color.FromArgb(13, 96, 211))) DrawCentered(g, "↗  Abrir Power BI", f, b, open);
    }

    private string GetSubtitle()
    {
        if (Data == null || !Data.DateFrom.HasValue || !Data.DateTo.HasValue) return PageSubtitles[pageIndex];
        string range = Data.DateFrom.Value.ToString("dd/MM/yyyy") + " – " + Data.DateTo.Value.ToString("dd/MM/yyyy");
        if (pageIndex == 0) return "MultiPizza · Dashboard de Ventas · 3 locales · " + range;
        if (pageIndex == 1) return "Participación y resumen de ventas por local · " + range;
        if (pageIndex == 2) return "Top productos, presentaciones y tipos · " + range;
        return "Demanda por hora y turno · 12:00 – 21:00 · " + range;
    }

    private void DrawSummary(Graphics g)
    {
        DrawMetric(g, 16, 110, 300, 100, "VENTAS NETAS", "Ingresos totales del periodo (S/)", "S/ " + (Data.Sales * (decimal)animation / 1000M).ToString("0.00", CultureInfo.InvariantCulture) + " mil", SeriesColors[0], "S/", true);
        DrawMetric(g, 332, 110, 300, 100, "TICKET PROMEDIO", "Gasto medio por transacción (S/)", "S/ " + (Data.Average * (decimal)animation).ToString("0.00", CultureInfo.InvariantCulture), Color.FromArgb(0, 190, 220), "▤", true);
        DrawMetric(g, 648, 110, 300, 100, "UNIDADES VENDIDAS", "Cantidad de productos vendidos", (Data.Units * (decimal)animation / 1000M).ToString("0", CultureInfo.InvariantCulture) + " mil", Color.FromArgb(0, 190, 142), "◇", true);
        DrawMetric(g, 964, 110, 300, 100, "TICKETS", "Transacciones registradas en el OLTP", ((long)(Data.Tickets * animation)).ToString("#,##0", CultureInfo.InvariantCulture), Color.FromArgb(245, 166, 0), "▤", true);
        DrawStackedChart(g, new Rectangle(16, 222, 616, 346), "Ventas por Mes", Data.Monthly, false, 200000M, true, true);
        DrawStackedChart(g, new Rectangle(648, 222, 616, 346), "Ventas por Día", Data.Weekdays, false, 60000M, true, true);
    }

    private void DrawProducts(Graphics g)
    {
        DrawStackedChart(g, new Rectangle(16, 110, 616, 224), "Top 10 Productos (S/)", Data.Products, true, 0, true, false);
        DrawStackedChart(g, new Rectangle(648, 110, 616, 224), "Top 10 Producto · Presentación (S/)", Data.Presentations, true, 0, true, false);
        DrawStackedChart(g, new Rectangle(16, 342, 1248, 226), "Top 10 Tipos de Presentación (S/)", Data.Types, true, 0, true, false);
    }

    private void DrawLocales(Graphics g)
    {
        DrawDonut(g, new Rectangle(16, 110, 616, 458));
        DrawLocalTable(g, new Rectangle(648, 110, 616, 458));
    }

    private void DrawSchedules(Graphics g)
    {
        DrawStackedChart(g, new Rectangle(16, 110, 1248, 220), "Ventas por Hora (S/)", Data.Hours, false, 0, true, false);
        DrawStackedChart(g, new Rectangle(16, 342, 616, 226), "Ventas por Turno (S/)", Data.Shifts, true, 0, true, false);
        DrawMetric(g, 648, 342, 300, 226, "HORA PUNTA", "Horario con mayores ventas", Data.PeakHour, Color.FromArgb(13, 110, 253), "◷", false);
        DrawMetric(g, 964, 342, 300, 226, "% VENTAS NOCHE", "Participación del turno noche", (Data.NightShare * 100M * (decimal)animation).ToString("0.0", CultureInfo.InvariantCulture) + "%", Color.FromArgb(13, 110, 253), "◒", false);
    }

    private void DrawMetric(Graphics g, int x, int y, int width, int height, string label, string description, string value, Color accent, string icon, bool compact)
    {
        double cardProgress = StaggeredProgress(metricCardIndex++);
        GraphicsState state = g.Save();
        float scale = (float)(0.92 + 0.08 * cardProgress);
        float lift = (float)((1.0 - cardProgress) * 13.0);
        g.TranslateTransform(x + width / 2F, y + height / 2F + lift);
        g.ScaleTransform(scale, scale);
        g.TranslateTransform(-(x + width / 2F), -(y + height / 2F));
        Rectangle r = new Rectangle(x, y, width, height);
        DrawCard(g, r);
        using (Font small = new Font("Segoe UI Semibold", compact ? 8F : 9F, FontStyle.Bold))
        using (Brush muted = new SolidBrush(Color.FromArgb(103, 116, 138))) g.DrawString(label, small, muted, x + 14, y + 12);
        using (Font desc = new Font("Segoe UI", compact ? 7.5F : 9F))
        using (Brush gray = new SolidBrush(Color.FromArgb(106, 120, 144))) g.DrawString(description, desc, gray, x + 14, y + 31);
        int bubbleSize = compact ? 30 : 40;
        Rectangle bubble = new Rectangle(x + width - bubbleSize - 15, y + 12, bubbleSize, bubbleSize);
        using (Brush pale = new SolidBrush(Color.FromArgb(26, accent))) g.FillEllipse(pale, bubble);
        using (Font iconFont = new Font("Segoe UI Semibold", compact ? 13F : 16F, FontStyle.Bold))
        using (Brush iconBrush = new SolidBrush(accent)) DrawCentered(g, icon, iconFont, iconBrush, bubble);
        using (Font valueFont = new Font("Segoe UI Semibold", compact ? 26F : 30F, FontStyle.Bold))
        using (Brush valueBrush = new SolidBrush(accent))
        {
            Rectangle valueRect = compact ? new Rectangle(x + 10, y + 52, width - 20, height - 58) : new Rectangle(x + 12, y + 67, width - 24, 53);
            DrawCentered(g, value, valueFont, valueBrush, valueRect);
        }
        g.Restore(state);
        tooltipRegions.Add(TooltipRegion.ForInfo(new RectangleF(x, y, width, height), label, description, MetricDefinition(label), "Valor actual: " + MetricExactValue(label)));
    }

    private string MetricDefinition(string label)
    {
        if (label == "VENTAS NETAS") return "Suma de los subtotales con los filtros actuales.";
        if (label == "TICKET PROMEDIO") return "Ventas netas divididas entre tickets únicos.";
        if (label == "UNIDADES VENDIDAS") return "Suma de las cantidades de productos vendidos.";
        if (label == "TICKETS") return "Conteo distinto de ventas (id_venta).";
        if (label == "HORA PUNTA") return "Franja horaria con mayores ventas netas.";
        if (label == "% VENTAS NOCHE") return "Ventas desde las 19:00 divididas entre ventas netas.";
        return "Métrica calculada con los filtros actuales.";
    }

    private string MetricExactValue(string label)
    {
        if (label == "VENTAS NETAS") return "S/ " + Data.Sales.ToString("#,##0.00", CultureInfo.InvariantCulture);
        if (label == "TICKET PROMEDIO") return "S/ " + Data.Average.ToString("0.00", CultureInfo.InvariantCulture);
        if (label == "UNIDADES VENDIDAS") return Data.Units.ToString("#,##0", CultureInfo.InvariantCulture) + " unidades";
        if (label == "TICKETS") return Data.Tickets.ToString("#,##0", CultureInfo.InvariantCulture);
        if (label == "HORA PUNTA") return Data.PeakHour;
        if (label == "% VENTAS NOCHE") return (Data.NightShare * 100M).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        return "";
    }

    private void DrawStackedChart(Graphics g, Rectangle rect, string title, List<ChartValue> points, bool horizontal, decimal fixedMax, bool legend, bool segmentLabels)
    {
        DrawCard(g, rect);
        using (Font titleFont = new Font("Segoe UI Semibold", 11F, FontStyle.Bold))
        using (Brush titleBrush = new SolidBrush(Color.FromArgb(50, 62, 80))) g.DrawString(title, titleFont, titleBrush, rect.X + 18, rect.Y + 13);
        List<string> categories = points.Select(p => p.Category).Distinct().ToList();
        categories.Sort(delegate(string a, string b)
        {
            int ia = points.First(p => p.Category == a).Order;
            int ib = points.First(p => p.Category == b).Order;
            return ia.CompareTo(ib);
        });
        List<string> series = points.Select(p => p.Series).Distinct().ToList();
        series.Sort(delegate(string a, string b) { return SeriesOrder(a).CompareTo(SeriesOrder(b)); });
        if (legend) DrawLegend(g, series, rect.X + 18, rect.Y + 40);
        if (categories.Count == 0)
        {
            using (Font f = new Font("Segoe UI", 9F)) using (Brush b = new SolidBrush(Color.Gray))
                DrawCentered(g, "Sin datos para los filtros seleccionados", f, b, new Rectangle(rect.X + 12, rect.Y + 60, rect.Width - 24, rect.Height - 76));
            return;
        }
        if (horizontal) DrawHorizontalBars(g, rect, title, points, categories, series, fixedMax, legend);
        else DrawColumns(g, rect, title, points, categories, series, fixedMax, legend, segmentLabels);
    }

    private void DrawColumns(Graphics g, Rectangle rect, string title, List<ChartValue> points, List<string> categories, List<string> series, decimal fixedMax, bool legend, bool segmentLabels)
    {
        int plotLeft = rect.X + 60;
        int plotRight = rect.Right - 20;
        int plotTop = rect.Y + (legend ? 84 : 49);
        int plotBottom = rect.Bottom - 48;
        if (plotBottom <= plotTop) plotBottom = plotTop + 30;
        decimal maximum = fixedMax > 0 ? fixedMax : RoundAxis(GetMaxTotal(points));
        DrawGrid(g, plotLeft, plotTop, plotRight, plotBottom, maximum, 4, true);
        float slot = (float)(plotRight - plotLeft) / categories.Count;
        float barWidth = Math.Min(58F, slot * 0.53F);
        for (int ci = 0; ci < categories.Count; ci++)
        {
            float cx = plotLeft + ci * slot + slot / 2F;
            float barX = cx - barWidth / 2F;
            float bottom = plotBottom;
            foreach (string sn in series)
            {
                ChartValue value = points.FirstOrDefault(p => p.Category == categories[ci] && p.Series == sn);
                if (value == null || value.Value <= 0) continue;
                float h = (float)(value.Value / maximum) * (plotBottom - plotTop) * (float)animation;
                if (h <= 0.5F) continue;
                RectangleF bar = new RectangleF(barX, bottom - h, barWidth, h);
                using (LinearGradientBrush brush = new LinearGradientBrush(bar, Lighten(SeriesColor(sn), 16), SeriesColor(sn), 90F)) g.FillRectangle(brush, bar);
                tooltipRegions.Add(TooltipRegion.ForBar(bar, title, categories[ci], sn, value.Value));
                if (segmentLabels && h > 19 && barWidth > 45)
                {
                    string label = "S/ " + Math.Round(value.Value / 1000M, 0).ToString("0", CultureInfo.InvariantCulture) + " mil";
                    using (Font f = new Font("Segoe UI Semibold", 7.2F, FontStyle.Bold))
                    using (Brush b = new SolidBrush(Color.White)) DrawCentered(g, label, f, b, new RectangleF(barX, bottom - h / 2F - 8, barWidth, 16));
                }
                bottom -= h;
            }
            using (Font f = new Font("Segoe UI", 7.2F))
            using (Brush b = new SolidBrush(Color.FromArgb(91, 105, 128)))
                DrawCentered(g, ShortCategory(categories[ci]), f, b, new RectangleF(cx - slot / 2, plotBottom + 7, slot, 24));
        }
        using (Pen axis = new Pen(Color.FromArgb(222, 229, 238))) g.DrawLine(axis, plotLeft, plotBottom, plotRight, plotBottom);
    }

    private void DrawHorizontalBars(Graphics g, Rectangle rect, string title, List<ChartValue> points, List<string> categories, List<string> series, decimal fixedMax, bool legend)
    {
        int labelWidth = rect.Width > 850 ? 205 : 170;
        int plotLeft = rect.X + labelWidth;
        int plotRight = rect.Right - 18;
        int plotTop = rect.Y + (legend ? 63 : 42);
        int plotBottom = rect.Bottom - 30;
        if (plotBottom <= plotTop) plotBottom = plotTop + 20;
        decimal max = fixedMax > 0 ? fixedMax : RoundAxis(GetMaxTotal(points));
        DrawHorizontalGrid(g, plotLeft, plotTop, plotRight, plotBottom, max, 3);
        float slot = (float)(plotBottom - plotTop) / categories.Count;
        float barHeight = Math.Max(5F, Math.Min(13F, slot * 0.68F));
        for (int ci = 0; ci < categories.Count; ci++)
        {
            float cy = plotTop + ci * slot + slot / 2F;
            string label = Ellipsize(g, categories[ci], new Font("Segoe UI", 7.3F), labelWidth - 15);
            using (Font f = new Font("Segoe UI", 7.3F))
            using (Brush b = new SolidBrush(Color.FromArgb(80, 94, 115)))
                g.DrawString(label, f, b, rect.X + 13, cy - 7);
            float currentX = plotLeft;
            foreach (string sn in series)
            {
                ChartValue value = points.FirstOrDefault(p => p.Category == categories[ci] && p.Series == sn);
                if (value == null || value.Value <= 0) continue;
                float width = (float)(value.Value / max) * (plotRight - plotLeft) * (float)animation;
                if (width <= 0.5F) continue;
                RectangleF bar = new RectangleF(currentX, cy - barHeight / 2F, width, barHeight);
                using (LinearGradientBrush brush = new LinearGradientBrush(bar, Lighten(SeriesColor(sn), 16), SeriesColor(sn), 0F)) g.FillRectangle(brush, bar);
                tooltipRegions.Add(TooltipRegion.ForBar(bar, title, categories[ci], sn, value.Value));
                currentX += width;
            }
        }
    }

    private void DrawHorizontalGrid(Graphics g, int left, int top, int right, int bottom, decimal max, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            float x = left + (right - left) * i / (float)steps;
            using (Pen pen = new Pen(Color.FromArgb(224, 231, 240)))
            {
                pen.DashPattern = new float[] { 1F, 3F };
                g.DrawLine(pen, x, top, x, bottom);
            }
            string label = FormatAxis(max * i / steps);
            using (Font f = new Font("Segoe UI", 6.8F))
            using (Brush b = new SolidBrush(Color.FromArgb(100, 113, 136)))
                DrawCentered(g, label, f, b, new RectangleF(x - 34, bottom + 3, 68, 14));
        }
    }

    private void DrawGrid(Graphics g, int left, int top, int right, int bottom, decimal max, int steps, bool verticalChart)
    {
        for (int i = 0; i <= steps; i++)
        {
            float y = bottom - (bottom - top) * i / (float)steps;
            using (Pen pen = new Pen(Color.FromArgb(224, 231, 240)))
            {
                pen.DashPattern = new float[] { 1F, 3F };
                g.DrawLine(pen, left, y, right, y);
            }
            string label = FormatAxis(max * i / steps);
            using (Font f = new Font("Segoe UI", 7.2F))
            using (Brush b = new SolidBrush(Color.FromArgb(100, 113, 136)))
            {
                SizeF s = g.MeasureString(label, f);
                g.DrawString(label, f, b, left - s.Width - 7, y - 7);
            }
        }
        if (verticalChart) using (Pen axis = new Pen(Color.FromArgb(222, 229, 238))) g.DrawLine(axis, left, top, left, bottom);
    }

    private void DrawXAxisLabels(Graphics g, int left, int right, int y, decimal max, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            int x = left + (right - left) * i / steps;
            using (Font f = new Font("Segoe UI", 6.8F))
            using (Brush b = new SolidBrush(Color.FromArgb(100, 113, 136)))
                DrawCentered(g, FormatAxis(max * i / steps), f, b, new Rectangle(x - 27, y, 54, 15));
        }
    }

    private void DrawLegend(Graphics g, List<string> series, int x, int y)
    {
        int dx = x;
        using (Font font = new Font("Segoe UI", 7.2F))
        using (Brush textBrush = new SolidBrush(Color.FromArgb(105, 118, 140)))
        {
            foreach (string name in series)
            {
                using (Brush swatch = new SolidBrush(SeriesColor(name))) g.FillEllipse(swatch, dx, y + 3, 9, 9);
                string label = name == "Mexico" ? "México" : name;
                g.DrawString(label, font, textBrush, dx + 13, y);
                dx += 20 + (int)g.MeasureString(label, font).Width;
            }
        }
    }

    private void DrawDonut(Graphics g, Rectangle rect)
    {
        DrawCard(g, rect);
        using (Font title = new Font("Segoe UI Semibold", 12F, FontStyle.Bold))
        using (Brush b = new SolidBrush(Color.FromArgb(50, 62, 80))) g.DrawString("Participación por Local", title, b, rect.X + 20, rect.Y + 18);
        decimal total = Data.Locals.Sum(x => x.Sales);
        if (total <= 0) return;
        Rectangle ring = new Rectangle(rect.X + 65, rect.Y + 102, 255, 255);
        float start = -90F;
        int sliceIndex = 0;
        foreach (LocalMetric local in Data.Locals)
        {
            float sweep = (float)(local.Sales / total * 360M);
            float shownSweep = sweep * (float)StaggeredProgress(sliceIndex++);
            if (shownSweep > 0.2F)
                using (Brush b = new SolidBrush(SeriesColor(local.Name))) g.FillPie(b, ring, start, shownSweep);
            string displayName = local.Name == "Mexico" ? "México" : local.Name;
            string money = "Ventas netas: S/ " + local.Sales.ToString("#,##0.00", CultureInfo.InvariantCulture);
            string share = "Participación: " + (local.Share * 100M).ToString("0.0", CultureInfo.InvariantCulture) + "%";
            if (shownSweep > 0.2F) tooltipRegions.Add(TooltipRegion.ForDonut(ring, 79F, start, shownSweep, "Participación por Local", displayName, share, money));
            start += sweep;
        }
        Rectangle inner = new Rectangle(ring.X + 48, ring.Y + 48, ring.Width - 96, ring.Height - 96);
        using (Brush b = new SolidBrush(Color.White)) g.FillEllipse(b, inner);
        using (Font totalFont = new Font("Segoe UI Semibold", 15F, FontStyle.Bold))
        using (Brush b = new SolidBrush(Color.FromArgb(36, 49, 69)))
            DrawCentered(g, "S/ " + (total / 1000M).ToString("0.0", CultureInfo.InvariantCulture) + " mil", totalFont, b, new Rectangle(inner.X, inner.Y + 29, inner.Width, 26));
        using (Font caption = new Font("Segoe UI", 8F))
        using (Brush b = new SolidBrush(Color.FromArgb(108, 122, 144)))
            DrawCentered(g, "Ventas netas", caption, b, new Rectangle(inner.X, inner.Y + 57, inner.Width, 20));

        int ly = rect.Y + 157;
        foreach (LocalMetric local in Data.Locals)
        {
            using (Brush swatch = new SolidBrush(SeriesColor(local.Name))) g.FillEllipse(swatch, rect.X + 365, ly + 4, 11, 11);
            string displayName = local.Name == "Mexico" ? "México" : local.Name;
            tooltipRegions.Add(TooltipRegion.ForBar(new RectangleF(rect.X + 360, ly - 3, 220, 46), "Participación por Local", displayName, "", local.Sales, "Participación: " + (local.Share * 100M).ToString("0.0", CultureInfo.InvariantCulture) + "%"));
            using (Font nameFont = new Font("Segoe UI Semibold", 9F, FontStyle.Bold))
            using (Brush nameBrush = new SolidBrush(Color.FromArgb(49, 62, 82))) g.DrawString(displayName, nameFont, nameBrush, rect.X + 385, ly);
            using (Font valueFont = new Font("Segoe UI", 8F))
            using (Brush valueBrush = new SolidBrush(Color.FromArgb(105, 118, 140)))
                g.DrawString("S/ " + (local.Sales / 1000M).ToString("0.0", CultureInfo.InvariantCulture) + " mil   ·   " + (local.Share * 100M).ToString("0.0", CultureInfo.InvariantCulture) + "%", valueFont, valueBrush, rect.X + 385, ly + 20);
            ly += 58;
        }
    }

    private void DrawLocalTable(Graphics g, Rectangle rect)
    {
        DrawCard(g, rect);
        using (Font title = new Font("Segoe UI Semibold", 12F, FontStyle.Bold))
        using (Brush b = new SolidBrush(Color.FromArgb(50, 62, 80))) g.DrawString("Resumen por Local", title, b, rect.X + 20, rect.Y + 18);
        string[] headers = { "LOCAL", "VENTAS NETAS", "TICKETS", "TICKET PROM.", "UNIDADES", "% TOTAL" };
        int[] widths = { 112, 117, 75, 105, 88, 75 };
        int x = rect.X + 17;
        int y = rect.Y + 70;
        using (Brush headerBg = new SolidBrush(Color.FromArgb(243, 246, 251))) g.FillRectangle(headerBg, x, y, widths.Sum(), 34);
        int cursor = x;
        using (Font f = new Font("Segoe UI Semibold", 7F, FontStyle.Bold))
        using (Brush b = new SolidBrush(Color.FromArgb(105, 118, 140)))
        {
            for (int i = 0; i < headers.Length; i++) { g.DrawString(headers[i], f, b, cursor + 5, y + 11); cursor += widths[i]; }
        }
        int rowY = y + 34;
        int rowIndex = 0;
        foreach (LocalMetric local in Data.Locals)
        {
            int rowOffset = (int)((1.0 - StaggeredProgress(rowIndex + 1)) * 11.0);
            int animatedRowY = rowY + rowOffset;
            using (Brush bg = new SolidBrush(rowIndex % 2 == 0 ? Color.White : Color.FromArgb(250, 251, 253))) g.FillRectangle(bg, x, animatedRowY, widths.Sum(), 68);
            using (Pen line = new Pen(Color.FromArgb(232, 236, 242))) g.DrawLine(line, x, animatedRowY + 67, x + widths.Sum(), animatedRowY + 67);
            string[] cells = {
                local.Name == "Mexico" ? "México" : local.Name,
                "S/ " + (local.Sales / 1000M).ToString("0.0", CultureInfo.InvariantCulture) + " mil",
                local.Tickets.ToString("#,##0", CultureInfo.InvariantCulture),
                "S/ " + local.Average.ToString("0.00", CultureInfo.InvariantCulture),
                local.Units.ToString("#,##0", CultureInfo.InvariantCulture),
                (local.Share * 100M).ToString("0.0", CultureInfo.InvariantCulture) + "%"
            };
            cursor = x;
            for (int i = 0; i < cells.Length; i++)
            {
                using (Font f = new Font(i == 0 ? "Segoe UI Semibold" : "Segoe UI", i == 0 ? 8F : 7.5F, i == 0 ? FontStyle.Bold : FontStyle.Regular))
                using (Brush b = new SolidBrush(Color.FromArgb(56, 68, 87)))
                    g.DrawString(Ellipsize(g, cells[i], f, widths[i] - 8), f, b, cursor + 5, animatedRowY + 26);
                cursor += widths[i];
            }
            rowY += 68;
            rowIndex++;
        }
        using (Font note = new Font("Segoe UI", 8F))
        using (Brush b = new SolidBrush(Color.FromArgb(108, 122, 144)))
            g.DrawString("Importes en soles · participación calculada sobre el total filtrado", note, b, rect.X + 20, rect.Bottom - 39);
    }

    private void DrawGlobalFilters(Graphics g)
    {
        using (Brush footer = new SolidBrush(Color.FromArgb(250, 251, 253))) g.FillRectangle(footer, 0, 588, CanvasWidth, 132);
        DrawCard(g, new Rectangle(16, 596, 300, 104));
        using (Font localTitle = new Font("Segoe UI Semibold", 10F, FontStyle.Bold))
        using (Brush blue = new SolidBrush(Color.FromArgb(13, 110, 253))) g.DrawString("Local", localTitle, blue, 35, 608);
        Rectangle localBox = new Rectangle(32, 630, 268, 30);
        using (Pen border = new Pen(Color.FromArgb(225, 229, 236)))
        using (Brush white = new SolidBrush(Color.White)) { g.FillRectangle(white, localBox); g.DrawRectangle(border, localBox); }
        using (Font f = new Font("Segoe UI", 9F))
        using (Brush b = new SolidBrush(Color.FromArgb(54, 64, 80))) g.DrawString(LocalDisplay(), f, b, localBox.X + 10, localBox.Y + 6);
        DrawChevron(g, localBox.Right - 19, localBox.Y + 11);

        DrawCard(g, new Rectangle(332, 596, 338, 104));
        using (Font periodTitle = new Font("Segoe UI Semibold", 10F, FontStyle.Bold))
        using (Brush blue = new SolidBrush(Color.FromArgb(13, 110, 253))) g.DrawString("Periodo", periodTitle, blue, 351, 608);
        Rectangle dateBox = new Rectangle(350, 630, 302, 30);
        using (Pen border = new Pen(Color.FromArgb(225, 229, 236)))
        using (Brush white = new SolidBrush(Color.White)) { g.FillRectangle(white, dateBox); g.DrawRectangle(border, dateBox); }
        using (Font f = new Font("Segoe UI", 8.5F))
        using (Brush b = new SolidBrush(Color.FromArgb(54, 64, 80))) g.DrawString(DateRangeText(), f, b, dateBox.X + 8, dateBox.Y + 7);
        DrawCalendar(g, dateBox.Right - 24, dateBox.Y + 7);
        int sliderLeft = 365, sliderRight = 616, sliderY = 681;
        using (Pen line = new Pen(Color.FromArgb(170, 175, 184), 3F)) g.DrawLine(line, sliderLeft, sliderY, sliderRight, sliderY);
        int leftHandle = monthIndex == 0 ? sliderLeft : sliderLeft + (monthIndex - 1) * 55;
        int rightHandle = monthIndex == 0 ? sliderRight : Math.Min(sliderRight, leftHandle + 75);
        using (Brush selected = new SolidBrush(Color.FromArgb(13, 110, 253))) using (Pen p = new Pen(Color.White, 2F))
        {
            g.FillEllipse(selected, leftHandle - 7, sliderY - 8, 16, 16); g.DrawEllipse(p, leftHandle - 7, sliderY - 8, 16, 16);
            g.FillEllipse(selected, rightHandle - 7, sliderY - 8, 16, 16); g.DrawEllipse(p, rightHandle - 7, sliderY - 8, 16, 16);
        }

        using (Font note = new Font("Segoe UI", 8.2F))
        using (Brush gray = new SolidBrush(Color.FromArgb(96, 110, 132)))
        {
            g.DrawString("Filtros globales · se sincronizan en todas las pestañas", note, gray, 694, 606);
            string state = Loading ? "Actualizando datos…" : (String.IsNullOrEmpty(ErrorMessage) ? "SQL Server · " + (LastUpdated == DateTime.MinValue ? "conectando" : "actualizado " + LastUpdated.ToString("HH:mm")) : "Sin conexión a SQL Server");
            g.DrawString(state, note, gray, 694, 628);
        }
        Rectangle refresh = new Rectangle(1080, 604, 88, 30);
        FillRound(g, refresh, 7, Color.FromArgb(238, 245, 255));
        using (Font f = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold))
        using (Brush b = new SolidBrush(Color.FromArgb(13, 96, 211))) DrawCentered(g, "↻  Actualizar", f, b, refresh);
        Rectangle open = new Rectangle(1080, 643, 184, 38);
        FillRound(g, open, 8, Color.FromArgb(13, 110, 253));
        using (Font f = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold))
        using (Brush b = new SolidBrush(Color.White)) DrawCentered(g, "Abrir página en Power BI  ↗", f, b, open);
    }

    private void DrawError(Graphics g)
    {
        Rectangle r = new Rectangle(750, 109, 514, 43);
        FillRound(g, r, 8, Color.FromArgb(255, 244, 238));
        using (Font f = new Font("Segoe UI", 8F))
        using (Brush b = new SolidBrush(Color.FromArgb(166, 73, 42)))
            g.DrawString("No se pudo consultar la base de datos. Comprueba que SQL Server esté encendido.", f, b, r.X + 12, r.Y + 13);
    }

    private void DrawHoverTooltip(Graphics g)
    {
        if (String.IsNullOrEmpty(currentTooltipKey)) return;
        TooltipRegion hit = tooltipRegions.FirstOrDefault(r => r.Key == currentTooltipKey);
        if (hit == null) return;
        int width = 278;
        int height = String.IsNullOrEmpty(hit.Detail) ? 62 : 76;
        int x = mouseCanvas.X + 18;
        int y = mouseCanvas.Y + 18;
        if (x + width > CanvasWidth - 8) x = mouseCanvas.X - width - 16;
        if (y + height > CanvasHeight - 8) y = mouseCanvas.Y - height - 16;
        x = Math.Max(8, x); y = Math.Max(8, y);
        Rectangle box = new Rectangle(x, y, width, height);
        FillRound(g, box, 9, Color.FromArgb(248, 25, 37, 57));
        using (Font title = new Font("Segoe UI Semibold", 8F, FontStyle.Bold))
        using (Brush white = new SolidBrush(Color.White))
            g.DrawString(Ellipsize(g, hit.Heading, title, width - 24), title, white, x + 12, y + 8);
        using (Font label = new Font("Segoe UI Semibold", 8F, FontStyle.Bold))
        using (Brush pale = new SolidBrush(Color.FromArgb(221, 231, 245)))
            g.DrawString(Ellipsize(g, hit.Label, label, width - 24), label, pale, x + 12, y + 25);
        if (!String.IsNullOrEmpty(hit.Detail))
        {
            using (Font detail = new Font("Segoe UI", 7.2F))
            using (Brush gray = new SolidBrush(Color.FromArgb(188, 202, 220)))
                g.DrawString(Ellipsize(g, hit.Detail, detail, width - 24), detail, gray, x + 12, y + 41);
        }
        using (Font value = new Font("Segoe UI Semibold", 8F, FontStyle.Bold))
        using (Brush blue = new SolidBrush(Color.FromArgb(117, 184, 255)))
            g.DrawString(Ellipsize(g, hit.ValueText, value, width - 24), value, blue, x + 12, y + height - 20);
    }

    private void OnLocalClick()
    {
        LocalIndex = (localIndex + 1) % LocalNames.Length;
        if (LocalSelected != null) LocalSelected(localIndex);
    }

    private void OnPeriodClick()
    {
        MonthIndex = (monthIndex + 1) % PeriodMonths.Length;
        if (PeriodSelected != null) PeriodSelected(monthIndex);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Point p = ToCanvas(e.Location);
        if (p.Y >= 3 && p.Y <= 44)
        {
            for (int i = 0; i < 4; i++) if (p.X >= 16 + i * 312 && p.X <= 320 + i * 312)
            {
                PageIndex = i;
                if (PageSelected != null) PageSelected(i);
                return;
            }
        }
        if (p.X >= 1113 && p.X <= 1256 && p.Y >= 69 && p.Y <= 96)
        {
            if (OpenPowerBIRequested != null) OpenPowerBIRequested();
            return;
        }
        if (p.Y >= 596 && p.Y <= 700 && p.X >= 16 && p.X <= 316) { OnLocalClick(); return; }
        if (p.Y >= 596 && p.Y <= 700 && p.X >= 332 && p.X <= 670) { OnPeriodClick(); return; }
        if (p.X >= 1080 && p.X <= 1168 && p.Y >= 604 && p.Y <= 634)
        {
            if (RefreshRequested != null) RefreshRequested();
            return;
        }
        if (p.X >= 1080 && p.X <= 1264 && p.Y >= 643 && p.Y <= 681 && OpenPowerBIRequested != null) OpenPowerBIRequested();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Point p = ToCanvas(e.Location);
        TooltipRegion hit = tooltipRegions.LastOrDefault(r => r.Contains(p));
        string nextKey = hit == null ? null : hit.Key;
        if (nextKey != currentTooltipKey || (hit != null && p != mouseCanvas))
        {
            currentTooltipKey = nextKey;
            mouseCanvas = p;
            Invalidate();
        }
        bool hand = (p.Y <= 44 && p.Y >= 3 && p.X >= 16 && p.X <= 1264) ||
                    (p.X >= 1113 && p.X <= 1256 && p.Y >= 69 && p.Y <= 96) ||
                    (p.Y >= 596 && p.Y <= 700 && p.X >= 16 && p.X <= 670) ||
                    (p.X >= 1080 && p.X <= 1264 && p.Y >= 604 && p.Y <= 681);
        Cursor = hit != null ? Cursors.Help : (hand ? Cursors.Hand : Cursors.Default);
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (currentTooltipKey != null) { currentTooltipKey = null; Invalidate(); }
        base.OnMouseLeave(e);
    }

    private Point ToCanvas(Point p)
    {
        float scale = Math.Min((float)Width / CanvasWidth, (float)Height / CanvasHeight);
        float ox = (Width - CanvasWidth * scale) / 2F;
        float oy = (Height - CanvasHeight * scale) / 2F;
        return new Point((int)((p.X - ox) / scale), (int)((p.Y - oy) / scale));
    }

    private string LocalDisplay() { return localIndex == 1 ? "México" : LocalNames[localIndex]; }

    private string DateRangeText()
    {
        if (Data == null || !Data.DateFrom.HasValue || !Data.DateTo.HasValue) return PeriodNames[monthIndex];
        return Data.DateFrom.Value.ToString("dd/MM/yyyy") + "     " + Data.DateTo.Value.ToString("dd/MM/yyyy");
    }

    private static void DrawCard(Graphics g, Rectangle r)
    {
        Rectangle shadow = new Rectangle(r.X + 1, r.Y + 3, r.Width - 2, r.Height - 1);
        using (GraphicsPath shadowPath = RoundedPath(shadow, 12))
        using (Brush shadowBrush = new SolidBrush(Color.FromArgb(22, 30, 45, 65))) g.FillPath(shadowBrush, shadowPath);
        using (GraphicsPath path = RoundedPath(r, 12))
        using (Brush fill = new SolidBrush(Color.White))
        using (Pen border = new Pen(Color.FromArgb(225, 231, 239)))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }
    }

    private static void FillRound(Graphics g, Rectangle r, int radius, Color color)
    {
        using (GraphicsPath path = RoundedPath(r, radius)) using (Brush b = new SolidBrush(color)) g.FillPath(b, path);
    }

    private static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        GraphicsPath path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }

    private static void DrawCentered(Graphics g, string text, Font font, Brush brush, Rectangle r)
    {
        StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter;
        g.DrawString(text, font, brush, r, sf); sf.Dispose();
    }
    private static void DrawCentered(Graphics g, string text, Font font, Brush brush, RectangleF r)
    {
        StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter;
        g.DrawString(text, font, brush, r, sf); sf.Dispose();
    }

    private static Color SeriesColor(string name)
    {
        if (name == "Mexico") return SeriesColors[0];
        if (name == "Pasacana") return SeriesColors[1];
        if (name == "Universitaria") return SeriesColors[2];
        return Color.FromArgb(13, 110, 253);
    }
    private static int SeriesOrder(string name) { return name == "Mexico" ? 0 : name == "Pasacana" ? 1 : name == "Universitaria" ? 2 : 3; }
    private static Color Lighten(Color c, int amount) { return Color.FromArgb(Math.Min(255, c.R + amount), Math.Min(255, c.G + amount), Math.Min(255, c.B + amount)); }
    private double StaggeredProgress(int index)
    {
        double delay = Math.Min(0.24, Math.Max(0, index) * 0.08);
        double t = Math.Max(0.0, Math.Min(1.0, (animationRaw - delay) / (1.0 - delay)));
        return EaseOut(t);
    }
    private static double EaseOut(double t) { return 1.0 - Math.Pow(1.0 - t, 3.0); }
    private static decimal GetMaxTotal(List<ChartValue> values)
    {
        decimal max = 0;
        foreach (var group in values.GroupBy(v => v.Category)) { decimal total = group.Sum(v => v.Value); if (total > max) max = total; }
        return max;
    }
    private static decimal RoundAxis(decimal value)
    {
        if (value <= 0) return 1;
        decimal unit = value >= 100000M ? 50000M : value >= 10000M ? 10000M : value >= 1000M ? 1000M : 100M;
        return Math.Ceiling(value / unit) * unit;
    }
    private static string FormatAxis(decimal v)
    {
        if (v <= 0) return "S/ 0 mil";
        return "S/ " + (v / 1000M).ToString("0", CultureInfo.InvariantCulture) + " mil";
    }
    private static string ShortCategory(string value)
    {
        if (value == "miércoles") return "miérc.";
        if (value == "sábado") return "sábado";
        if (value == "domingo") return "domingo";
        if (value == "Almuerzo (12-15)") return "Almuerzo";
        if (value == "Tarde (16-18)") return "Tarde";
        if (value == "Noche (19-21)") return "Noche";
        return value;
    }
    private static string Ellipsize(Graphics g, string text, Font font, int width)
    {
        if (g.MeasureString(text, font).Width <= width) return text;
        string value = text;
        while (value.Length > 3 && g.MeasureString(value + "…", font).Width > width) value = value.Substring(0, value.Length - 1);
        return value + "…";
    }
    private static void DrawChevron(Graphics g, int x, int y)
    {
        using (Pen p = new Pen(Color.FromArgb(105, 116, 133), 1.5F)) { g.DrawLine(p, x, y, x + 5, y + 5); g.DrawLine(p, x + 5, y + 5, x + 10, y); }
    }
    private static void DrawCalendar(Graphics g, int x, int y)
    {
        using (Pen p = new Pen(Color.FromArgb(85, 101, 124), 1F))
        {
            g.DrawRectangle(p, x, y, 14, 13); g.DrawLine(p, x, y + 4, x + 14, y + 4);
            g.DrawLine(p, x + 4, y - 1, x + 4, y + 2); g.DrawLine(p, x + 10, y - 1, x + 10, y + 2);
        }
    }
}
