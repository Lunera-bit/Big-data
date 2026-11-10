using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal static class ProjectPicker
{
    private static string SettingsFile
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultiPizzaLauncher", "last-project.txt"); }
    }

    public static string SelectProject()
    {
        string lastPath = null;
        try { if (File.Exists(SettingsFile)) lastPath = File.ReadAllText(SettingsFile, Encoding.UTF8).Trim(); }
        catch { }

        while (true)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Selecciona el proyecto Power BI de MultiPizza";
                dialog.Filter = "Proyecto Power BI (*.pbip)|*.pbip";
                dialog.DefaultExt = "pbip";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                dialog.RestoreDirectory = true;
                if (!String.IsNullOrEmpty(lastPath) && File.Exists(lastPath))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(lastPath);
                    dialog.FileName = Path.GetFileName(lastPath);
                }
                else
                {
                    dialog.InitialDirectory = Directory.Exists(@"E:\") ? @"E:\" : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    dialog.FileName = "POWER BI.pbip";
                }

                if (dialog.ShowDialog() != DialogResult.OK) return null;
                string metadata = GetPagesMetadataPath(dialog.FileName);
                if (metadata != null)
                {
                    try
                    {
                        string settingsDirectory = Path.GetDirectoryName(SettingsFile);
                        Directory.CreateDirectory(settingsDirectory);
                        File.WriteAllText(SettingsFile, dialog.FileName, Encoding.UTF8);
                    }
                    catch { }
                    return dialog.FileName;
                }
                MessageBox.Show("No pude encontrar las páginas del informe junto al .pbip. Elige el archivo de proyecto correcto y verifica que su carpeta .Report esté en el mismo lugar.", "Proyecto Power BI incompleto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                lastPath = null;
            }
        }
    }

    public static string GetPagesMetadataPath(string projectPath)
    {
        try
        {
            if (String.IsNullOrEmpty(projectPath) || !File.Exists(projectPath)) return null;
            string pbip = File.ReadAllText(projectPath, Encoding.UTF8);
            Match match = Regex.Match(pbip, "\"report\"\\s*:\\s*\\{\\s*\"path\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"", RegexOptions.Singleline);
            if (!match.Success) return null;
            string relativeReportPath = Regex.Unescape(match.Groups[1].Value);
            string reportFolder = Path.Combine(Path.GetDirectoryName(projectPath), relativeReportPath);
            string pagesPath = Path.Combine(reportFolder, "definition", "pages", "pages.json");
            return File.Exists(pagesPath) ? pagesPath : null;
        }
        catch { return null; }
    }
}

internal sealed class ReportForm : Form
{
    private readonly DashboardCanvas canvas;
    private readonly string selectedProjectPath;
    private readonly string pagesMetadataPath;
    private readonly string[] pageIds = { "0e60856db4624380477e", "a7f3c9d21b4e4f6a8c05", "7b1c4d92a05e4f3b8c61", "b8e4d1a35c2f4790ad16" };

    public ReportForm(string projectPath)
    {
        selectedProjectPath = projectPath;
        pagesMetadataPath = ProjectPicker.GetPagesMetadataPath(projectPath);
        Text = "MultiPizza | Reporte de ventas";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new System.Drawing.Size(1050, 650);
        ClientSize = new System.Drawing.Size(1440, 810);
        BackColor = System.Drawing.Color.FromArgb(244, 247, 251);
        Font = new System.Drawing.Font("Segoe UI", 9F);

        canvas = new DashboardCanvas();
        canvas.Dock = DockStyle.Fill;
        canvas.PageSelected += delegate(int page) { canvas.PageIndex = page; };
        canvas.LocalSelected += delegate(int local) { canvas.LocalIndex = local; ReloadData(); };
        canvas.PeriodSelected += delegate(int index) { canvas.MonthIndex = index; ReloadData(); };
        canvas.RefreshRequested += delegate { ReloadData(); };
        canvas.OpenPowerBIRequested += delegate { OpenInPowerBI(); };
        Controls.Add(canvas);
        Shown += delegate { ReloadData(); };
    }

