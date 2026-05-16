using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Markdig;
using PdfSharp.Drawing;
using UglyToad.PdfPig;

namespace FileConverterApp;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm()); 
    }
}
public class MainForm : Form
{
    private Button btnSelectFile, btnConvert;
    private TextBox txtInputFile, txtLog;
    private ComboBox cbToFormat;
    private ProgressBar progressBar;

    public MainForm()
    {
        // Classic Windows Window Setup
        Text = "Universal File Converter";
        Size = new Size(500, 450);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; // Classic un-resizable window
        MaximizeBox = false;
        
        // Classic Windows 95/98/2K Grey Color Palette
        BackColor = SystemColors.Control;
        ForeColor = SystemColors.ControlText;
        Font = new Font("Microsoft Sans Serif", 8.25f, FontStyle.Regular);

        // GroupBox to give that classic inset framing border
        GroupBox mainGroupBox = new GroupBox
        {
            Text = "Parameters",
            Left = 12,
            Top = 10,
            Width = 460,
            Height = 125,
            FlatStyle = FlatStyle.Standard
        };

        Label lbl1 = new Label { Text = "Selected File:", Left = 15, Top = 25, AutoSize = true };
        txtInputFile = new TextBox { Left = 15, Top = 43, Width = 340, ReadOnly = true, BackColor = SystemColors.Window };
        
        btnSelectFile = new Button { Text = "Browse...", Left = 365, Top = 41, Width = 80, FlatStyle = FlatStyle.Standard };
        btnSelectFile.Click += BtnSelectFile_Click;

        Label lbl2 = new Label { Text = "Convert To:", Left = 15, Top = 80, AutoSize = true };
        cbToFormat = new ComboBox { Left = 15, Top = 95, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Standard };
        cbToFormat.Items.AddRange(ConversionEngine.SupportedFormats);
        if (cbToFormat.Items.Count > 0) cbToFormat.SelectedIndex = 0;

        btnConvert = new Button { Text = "Convert File", Left = 315, Top = 93, Width = 130, Height = 24, FlatStyle = FlatStyle.Standard };
        btnConvert.Click += BtnConvert_Click;

        mainGroupBox.Controls.Add(lbl1); mainGroupBox.Controls.Add(txtInputFile); mainGroupBox.Controls.Add(btnSelectFile);
        mainGroupBox.Controls.Add(lbl2); mainGroupBox.Controls.Add(cbToFormat); mainGroupBox.Controls.Add(btnConvert);

        // Log section
        Label lblLog = new Label { Text = "Output Log:", Left = 12, Top = 145, AutoSize = true };
        txtLog = new TextBox { Left = 12, Top = 160, Width = 460, Height = 195, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window, ForeColor = SystemColors.WindowText, Font = new Font("Courier New", 9f) };
        
        // Status & Progress section (FlatStyle removed here to fix error CS0117)
        progressBar = new ProgressBar { Left = 12, Top = 365, Width = 460, Height = 20 };

        Controls.Add(mainGroupBox);
        Controls.Add(lblLog);
        Controls.Add(txtLog); 
        Controls.Add(progressBar);
    }

