using System.Drawing.Printing;
using System.Globalization;
using System.Runtime.InteropServices;


namespace AiryPdf;

public partial class PrintWindow : Window
{
    private readonly PdfDocument document;
    private readonly int currentPage;
    private readonly Rect? selectedRegion;
    private readonly PrintDocument printer = new();
    private List<Sheet> sheets = [];
    private Rect? activeRegion;
    private PrintMode activeMode;
    private bool ready;
    private int side, previewVersion, settingsVersion;
    public PrintWindow(PdfDocument doc, int page, Rect? region)
    {
        document = doc; currentPage = page; selectedRegion = region;
        InitializeComponent();
        PercentBox.Text = doc.PrintPercent.ToString(CultureInfo.InvariantCulture);
        RegionBox.IsEnabled = region.HasValue; RegionBox.IsChecked = region.HasValue;
        printer.OriginAtMargins = false;
        printer.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        printer.PrinterSettings.Copies = 1;
        printer.PrinterSettings.Duplex = Duplex.Simplex;
        foreach (string name in PrinterSettings.InstalledPrinters) PrinterBox.Items.Add(name);
        PrinterBox.SelectedItem = printer.PrinterSettings.PrinterName;
        LoadPapers(); ready = true;
        Loaded += (_, _) => Refresh();
        Closed += (_, _) => { ++previewVersion; printer.Dispose(); };
    }
    private void LoadPapers()
    {
        PaperBox.Items.Clear();
        PrinterName.Text = printer.PrinterSettings.IsValid ? printer.PrinterSettings.PrinterName : "利用可能なプリンターがありません";
        if (!printer.PrinterSettings.IsValid) return;
        foreach (PaperSize paper in printer.PrinterSettings.PaperSizes) PaperBox.Items.Add(paper);
        PaperBox.DisplayMemberPath = "PaperName";
        PaperBox.SelectedItem = PaperBox.Items.Cast<PaperSize>().FirstOrDefault(p => p.Kind == PaperKind.A4) ?? PaperBox.Items.Cast<PaperSize>().FirstOrDefault();
    }
    private void SettingsChanged(object s, RoutedEventArgs e)
    {
        if (!ready) return;
        ++previewVersion; ++settingsVersion; PrintButton.IsEnabled = false;
        Warning.Text = "設定が変わりました。「プレビューを更新」で配置を確認してください。";
    }
    private void PrinterSelected(object s, SelectionChangedEventArgs e)
    {
        if (!ready || PrinterBox.SelectedItem is not string name) return;
        try
        {
            printer.PrinterSettings = new PrinterSettings { PrinterName = name };
            printer.DefaultPageSettings = new PageSettings(printer.PrinterSettings) { Margins = new Margins(0, 0, 0, 0) };
            LoadPapers(); SettingsChanged(s, e);
        }
        catch (Exception ex) { Warning.Text = ex.Message; PrintButton.IsEnabled = false; }
    }
    private void PrinterClick(object s, RoutedEventArgs e)
    {
        try
        {
            if (!NativePrinterSettings.Show(this, printer)) return;
            int kind = printer.DefaultPageSettings.PaperSize.RawKind;
            LoadPapers();
            PaperBox.SelectedItem = PaperBox.Items.Cast<PaperSize>().FirstOrDefault(p => p.RawKind == kind) ?? PaperBox.SelectedItem;
            OrientationBox.SelectedIndex = printer.DefaultPageSettings.Landscape ? 1 : 0;
            SettingsChanged(s, e);
        }
        catch (Exception ex) { Warning.Text = ex.Message; PrintButton.IsEnabled = false; }
    }
    private static double Number(TextBox box, string label)
    {
        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n)) throw new ArgumentException($"{label}を数値で入力してください。");
        return n;
    }
    private void RefreshClick(object s, RoutedEventArgs e) => Refresh();
    private async void Refresh() => await RefreshAsync();
    internal async Task RefreshAsync()
    {
        int settings = ++settingsVersion;
        PrintButton.IsEnabled = false;
        try
        {
            if (!printer.PrinterSettings.IsValid || PaperBox.SelectedItem is not PaperSize paper) throw new InvalidOperationException("プリンターと用紙を選んでください。");
            activeMode = (PrintMode)ModeBox.SelectedIndex;
            double percent = Number(PercentBox, "印刷倍率");
            if (!short.TryParse(CopiesBox.Text, out short copies) || copies < 1 || copies > 999) throw new ArgumentException("部数は1〜999で指定してください。");
            if (DuplexBox.SelectedIndex != 0 && !printer.PrinterSettings.CanDuplex) throw new ArgumentException("選んだプリンターは両面印刷に対応していません。");
            if (activeMode == PrintMode.Booklet && (OrientationBox.SelectedIndex != 1 || DuplexBox.SelectedIndex != 2)) throw new ArgumentException("小冊子は「横」「両面・短辺とじ」を指定してください。");
            if (activeMode == PrintMode.Poster && DuplexBox.SelectedIndex != 0) throw new ArgumentException("ポスターは片面を指定してください。");
            printer.DefaultPageSettings.PaperSize = paper;
            printer.DefaultPageSettings.Landscape = OrientationBox.SelectedIndex == 1;
            printer.PrinterSettings.Copies = copies; printer.PrinterSettings.Collate = true;
            printer.PrinterSettings.Duplex = DuplexBox.SelectedIndex switch { 1 => Duplex.Vertical, 2 => Duplex.Horizontal, _ => Duplex.Simplex };
            var (paperSize, printableRect) = PrinterOutput.GetGeometry(printer);
            activeRegion = RegionBox.IsChecked == true ? selectedRegion : null;
            int[] pages = activeRegion.HasValue ? [currentPage] : PrintLayout.ParsePages(RangeBox.Text, document.Count);
            var options = new PrintOptions(activeMode, percent, Number(OverlapBox, "重なり幅"), RightBindingBox.IsChecked == true);
            sheets = PrintLayout.Build(document.SizeMm, pages, paperSize, printableRect, options, activeRegion);
            document.PrintPercent = percent;
            side = 0;
            bool clipped = activeMode != PrintMode.Poster && sheets.SelectMany(x => x.Items).Any(p => PrintLayout.IsClipped(p, document.SizeMm(p.Page), activeRegion));
            int paperCount = DuplexBox.SelectedIndex == 0 ? sheets.Count : (sheets.Count + 1) / 2;
            Summary.Text = $"{pages.Length}ページ → {sheets.Count}面 / {paperCount * copies}枚（{copies}部）\n用紙 {paperSize.Width:F1} × {paperSize.Height:F1} mm";
            Warning.Text = clipped ? "用紙の印刷可能範囲を超える部分が欠けます。指定倍率を維持し、自動縮小しません。赤線は印刷可能範囲です。" : "赤線は印刷可能範囲です（紙には印刷されません）。";
            await ShowSide();
            if (settings != settingsVersion) return;
            PrintButton.IsEnabled = document.CanPrint;
            if (!document.CanPrint) Warning.Text = "このPDFは高品質の印刷が制限されています。";
        }
        catch (Exception ex) { Warning.Text = ex.Message; sheets = []; Preview.Children.Clear(); }
    }
    private async Task ShowSide()
    {
        if (sheets.Count == 0) return;
        int version = ++previewVersion;
        Sheet sheet = sheets[side];
        const double display = 1.8;
        Preview.Width = sheet.Paper.Width * display; Preview.Height = sheet.Paper.Height * display;
        Preview.Children.Clear();
        foreach (Placement item in sheet.Items)
        {
            // 画面プレビューだけは軽量な画像。実印刷はPDFiumからプリンターへ直接描画する。
            double ratio = Math.Min(1, 1800 / Math.Max(item.Destination.Width * display, item.Destination.Height * display));
            BitmapSource bitmap = await Task.Run(() => document.Render(item.Page, Math.Max(1, (int)(item.Destination.Width * display * ratio)), Math.Max(1, (int)(item.Destination.Height * display * ratio))));
            if (version != previewVersion) return;
            var image = new Image { Source = bitmap, Width = item.Destination.Width * display, Height = item.Destination.Height * display, Stretch = Stretch.Fill,
                Clip = new RectangleGeometry(new Rect((item.Clip.X - item.Destination.X) * display, (item.Clip.Y - item.Destination.Y) * display, item.Clip.Width * display, item.Clip.Height * display)) };
            Canvas.SetLeft(image, item.Destination.X * display); Canvas.SetTop(image, item.Destination.Y * display); Preview.Children.Add(image);
        }
        var outline = new System.Windows.Shapes.Rectangle { Width = sheet.Printable.Width * display, Height = sheet.Printable.Height * display, Stroke = Brushes.IndianRed, StrokeThickness = 1, StrokeDashArray = [4, 3] };
        Canvas.SetLeft(outline, sheet.Printable.X * display); Canvas.SetTop(outline, sheet.Printable.Y * display); Preview.Children.Add(outline);
        SideLabel.Text = $"{side + 1} / {sheets.Count}  ·  {sheet.Label}  ·  " + string.Join(" / ", sheet.Items.Select(p => $"{p.Scale * 100:0.##}%"));
    }
    private async void PreviousClick(object s, RoutedEventArgs e) { if (side > 0) { side--; try { await ShowSide(); } catch (Exception ex) { Warning.Text = ex.Message; } } }
    private async void NextClick(object s, RoutedEventArgs e) { if (side + 1 < sheets.Count) { side++; try { await ShowSide(); } catch (Exception ex) { Warning.Text = ex.Message; } } }
    private void PrintNowClick(object s, RoutedEventArgs e)
    {
        if (!PrintButton.IsEnabled || sheets.Count == 0) return;
        int index = 0;
        void Draw(object? sender, PrintPageEventArgs args)
        {
            PrinterOutput.Draw(document, sheets[index++], args);
            args.HasMorePages = index < sheets.Count;
        }
        printer.DocumentName = System.IO.Path.GetFileName(document.Path);
        printer.PrintPage += Draw;
        try { printer.Print(); Warning.Text = "印刷データを送信しました。紙上の寸法精度は実測で確認してください。"; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "印刷できませんでした"); }
        finally { printer.PrintPage -= Draw; }
    }
}