    private void ReloadData()
    {
        canvas.Loading = true;
        canvas.ErrorMessage = null;
        canvas.Invalidate();
        Application.DoEvents();
        try
        {
            string local = null;
            if (canvas.LocalIndex == 1) local = "Mexico";
            else if (canvas.LocalIndex == 2) local = "Pasacana";
            else if (canvas.LocalIndex == 3) local = "Universitaria";
        int? month = null;
        if (canvas.MonthIndex == 1) month = 8;
        else if (canvas.MonthIndex == 2) month = 9;
        else if (canvas.MonthIndex == 3) month = 10;
            canvas.Data = ReportRepository.Load(local, month);
            canvas.LastUpdated = DateTime.Now;
            canvas.Loading = false;
            canvas.StartAnimation();
        }
        catch (Exception ex)
        {
            canvas.Data = new ReportData();
            canvas.ErrorMessage = "No se pudo leer SQL Server: " + ex.Message;
            canvas.Loading = false;
            canvas.StartAnimation();
        }
        canvas.Invalidate();
    }

    private void OpenInPowerBI()
    {
        string project = selectedProjectPath;
        string metadata = pagesMetadataPath;
        if (String.IsNullOrEmpty(project) || !File.Exists(project) || String.IsNullOrEmpty(metadata) || !File.Exists(metadata))
        {
            MessageBox.Show("No encuentro el proyecto .pbip seleccionado. Cierra el panel y vuelve a elegirlo.", "Proyecto no encontrado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (Process.GetProcessesByName("PBIDesktop").Length > 0)
        {
            MessageBox.Show("Power BI Desktop ya está abierto. Guarda y ciérralo antes de cambiar la página desde este lanzador.", "Power BI está abierto", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string original = File.ReadAllText(metadata, Encoding.UTF8);
        string id = pageIds[Math.Max(0, Math.Min(canvas.PageIndex, pageIds.Length - 1))];
        string updated = Regex.Replace(original, "(\"activePageName\"\\s*:\\s*\")[^\"]*(\")", delegate(Match match)
        {
            return match.Groups[1].Value + id + match.Groups[2].Value;
        });
        if (updated == original && original.IndexOf(id, StringComparison.Ordinal) < 0)
        {
            MessageBox.Show("No pude seleccionar la página. No se hicieron cambios.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string temp = metadata + ".launcher.tmp";
        try
        {
            if (updated != original)
            {
                File.WriteAllText(temp, updated, new UTF8Encoding(false));
                try { File.Replace(temp, metadata, null); }
                catch { File.Copy(temp, metadata, true); File.Delete(temp); }
            }
            string desktop = FindPowerBIDesktop();
            ProcessStartInfo start = new ProcessStartInfo();
            if (desktop != null)
            {
                start.FileName = desktop;
                start.Arguments = "\"" + project + "\"";
                start.UseShellExecute = false;
            }
            else
            {
                start.FileName = project;
                start.UseShellExecute = true;
            }
            Process.Start(start);
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
                if (updated != original) File.WriteAllText(metadata, original, new UTF8Encoding(false));
            }
            catch { }
            MessageBox.Show("Windows no pudo abrir el proyecto en Power BI Desktop.\r\n\r\n" + ex.Message, "No se pudo abrir Power BI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string FindPowerBIDesktop()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string[] candidates = {
            Path.Combine(programFiles, "Microsoft Power BI Desktop", "bin", "PBIDesktop.exe"),
            Path.Combine(programFilesX86, "Microsoft Power BI Desktop", "bin", "PBIDesktop.exe")
        };
        foreach (string path in candidates) if (File.Exists(path)) return path;
        try
        {
            string windowsApps = Path.Combine(programFiles, "WindowsApps");
            foreach (string package in Directory.GetDirectories(windowsApps, "Microsoft.MicrosoftPowerBIDesktop_*"))
            {
                string path = Path.Combine(package, "bin", "PBIDesktop.exe");
                if (File.Exists(path)) return path;
            }
        }
        catch { }
        return null;
    }
}