    private void BtnSelectFile_Click(object? sender, EventArgs e)
    {
        using OpenFileDialog ofd = new OpenFileDialog { Title = "Open" };
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            txtInputFile.Text = ofd.FileName;
            Log($"Selected file: {Path.GetFileName(ofd.FileName)}");
        }
    }

    private async void BtnConvert_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtInputFile.Text) || !File.Exists(txtInputFile.Text))
        {
            MessageBox.Show("Please select a valid file first.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string inputPath = txtInputFile.Text;
        string fromFmt = Path.GetExtension(inputPath).TrimStart('.');
        string toFmt = cbToFormat.Text;
        
        string directory = Path.GetDirectoryName(inputPath) ?? "";
        string fileNameNoExt = Path.GetFileNameWithoutExtension(inputPath);
        string outputPath = Path.Combine(directory, $"{fileNameNoExt}_converted.{toFmt}");

        btnConvert.Enabled = false;
        progressBar.Value = 0;
        Log($"\n--- Starting Conversion Task ---");

        var logProgress = new Progress<(string message, LogLevel level)>(update => Log(update.message));
        var percentProgress = new Progress<int>(percent => progressBar.Value = percent);

        try
        {
            await ConversionEngine.ConvertAsync(inputPath, outputPath, fromFmt, toFmt, logProgress, percentProgress);
            MessageBox.Show("Conversion process completed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            MessageBox.Show($"Conversion failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnConvert.Enabled = true;
            progressBar.Value = 100;
        }
    }

    private void Log(string message)
    {
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}

public enum LogLevel { Info, Success, Warning, Error }
public static class ConversionEngine
{
    public static readonly string[] SupportedFormats =
        { "txt", "csv", "json", "md", "html", "png", "jpg", "pdf", "bat" };

    public static async Task ConvertAsync(
        string inputPath, string outputPath, string fromFmt, string toFmt,
        IProgress<(string message, LogLevel level)> log, IProgress<int> progressPercent)
    {
        fromFmt = fromFmt.ToLowerInvariant().TrimStart('.');
        toFmt   = toFmt.ToLowerInvariant().TrimStart('.');

        log.Report(($"Format : {fromFmt.ToUpper()} -> {toFmt.ToUpper()}", LogLevel.Info));
        progressPercent.Report(5);

        if (fromFmt == toFmt)
        {
            await Task.Run(() => File.Copy(inputPath, outputPath, overwrite: true));
            log.Report(("Source and destination formats identical. File copied.", LogLevel.Success));
            progressPercent.Report(100);
            return;
        }

        await Task.Run(() => Dispatch(inputPath, outputPath, fromFmt, toFmt, log));
        progressPercent.Report(100);
        log.Report(($"Output file generated: {Path.GetFileName(outputPath)}", LogLevel.Success));
    }

    private static void Dispatch(string input, string output, string from, string to, IProgress<(string, LogLevel)> log)
    {
        switch ((from, to))
        {
            case ("txt", "csv"):  TxtToCsv(input, output, log);  break;
            case ("txt", "json"): TxtToJson(input, output, log); break;
            case ("txt", "md"):   CopyText(input, output, log, "Saved as Markdown (plain text)."); break;
            case ("txt", "html"): TxtToHtml(input, output, log); break;
            case ("txt", "bat"):  TxtToBat(input, output, log);  break;
            case ("txt", "pdf"):  TextToPdf(input, output, log); break;
            case ("txt", "png"):  TextToImage(input, output, ImageFormat.Png, log);  break;
            case ("txt", "jpg"):  TextToImage(input, output, ImageFormat.Jpeg, log); break;

            case ("csv", "txt"):  CopyText(input, output, log, "Saved CSV as plain text."); break;
            case ("csv", "json"): CsvToJson(input, output, log); break;
            case ("csv", "md"):   CsvToMd(input, output, log);   break;
            case ("csv", "html"): CsvToHtml(input, output, log); break;
            case ("csv", "bat"):  TxtToBat(input, output, log);  break;
            case ("csv", "pdf"):  TextToPdf(input, output, log); break;
            case ("csv", "png"):  TextToImage(input, output, ImageFormat.Png, log);  break;
            case ("csv", "jpg"):  TextToImage(input, output, ImageFormat.Jpeg, log); break;

            case ("json", "txt"):  CopyText(input, output, log, "Saved JSON as plain text."); break;
            case ("json", "csv"):  JsonToCsv(input, output, log);  break;
            case ("json", "md"):   JsonToMd(input, output, log);   break;
            case ("json", "html"): JsonToHtml(input, output, log); break;
            case ("json", "bat"):  TxtToBat(input, output, log);   break;
            case ("json", "pdf"):  TextToPdf(input, output, log);  break;
            case ("json", "png"):  TextToImage(input, output, ImageFormat.Png, log);  break;
            case ("json", "jpg"):  TextToImage(input, output, ImageFormat.Jpeg, log); break;

            case ("md", "txt"):  MdToTxt(input, output, log);  break;
            case ("md", "csv"):  TxtToCsv(input, output, log); break;
            case ("md", "html"): MdToHtml(input, output, log); break;
            case ("md", "pdf"):  MdToPdf(input, output, log);  break;
            case ("md", "png"):  TextToImage(input, output, ImageFormat.Png, log);  break;
            case ("md", "jpg"):  TextToImage(input, output, ImageFormat.Jpeg, log); break;
            case ("md", "bat"):  TxtToBat(input, output, log); break;

            case ("html", "txt"): HtmlToTxt(input, output, log); break;
            case ("html", "md"):  HtmlToMd(input, output, log);  break;
            case ("html", "csv"): HtmlToTxt(input, output, log); break;
            case ("html", "bat"): TxtToBat(input, output, log);  break;
            case ("html", "pdf"): TextToPdf(input, output, log); break;
            case ("html", "png"): TextToImage(input, output, ImageFormat.Png, log);  break;
            case ("html", "jpg"): TextToImage(input, output, ImageFormat.Jpeg, log); break;

            case ("bat", "txt"):  CopyText(input, output, log, "Read batch script as plain text."); break;
            case ("bat", "md"):   CopyText(input, output, log, "Saved batch script as Markdown."); break;
            case ("bat", "html"): TxtToHtml(input, output, log); break;
            case ("bat", "json"): TxtToJson(input, output, log); break;
            case ("bat", "csv"):  TxtToCsv(input, output, log);  break;
            case ("bat", "pdf"):  TextToPdf(input, output, log); break;

            case ("pdf", "txt"): PdfToTxt(input, output, log); break;
            case ("pdf", "md"):  PdfToTxt(input, output, log); break;

            case ("png", "jpg"): ConvertImage(input, output, ImageFormat.Jpeg, log); break;
            case ("jpg", "png"): ConvertImage(input, output, ImageFormat.Png,  log); break;
            case ("png", "pdf"): ImageToPdf(input, output, log); break;
            case ("jpg", "pdf"): ImageToPdf(input, output, log); break;

            default: throw new NotSupportedException($"Conversion from .{from} to .{to} is not supported.");
        }
    }

    private static string Read(string path) => File.ReadAllText(path, Encoding.UTF8);
    private static string HtmlEncode(string s) => System.Net.WebUtility.HtmlEncode(s);
    private static string HtmlDecode(string s) => System.Net.WebUtility.HtmlDecode(s);

    private static void CopyText(string input, string output, IProgress<(string, LogLevel)> log, string msg)
    {
        File.Copy(input, output, overwrite: true);
        log.Report((msg, LogLevel.Info));
    }

    private static void TxtToCsv(string input, string output, IProgress<(string, LogLevel)> log)
    {
        var lines = File.ReadAllLines(input, Encoding.UTF8);
        var sb = new StringBuilder();
        sb.AppendLine("\"line_number\",\"content\"");
        for (int i = 0; i < lines.Length; i++) sb.AppendLine($"{i + 1},\"{lines[i].Replace("\"", "\"\"")}\"");
        File.WriteAllText(output, sb.ToString(), Encoding.UTF8);
        log.Report(($"Wrote {lines.Length} rows to CSV.", LogLevel.Info));
    }

    private static void TxtToJson(string input, string output, IProgress<(string, LogLevel)> log)
    {
        var lines = File.ReadAllLines(input, Encoding.UTF8);
        var obj = new { source = Path.GetFileName(input), line_count = lines.Length, content = string.Join("\n", lines), lines };
        File.WriteAllText(output, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        log.Report(($"Serialized {lines.Length} lines to JSON.", LogLevel.Info));
    }

    private static void TxtToHtml(string input, string output, IProgress<(string, LogLevel)> log)
    {
        var content = HtmlEncode(Read(input));
        var html = $"<!DOCTYPE html><html><body><pre>{content}</pre></body></html>";
        File.WriteAllText(output, html, Encoding.UTF8);
        log.Report(("Wrapped content in HTML.", LogLevel.Info));
    }

    private static void TxtToBat(string input, string output, IProgress<(string, LogLevel)> log)
    {
        var lines = File.ReadAllLines(input, Encoding.UTF8);
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        foreach (var line in lines) sb.AppendLine(string.IsNullOrEmpty(line) ? "echo." : $"echo {line.Replace("^", "^^").Replace("&", "^&").Replace("|", "^|").Replace("<", "^<").Replace(">", "^>")}");
        sb.AppendLine("pause");
        File.WriteAllText(output, sb.ToString(), Encoding.ASCII);
        log.Report(("Created batch script.", LogLevel.Info));
    }

    private static List<string[]> ParseCsv(string path)
    {
        var rows = new List<string[]>();
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool inQ = false;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"') { if (inQ && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; } else inQ = !inQ; }
                else if (line[i] == ',' && !inQ) { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(line[i]);
            }
            fields.Add(current.ToString());
            rows.Add([.. fields]);
        }
        return rows;
    }

    private static void CsvToJson(string input, string output, IProgress<(string, LogLevel)> log)
    {
        var rows = ParseCsv(input);
        if (rows.Count == 0) { File.WriteAllText(output, "[]"); return; }
        var headers = rows[0];
        var records = new List<Dictionary<string, string>>();
        for (int i = 1; i < rows.Count; i++)
        {
            var dict = new Dictionary<string, string>();
            for (int j = 0; j < headers.Length; j++) dict[headers[j]] = j < rows[i].Length ? rows[i][j] : "";
            records.Add(dict);
        }
        File.WriteAllText(output, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        log.Report(("Converted CSV to JSON.", LogLevel.Info));
    }

    private static void CsvToHtml(string input, string output, IProgress<(string, LogLevel)> log)
    {
        var rows = ParseCsv(input);
        var sb = new StringBuilder("<!DOCTYPE html><html><body><table border='1'>");
        for (int i = 0; i < rows.Count; i++)
        {
            sb.Append("<tr>");
            foreach (var cell in rows[i]) sb.Append(i == 0 ? $"<th>{HtmlEncode(cell)}</th>" : $"<td>{HtmlEncode(cell)}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table></body></html>");
        File.WriteAllText(output, sb.ToString(), Encoding.UTF8);
        log.Report(("Rendered CSV as HTML table.", LogLevel.Info));
    }

    private static void CsvToMd(string input, string output, IProgress<(string, LogLevel)> log)
    {
        var rows = ParseCsv(input);
        if (rows.Count == 0) return;
        var sb = new StringBuilder();
        sb.AppendLine("| " + string.Join(" | ", rows[0].Select(HtmlEncode)) + " |");
        sb.AppendLine("| " + string.Join(" | ", rows[0].Select(_ => "---")) + " |");
        for (int i = 1; i < rows.Count; i++) sb.AppendLine("| " + string.Join(" | ", rows[i].Select(c => c.Replace("|", "\\|"))) + " |");
        File.WriteAllText(output, sb.ToString(), Encoding.UTF8);
        log.Report(("Converted CSV to Markdown.", LogLevel.Info));
    }

    private static void JsonToCsv(string input, string output, IProgress<(string, LogLevel)> log)
    {
        var json = Read(input);
        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            var items = doc.RootElement.EnumerateArray().ToList();
            if (items.Count == 0) return;
            var keys = new List<string>();
            foreach (var item in items) if (item.ValueKind == JsonValueKind.Object) foreach (var prop in item.EnumerateObject()) if (!keys.Contains(prop.Name)) keys.Add(prop.Name);
            sb.AppendLine(string.Join(",", keys.Select(h => $"\"{h}\"")));
            foreach (var item in items) sb.AppendLine(string.Join(",", keys.Select(h => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(h, out var v) ? $"\"{v.ToString().Replace("\"", "\"\"")}\"" : "\"\"")));
        }
        else { sb.AppendLine("\"key\",\"value\""); foreach (var prop in doc.RootElement.EnumerateObject()) sb.AppendLine($"\"{prop.Name}\",\"{prop.Value.ToString().Replace("\"", "\"\"")}\""); }
        File.WriteAllText(output, sb.ToString(), Encoding.UTF8);
        log.Report(("Flattened JSON to CSV.", LogLevel.Info));
    }

    private static void JsonToHtml(string input, string output, IProgress<(string, LogLevel)> log)
    {
        var pretty = JsonSerializer.Serialize(JsonDocument.Parse(Read(input)).RootElement, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(output, $"<!DOCTYPE html><html><body><pre>{HtmlEncode(pretty)}</pre></body></html>", Encoding.UTF8);
        log.Report(("Rendered JSON to HTML.", LogLevel.Info));
    }

    private static void JsonToMd(string input, string output, IProgress<(string, LogLevel)> log)
    {
        var pretty = JsonSerializer.Serialize(JsonDocument.Parse(Read(input)).RootElement, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(output, $"```json\n{pretty}\n```\n", Encoding.UTF8);
        log.Report(("Wrapped JSON in Markdown.", LogLevel.Info));
    }

    private static readonly MarkdownPipeline _mdPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private static void MdToHtml(string input, string output, IProgress<(string, LogLevel)> log)
    {
        File.WriteAllText(output, $"<!DOCTYPE html><html><body>\n{Markdig.Markdown.ToHtml(Read(input), _mdPipeline)}\n</body></html>", Encoding.UTF8);
        log.Report(("Rendered Markdown to HTML.", LogLevel.Info));
    }

    private static void MdToTxt(string input, string output, IProgress<(string, LogLevel)> log)
    {
        File.WriteAllText(output, Markdig.Markdown.ToPlainText(Read(input), _mdPipeline).Trim(), Encoding.UTF8);
        log.Report(("Stripped Markdown to Text.", LogLevel.Info));
    }

    private static void MdToPdf(string input, string output, IProgress<(string, LogLevel)> log)
    {
        WriteTextToPdf(Markdig.Markdown.ToPlainText(Read(input), _mdPipeline), output);
        log.Report(("Converted Markdown to PDF.", LogLevel.Info));
    }

    private static void HtmlToTxt(string input, string output, IProgress<(string, LogLevel)> log)
    {
        var html = Regex.Replace(Read(input), @"<style[\s\S]*?</style>|<script[\s\S]*?</script>|<[^>]+>", " ", RegexOptions.IgnoreCase);
        File.WriteAllText(output, Regex.Replace(HtmlDecode(html), @"\s{2,}", " ").Trim(), Encoding.UTF8);
        log.Report(("Stripped HTML tags.", LogLevel.Info));
    }

    private static void HtmlToMd(string input, string output, IProgress<(string, LogLevel)> log)
    {
        var md = Regex.Replace(Read(input), @"<(strong|b)[^>]*>(.*?)</(strong|b)>", "**$2**", RegexOptions.IgnoreCase);
        md = Regex.Replace(md, @"<[^>]+>", string.Empty);
        File.WriteAllText(output, HtmlDecode(md).Trim(), Encoding.UTF8);
        log.Report(("Converted HTML to basic Markdown.", LogLevel.Info));
    }

    private static void TextToPdf(string input, string output, IProgress<(string, LogLevel)> log)
    {
        WriteTextToPdf(Read(input), output);
        log.Report(("Rendered text to PDF.", LogLevel.Info));
    }

    private static void WriteTextToPdf(string text, string outputPath)
    {
        var doc = new PdfSharp.Pdf.PdfDocument(); 
        var page = doc.AddPage();
        var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Courier New", 10, XFontStyleEx.Regular);
        double y = 40;
        foreach (var line in text.Split('\n')) { gfx.DrawString(line.Length > 80 ? line.Substring(0, 80) : line, font, XBrushes.Black, new XPoint(40, y)); y += 14; if (y > page.Height - 40) { page = doc.AddPage(); gfx = XGraphics.FromPdfPage(page); y = 40; } }
        doc.Save(outputPath);
    }

    private static void PdfToTxt(string input, string output, IProgress<(string, LogLevel)> log)
    {
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(input);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages()) sb.AppendLine(page.Text);
        File.WriteAllText(output, sb.ToString(), Encoding.UTF8);
        log.Report(("Extracted text from PDF.", LogLevel.Info));
    }

    private static void ConvertImage(string input, string output, ImageFormat format, IProgress<(string, LogLevel)> log)
    {
        using var img = Image.FromFile(input);
        img.Save(output, format);
        log.Report(("Converted image.", LogLevel.Info));
    }

    private static void ImageToPdf(string input, string output, IProgress<(string, LogLevel)> log)
    {
        var doc = new PdfSharp.Pdf.PdfDocument(); 
        var page = doc.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        using var xImg = XImage.FromFile(input);
        gfx.DrawImage(xImg, 40, 40, 500, 500); 
        doc.Save(output);
        log.Report(("Embedded image in PDF.", LogLevel.Info));
    }

    private static void TextToImage(string input, string output, ImageFormat format, IProgress<(string, LogLevel)> log)
    {
        using var bmp = new Bitmap(800, 600);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.DrawString(Read(input).Substring(0, Math.Min(Read(input).Length, 1000)), new Font("Courier New", 12), Brushes.Black, 20, 20);
        bmp.Save(output, format);
        log.Report(("Rendered text to image.", LogLevel.Info));
    }
}