public static class PrinterOutput
{
    [DllImport("gdi32.dll")] private static extern int SaveDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool RestoreDC(IntPtr dc, int saved);
    [DllImport("gdi32.dll")] private static extern int IntersectClipRect(IntPtr dc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr dc, int index);

    public static (Size Paper, Rect Printable) GetGeometry(PrintDocument printer)
    {
        // PrintableAreaはこの環境の横向き設定で縦の寸法を返したため、
        // 印刷時と同じPageSettingsのDCから物理寸法と余白を取得する。
        using var graphics = printer.PrinterSettings.CreateMeasurementGraphics(printer.DefaultPageSettings);
        IntPtr dc = graphics.GetHdc();
        try
        {
            double dx = GetDeviceCaps(dc, 88) / 25.4, dy = GetDeviceCaps(dc, 90) / 25.4;
            if (dx <= 0 || dy <= 0) throw new IOException("プリンターの解像度を取得できません。");
            var paper = new Size(GetDeviceCaps(dc, 110) / dx, GetDeviceCaps(dc, 111) / dy);
            var printable = new Rect(GetDeviceCaps(dc, 112) / dx, GetDeviceCaps(dc, 113) / dy, GetDeviceCaps(dc, 8) / dx, GetDeviceCaps(dc, 10) / dy);
            if (paper.Width <= 0 || paper.Height <= 0 || printable.Width <= 0 || printable.Height <= 0) throw new IOException("プリンターの用紙寸法を取得できません。");
            return (paper, Rect.Intersect(new Rect(paper), printable));
        }
        finally { graphics.ReleaseHdc(dc); }
    }
    public static void Draw(PdfDocument document, Sheet sheet, PrintPageEventArgs args)
    {
        var graphics = args.Graphics ?? throw new IOException("プリンターの描画領域を取得できません。");
        IntPtr dc = graphics.GetHdc();
        try
        {
            double px = GetDeviceCaps(dc, 88) / 25.4, py = GetDeviceCaps(dc, 90) / 25.4;
            int offsetX = GetDeviceCaps(dc, 112), offsetY = GetDeviceCaps(dc, 113);
            if (px <= 0 || py <= 0) throw new IOException("プリンターの解像度を取得できません。");
            int X(double mm) => (int)Math.Round(mm * px, MidpointRounding.AwayFromZero) - offsetX;
            int Y(double mm) => (int)Math.Round(mm * py, MidpointRounding.AwayFromZero) - offsetY;
            foreach (Placement item in sheet.Items)
            {
                int saved = SaveDC(dc);
                try
                {
                    IntersectClipRect(dc, X(item.Clip.Left), Y(item.Clip.Top), X(item.Clip.Right), Y(item.Clip.Bottom));
                    document.DrawToPrinter(dc, item.Page, X(item.Destination.X), Y(item.Destination.Y),
                        (int)Math.Round(item.Destination.Width * px, MidpointRounding.AwayFromZero), (int)Math.Round(item.Destination.Height * py, MidpointRounding.AwayFromZero));
                }
                finally { RestoreDC(dc, saved); }
            }
        }
        finally { graphics.ReleaseHdc(dc); }
    }
}
