using System.Diagnostics;
using System.Drawing.Printing;
using System.Text;

namespace AiryPdf;

public static class SelfTest
{
    private static readonly List<string> Results = [];
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Results.Add("PASS: " + name);
    }
    private static bool Near(double a, double b, double tolerance = .05) => Math.Abs(a - b) <= tolerance;
    public static async Task RunUiAsync()
    {
        Directory.CreateDirectory("artifacts");
        string fixture = System.IO.Path.GetFullPath("artifacts/dimension-check.pdf");
        CreateFixture(fixture, 5);
        var window = new MainWindow(); window.Show();
        string uiPath = Environment.GetEnvironmentVariable("AIRYPDF_UI_PDF") ?? fixture;
        using var uiDocument = new PdfDocument(uiPath);
        int pageTotal = uiDocument.Count;
        Check(pageTotal >= 2, "連続表示の検証対象が複数ページ");
        await window.OpenPathsAsync([uiPath]);
        window.UpdateLayout();
        Capture(window, "artifacts/viewer-window.png");
        var viewer = (ScrollViewer)window.FindName("Viewer");
        var host = (StackPanel)window.FindName("PagesHost");
        var pageNumber = (TextBox)window.FindName("PageNumber");
        Check(host.Children.Count == pageTotal, "PDFの全ページを連続して配置");
        // ScrollToOffsetだけでは実際のホイール経路の不具合を見落とすため、入力イベントでも検証する。
        for (int i = 0; i < 35; i++)
        {
            ((Grid)host.Children[0]).RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent });
            await Task.Delay(20);
        }
        await Task.Delay(800); window.UpdateLayout();
        Check(viewer.VerticalOffset > ((Grid)host.Children[0]).Height, "ホイール操作で1ページ目を越えて進む");
        viewer.ScrollToTop(); await Task.Delay(400);
        var second = (Grid)host.Children[1];
        viewer.ScrollToVerticalOffset(second.TranslatePoint(new Point(), host).Y - 150);
        await Task.Delay(600); window.UpdateLayout();
        Check(((Image)((Grid)host.Children[0]).Children[0]).Source != null && ((Image)second.Children[0]).Source != null, "境目で前後のページを同時に描画");
        Capture(window, "artifacts/continuous-boundary.png");
        viewer.ScrollToBottom(); await Task.Delay(600); window.UpdateLayout();
        Check(pageNumber.Text == pageTotal.ToString(), "スクロールで最終ページに移動しページ番号を更新");
        Check(((Image)((Grid)host.Children[pageTotal - 1]).Children[0]).Source != null, "最終ページの内容を描画");
        Check(((Image)((Grid)host.Children[0]).Children[0]).Source == null, "画面から離れたページの画像を解放");
        var zoomButton = FindButtons(window).First(b => (string?)b.Content == "＋");
        double oldHeight = second.Height;
        zoomButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(600); window.UpdateLayout();
        Check(second.Height > oldHeight && pageNumber.Text == pageTotal.ToString(), "連続表示の拡大後も閲覧ページを維持");
        Capture(window, "artifacts/continuous-last.png");
        viewer.ScrollToTop(); await Task.Delay(600); window.UpdateLayout();
        Check(pageNumber.Text == "1", "スクロールで先頭ページへ戻れる");
        using var doc = new PdfDocument(fixture);
        var print = new PrintWindow(doc, 0, null) { Owner = window };
        print.Show();
        await Task.Delay(1200);
        print.UpdateLayout();
        Capture(print, "artifacts/print-window.png");
        var printButton = (Button)print.FindName("PrintButton");
        Check(printButton.IsEnabled, "印刷プレビュー完了後に印刷可能");
        var modeBox = (ComboBox)print.FindName("ModeBox");
        modeBox.SelectedIndex = 2;
        Check(!printButton.IsEnabled, "設定変更後は古いプレビューで印刷させない");
        await print.RefreshAsync();
        Check(printButton.IsEnabled, "2アップのプレビュー更新後に印刷可能");
        Capture(print, "artifacts/print-two-up.png");
        var percentBox = (TextBox)print.FindName("PercentBox");
        percentBox.Text = "NaN";
        await print.RefreshAsync();
        Check(!printButton.IsEnabled, "不正な倍率入力で印刷を止める");
        percentBox.Text = "100";
        modeBox.SelectedIndex = 5;
        await print.RefreshAsync();
        Check(printButton.IsEnabled, "ポスターのプレビュー更新");
        Capture(print, "artifacts/print-poster.png");
        // 登録済みの物理プリンターもプレビューだけ検証する。印刷ジョブは送らない。
        var printerBox = (ComboBox)print.FindName("PrinterBox");
        foreach (string printerName in printerBox.Items.Cast<string>().ToArray())
        {
            printerBox.SelectedItem = printerName;
            modeBox.SelectedIndex = 0;
            await print.RefreshAsync();
            Check(printButton.IsEnabled, "プリンター切替・プレビュー: " + printerName);
        }
        print.Close(); window.Close();
        File.WriteAllLines("artifacts/ui-test-results.txt", Results);
    }
    private static IEnumerable<Button> FindButtons(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button button) yield return button;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window); SaveImage(image, path);
    }
    public static void Run()
    {
        Directory.CreateDirectory("artifacts");
        string fixture = System.IO.Path.GetFullPath("artifacts/dimension-check.pdf");
        CreateFixture(fixture, 5);
        string resolvedFixture = DesktopInstaller.ResolveShellPath(fixture);
        Check(File.Exists(resolvedFixture) && File.ReadAllBytes(fixture).SequenceEqual(File.ReadAllBytes(resolvedFixture)), "ショートカット用の実パスが同じファイルを指す");
        Check(!resolvedFixture.StartsWith(@"\\?\", StringComparison.Ordinal), "シェル用のパスから拡張接頭辞を除去");
        var watch = Stopwatch.StartNew();
        using var doc = new PdfDocument(fixture);
        Check(doc.Count == 5, "PDF読込・5ページ");
        Check(Near(doc.SizeMm(0).Width, 210) && Near(doc.SizeMm(0).Height, 297), "PDFのA4物理寸法");
        Check(doc.PrintPercent == 100, "新しいPDFの印刷倍率100%");
        var image = doc.Render(0, 794, 1123);
        SaveImage(image, "artifacts/pdf-render.png");
        Results.Add($"INFO: 読込＋初回描画 {watch.ElapsedMilliseconds} ms（合格基準未設定）");
        var paper = new Size(210, 297); var printable = new Rect(5, 5, 200, 287);
        var plan = PrintLayout.Build(doc.SizeMm, [0], paper, printable, new(PrintMode.Scale));
        Check(Near(plan[0].Items[0].Destination.Width, 210), "原寸で自動縮小しない");
        Check(PrintLayout.IsClipped(plan[0].Items[0], doc.SizeMm(0), null), "原寸の欠けを検出");
        plan = PrintLayout.Build(doc.SizeMm, [0], paper, printable, new(PrintMode.Scale, 50));
        Check(Near(plan[0].Items[0].Destination.Width, 105), "50%の物理寸法");
        plan = PrintLayout.Build(doc.SizeMm, [0], paper, printable, new(PrintMode.Fit));
        Check(!PrintLayout.IsClipped(plan[0].Items[0], doc.SizeMm(0), null), "用紙に合わせると欠けない");
        var selection = new Rect(10, 20, 100, 100);
        plan = PrintLayout.Build(doc.SizeMm, [0], paper, printable, new(PrintMode.Scale), selection);
        Check(Near(plan[0].Items[0].Clip.Width, 100) && Near(plan[0].Items[0].Clip.Height, 100), "選択範囲100mmを維持");
        plan = PrintLayout.Build(doc.SizeMm, [0, 1, 2, 3, 4], paper, printable, new(PrintMode.FourUp));
        Check(plan.Count == 2 && plan.SelectMany(s => s.Items).Select(p => p.Page).SequenceEqual([0, 1, 2, 3, 4]), "4アップの端数・ページ順");
        var landscape = new Size(297, 210); var wide = new Rect(5, 5, 287, 200);
        plan = PrintLayout.Build(doc.SizeMm, [0, 1, 2, 3], landscape, wide, new(PrintMode.Booklet));
        Check(plan[0].Items.Select(p => p.Page).SequenceEqual([3, 0]) && plan[1].Items.Select(p => p.Page).SequenceEqual([1, 2]), "4ページ小冊子の表裏順序");
        plan = PrintLayout.Build(doc.SizeMm, [0, 1, 2, 3, 4], landscape, wide, new(PrintMode.Booklet));
        Check(plan.Count == 4 && plan.SelectMany(s => s.Items).Count() == 5 && plan[0].Items.Single().Page == 0, "5ページ小冊子の末尾空白補充");
        plan = PrintLayout.Build(doc.SizeMm, [0, 1, 2, 3], landscape, wide, new(PrintMode.Booklet, RightBinding: true));
        Check(plan[0].Items.Select(p => p.Page).SequenceEqual([0, 3]), "右とじ小冊子");
        plan = PrintLayout.Build(doc.SizeMm, [0], paper, printable, new(PrintMode.Poster, 200, 5));
        Check(plan.Count == 9 && Near(plan[0].Items[0].Destination.Width, 420), "200%ポスター分割・倍率維持");
        Check(Near(plan[0].Items[0].Destination.X - plan[1].Items[0].Destination.X, 195), "ポスター5mmの重なり");
        Check(PrintLayout.ParsePages("1-3,5", 5).SequenceEqual([0, 1, 2, 4]), "ページ範囲入力");
        try { PrintLayout.ParsePages("0-8", 5); Check(false, "不正な範囲"); } catch (FormatException) { Check(true, "不正な範囲を拒否"); }
        try { PrintLayout.Build(doc.SizeMm, [0], paper, printable, new(PrintMode.Scale, double.NaN)); Check(false, "不正な倍率"); } catch (ArgumentException) { Check(true, "不正な倍率を拒否"); }
        doc.Rotate(0, 1);
        Check(Near(doc.SizeMm(0).Width, 297) && Near(doc.SizeMm(1).Width, 210), "現在ページだけ回転");
        string copy = System.IO.Path.GetFullPath("artifacts/rotated.pdf");
        doc.SaveCopy(copy);
        using (var reopened = new PdfDocument(copy)) Check(Near(reopened.SizeMm(0).Width, 297) && reopened.Count == 5, "回転の保存・再読込");
        using (var original = new PdfDocument(fixture)) Check(Near(original.SizeMm(0).Width, 210), "元ファイルの維持");
        try { doc.SaveCopy(fixture); Check(false, "原本上書き"); } catch (IOException) { Check(true, "原本への上書きを拒否"); }
        VirtualPrint(fixture, 50);
        VirtualPrint(fixture, 100);
        VirtualPrint(fixture, 200);
        File.WriteAllLines("artifacts/test-results.txt", Results);
    }

    private static void VirtualPrint(string fixture, int percent)
    {
        using var doc = new PdfDocument(fixture);
        using var printer = new PrintDocument();
        printer.PrinterSettings.PrinterName = "Microsoft Print to PDF";
        if (!printer.PrinterSettings.IsValid) { Results.Add("SKIP: Microsoft Print to PDF がありません"); return; }
        string output = System.IO.Path.GetFullPath($"artifacts/printed-{percent}-percent.pdf");
        printer.PrinterSettings.PrintToFile = true; printer.PrinterSettings.PrintFileName = output;
        printer.PrinterSettings.Duplex = Duplex.Simplex;
        printer.PrintController = new StandardPrintController();
        printer.OriginAtMargins = false;
        printer.DefaultPageSettings.PaperSize = printer.PrinterSettings.PaperSizes.Cast<PaperSize>().First(p => p.Kind == (percent == 200 ? PaperKind.A3 : PaperKind.A4));
        printer.DefaultPageSettings.Landscape = percent == 200;
        printer.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        var (paper, printable) = PrinterOutput.GetGeometry(printer);
        File.AppendAllText("artifacts/print-diagnostics.txt", $"{percent}% Paper={paper} Printable={printable} Landscape={printer.DefaultPageSettings.Landscape}\n");
        var sheet = PrintLayout.Build(doc.SizeMm, [0], paper, printable, new(PrintMode.Scale, percent))[0];
        printer.PrintPage += (_, args) => { PrinterOutput.Draw(doc, sheet, args); args.HasMorePages = false; };
        printer.Print();
        Check(File.Exists(output) && new FileInfo(output).Length > 100, "実際のWindows印刷経路からPDF出力");
        using var result = new PdfDocument(output);
        Check(result.Count == 1 && Near(result.SizeMm(0).Width, paper.Width, .2), $"{percent}%印刷結果のページ数・用紙寸法");
        int width = (int)Math.Round(result.SizeMm(0).Width * 4);
        int height = (int)Math.Round(result.SizeMm(0).Height * 4);
        BitmapSource rendered = result.Render(0, width, height);
        SaveImage(rendered, $"artifacts/printed-{percent}-render.png");
        var pixels = new byte[width * height * 4]; rendered.CopyPixels(pixels, width * 4, 0);
        var placement = sheet.Items[0];
        int expectedY = (int)Math.Round((placement.Destination.Y + 197 * percent / 100.0) * 4);
        int expectedX = (int)Math.Round((placement.Destination.X + 20 * percent / 100.0) * 4);
        bool Dark(int x, int y)
        {
            int p = (y * width + x) * 4;
            return pixels[p] < 200 && pixels[p + 1] < 200 && pixels[p + 2] < 200;
        }
        int horizontal = 0, vertical = 0;
        for (int y = Math.Max(0, expectedY - 8); y < Math.Min(height, expectedY + 8); y++)
        {
            int run = 0;
            for (int x = 0; x < width; x++) { if (Dark(x, y)) { run++; horizontal = Math.Max(horizontal, run); } else run = 0; }
        }
        for (int x = Math.Max(0, expectedX - 8); x < Math.Min(width, expectedX + 8); x++)
        {
            int run = 0;
            for (int y = 0; y < height; y++) { if (Dark(x, y)) { run++; vertical = Math.Max(vertical, run); } else run = 0; }
        }
        Check(Math.Abs(horizontal - percent * 4) <= 4, $"{percent}%の水平基準線（{horizontal / 4.0:F2}mm、画像上の測定）");
        Check(Math.Abs(vertical - percent * 4) <= 4, $"{percent}%の垂直基準線（{vertical / 4.0:F2}mm、画像上の測定）");
    }

    public static void SaveImage(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    public static void CreateFixture(string path, int count)
    {
        var objects = new List<string>();
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objects.Add($"<< /Type /Pages /Count {count} /Kids [{string.Join(' ', Enumerable.Range(0, count).Select(i => $"{3 + i * 2} 0 R"))}] >>");
        for (int i = 0; i < count; i++)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.2756 841.8898] /Contents {4 + i * 2} 0 R >>");
            // 横・縦100mmの基準線と、ページごとに数が変わる識別用の四角。
            string content = "0 0 0 RG 0.8 w 56.6929 283.4646 m 340.1575 283.4646 l S 56.6929 283.4646 m 56.6929 566.9292 l S\n";
            for (int n = 0; n <= i; n++) content += $"{60 + 22 * n} 730 14 14 re f\n";
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream");
        }
        using var stream = File.Create(path); var offsets = new List<long>();
        void Write(string text) { byte[] bytes = Encoding.ASCII.GetBytes(text); stream.Write(bytes); }
        Write("%PDF-1.7\n");
        for (int i = 0; i < objects.Count; i++) { offsets.Add(stream.Position); Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        long xref = stream.Position; Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
}
