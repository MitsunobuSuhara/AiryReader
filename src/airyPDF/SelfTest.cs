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
        var zoomInput = (TextBox)window.FindName("ZoomText");
        void EnterZoom(string text)
        {
            zoomInput.Text = text;
            zoomInput.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, System.Windows.Input.Key.Enter) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
        }
        EnterZoom("100");
        await Task.Delay(300); window.UpdateLayout();
        Check(Near(second.Height, oldHeight) && zoomInput.Text == "100", "倍率手入力で100％へ正確に戻す");
        EnterZoom("125%");
        Check(Near(second.Height, oldHeight * 1.25), "％付きの倍率手入力を反映");
        EnterZoom("NaN");
        Check(Near(second.Height, oldHeight * 1.25) && zoomInput.Text == "125", "不正な表示倍率で状態を壊さない");
        EnterZoom("95"); window.ZoomByWheel(120);
        Check(Near(second.Height, oldHeight) && zoomInput.Text == "100", "Ctrlホイール拡大で100％に止まる");
        window.ZoomByWheel(120);
        Check(Near(second.Height, oldHeight), "連続ホイールでも100％で短く停止");
        await Task.Delay(500); window.ZoomByWheel(120);
        Check(second.Height > oldHeight, "100％から次のホイールで拡大できる");
        EnterZoom("105"); window.ZoomByWheel(-120);
        Check(Near(second.Height, oldHeight) && zoomInput.Text == "100", "Ctrlホイール縮小で100％に止まる");
        await Task.Delay(500); window.ZoomByWheel(-120);
        Check(second.Height < oldHeight, "100％から次のホイールで縮小できる");
        EnterZoom("100");
        Check(!FindButtons(window).Any(b => (string?)b.Content == "100%"), "100％ボタンを削除");
        var fit = FindButtons(window).First(b => (string?)b.Content == "画面幅に合わせる");
        Check(((Panel)fit.Parent).Children[((Panel)fit.Parent).Children.Count - 1] == fit, "画面幅に合わせるを右端に配置");
        Check(window.FindName("SelectRegion") == null && FindButtons(window).Any(b => (string?)b.Content == "画面幅に合わせる"), "範囲選択を削除して画面幅の文言に変更");
        window.WindowState = WindowState.Normal; window.Width = 860;
        await Task.Delay(200); window.UpdateLayout();
        fit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(second.Height < oldHeight, "画面幅に合わせて100％未満の倍率にする");
        for (int i = 0; i < 40 && !Near(second.Height, oldHeight); i++) window.ZoomByWheel(120);
        Check(Near(second.Height, oldHeight) && zoomInput.Text == "100", "画面幅調整後の連続ホイールも100％で停止");
        fit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (int i = 0; i < 40 && !Near(second.Height, oldHeight); i++) zoomButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Near(second.Height, oldHeight) && zoomInput.Text == "100", "画面幅調整後の＋ボタンも100％で停止");
        EnterZoom("105");
        FindButtons(window).First(b => (string?)b.Content == "−").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Near(second.Height, oldHeight), "−ボタンも100％で停止");
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
        Check(print.InputBindings.OfType<System.Windows.Input.KeyBinding>().Any(b => b.Command == System.Windows.Input.ApplicationCommands.Print && b.Key == System.Windows.Input.Key.Enter && b.Modifiers == System.Windows.Input.ModifierKeys.Control), "Ctrl＋Enterを印刷コマンドに割り当て");
        Check(System.Windows.Input.ApplicationCommands.Print.CanExecute(null, print), "プレビュー完了時だけショートカットで印刷可能");
        print.FitToWorkArea(new Rect(0, 0, 1024, 600));
        print.UpdateLayout();
        Check(print.Top >= 0 && print.Top + print.ActualHeight <= 600, "小さい画面でも印刷ウィンドウを画面内に収める");
        Point printLocation = printButton.TranslatePoint(new Point(), print);
        Check(printButton.IsVisible && printLocation.Y >= 0 && printLocation.Y + printButton.ActualHeight < print.ActualHeight && printLocation.Y > print.ActualHeight / 2, "小画面でも下部の印刷ボタンを常時表示");
        Capture(print, "artifacts/print-small-window.png");
        var previewScroller = (ScrollViewer)print.FindName("PreviewScroller");
        var sideText = (TextBlock)print.FindName("SideLabel");
        void PreviewScroll(int delta) => previewScroller.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent });
        previewScroller.ScrollToBottom(); print.UpdateLayout();
        PreviewScroll(-120);
        for (int i = 0; i < 100 && !sideText.Text.StartsWith("2 /"); i++) await Task.Delay(100);
        Check(sideText.Text.StartsWith("2 /"), "印刷プレビューの下端でホイールから次の面へ");
        previewScroller.ScrollToTop(); print.UpdateLayout();
        PreviewScroll(120);
        for (int i = 0; i < 100 && !sideText.Text.StartsWith("1 /"); i++) await Task.Delay(100);
        Check(sideText.Text.StartsWith("1 /"), "印刷プレビューの上端でホイールから前の面へ");
        previewScroller.ScrollToTop(); print.UpdateLayout(); PreviewScroll(120);
        Check(sideText.Text.StartsWith("1 /"), "印刷プレビューの先頭から範囲外へ進まない");
        var modeBox = (ComboBox)print.FindName("ModeBox");
        modeBox.SelectedIndex = 2;
        Check(!printButton.IsEnabled, "設定変更後は古いプレビューで印刷させない");
        await print.RefreshAsync();
        Check(printButton.IsEnabled, "2アップのプレビュー更新後に印刷可能");
        Capture(print, "artifacts/print-two-up.png");
        var percentBox = (TextBox)print.FindName("PercentBox");
        foreach (string name in new[] { "PercentBox", "CopiesBox", "OverlapBox" })
        {
            var numberInput = (TextBox)print.FindName(name);
            numberInput.Select(0, 0);
            numberInput.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent });
            Check(numberInput.SelectionLength == numberInput.Text.Length, "印刷入力欄クリックで全選択: " + name);
        }
        FindButtons(print).First(b => (string?)b.Tag == "PercentBox:1").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(percentBox.Text == "101", "倍率の上矢印で1％増やす");
        var copiesInput = (TextBox)print.FindName("CopiesBox");
        FindButtons(print).First(b => (string?)b.Tag == "CopiesBox:1").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(copiesInput.Text == "2", "部数の上矢印で1部増やす");
        copiesInput.Text = "1";
        FindButtons(print).First(b => (string?)b.Tag == "CopiesBox:-1").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(copiesInput.Text == "1", "部数の下矢印で1部未満にしない");
        modeBox.SelectedIndex = 0;
        percentBox.Text = "50";
        for (int i = 0; i < 100 && !printButton.IsEnabled; i++) await Task.Delay(100);
        Check(printButton.IsEnabled && ((TextBlock)print.FindName("SideLabel")).Text.Contains("50%"), "倍率変更が更新ボタンなしで自動反映");
        percentBox.Text = "150"; percentBox.Text = "75";
        for (int i = 0; i < 100 && !printButton.IsEnabled; i++) await Task.Delay(100);
        Check(printButton.IsEnabled && ((TextBlock)print.FindName("SideLabel")).Text.Contains("75%"), "連続入力では最後の倍率を反映");
        percentBox.Text = "NaN";
        await print.RefreshAsync();
        Check(!printButton.IsEnabled, "不正な倍率入力で印刷を止める");
        Check(!System.Windows.Input.ApplicationCommands.Print.CanExecute(null, print), "不正な倍率ならCtrl＋Enterの印刷も無効");
        percentBox.Text = "100";
        Check(((FrameworkElement)print.FindName("PosterSettings")).Visibility == Visibility.Collapsed, "通常の印刷で貼り合わせ幅を隠す");
        modeBox.SelectedIndex = 5;
        Check(((FrameworkElement)print.FindName("PosterSettings")).Visibility == Visibility.Visible, "ポスター選択時だけ貼り合わせ幅を表示");
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
        print.Close();
        using (var landscapeDoc = new PdfDocument(System.IO.Path.Combine(AppContext.BaseDirectory, "Samples", "print-check.pdf")))
        {
            var landscapePrint = new PrintWindow(landscapeDoc, 1, null) { Owner = window };
            landscapePrint.Show();
            var landscapeButton = (Button)landscapePrint.FindName("PrintButton");
            for (int i = 0; i < 100 && !landscapeButton.IsEnabled; i++) await Task.Delay(100);
            var previewCanvas = (Canvas)landscapePrint.FindName("Preview");
            Check(((ComboBox)landscapePrint.FindName("OrientationBox")).SelectedIndex == 1 && previewCanvas.Width > previewCanvas.Height, "A4横PDFの初回プレビューを横向きにする");
            landscapePrint.Close();
            var a3Print = new PrintWindow(landscapeDoc, 2, null) { Owner = window };
            a3Print.Show();
            var a3Button = (Button)a3Print.FindName("PrintButton");
            for (int i = 0; i < 100 && !a3Button.IsEnabled; i++) await Task.Delay(100);
            Check(((PaperSize)((ComboBox)a3Print.FindName("PaperBox")).SelectedItem).Kind == PaperKind.A3 && ((ComboBox)a3Print.FindName("OrientationBox")).SelectedIndex == 1, "A3横原本は用紙サイズもA3横に合わせる");
            a3Print.Close();
        }
        if (File.Exists("artifacts/feature-tests/forms.pdf"))
        {
            string formPath = Environment.GetEnvironmentVariable("AIRYPDF_TOOL_PDF") ?? "artifacts/feature-tests/forms.pdf";
            using var forms = new PdfDocument(formPath);
            var metadata = await PdfHelper.RunAsync(new Dictionary<string,object?> { ["operation"]="inspect", ["source"]=formPath, ["password"]="" });
            int expectedFields = metadata.GetProperty("fields").GetArrayLength();
            var tools = new PdfToolsWindow(forms, 0, _ => { }, _ => Task.CompletedTask) { Owner = window };
            tools.Show();
            for (int i = 0; i < 100 && tools.FieldCount < expectedFields; i++) await Task.Delay(100);
            tools.UpdateLayout();
            Check(tools.FieldCount == expectedFields && tools.StatusMessage.Contains("入力欄"), "フォーム欄としおりを読み込んで表示: " + tools.StatusMessage);
            Check(FindButtons(tools).Any(b => (string?)b.Content == "入力内容を別名保存"), "フォーム編集画面を表示");
            Capture(tools, "artifacts/pdf-tools-window.png");
            tools.Close();
        }
        window.Close();
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
        if (File.Exists("artifacts/feature-tests/encrypted.pdf"))
        {
            bool refused = false;
            try { using var protectedDoc = new PdfDocument("artifacts/feature-tests/encrypted.pdf", "wrong"); }
            catch (PdfPasswordException) { refused = true; }
            Check(refused, "間違ったPDFパスワードを拒否");
            using var protectedOk = new PdfDocument("artifacts/feature-tests/encrypted.pdf", "secret");
            Check(protectedOk.Count == 2, "パスワード付きPDFを全ページ読込");
            using var filled = new PdfDocument("artifacts/feature-tests/filled.pdf");
            SaveImage(filled.Render(0,595,842), "artifacts/filled-native.png"); Check(filled.Count == 2, "保存した入力欄のPDFium描画");
            using var added = new PdfDocument("artifacts/feature-tests/added.pdf");
            Check(added.PageText(1).Contains("日本語の記入"), "日本語追記をPDFium検索で抽出");
            using var signed = new PdfDocument("artifacts/feature-tests/signed.pdf");
            Check(signed.HasSignatures && !signed.CanEdit, "署名済みPDFの編集を保護");
        }
        var watch = Stopwatch.StartNew();
        string? compat = Environment.GetEnvironmentVariable("AIRYPDF_COMPAT_PDF");
        if (!string.IsNullOrEmpty(compat))
        {
            using var compatible = new PdfDocument(compat);
            Check(compatible.CanFill && !compatible.CanEdit, "Adobe系PDFのフォーム入力権限を維持");
            for (int i=0;i<compatible.Count;i++) SaveImage(compatible.Render(i,794,1123), $"artifacts/compat-native-{i+1}.png");
        }
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
        if (File.Exists("artifacts/feature-tests/filled.pdf")) VirtualPrintForm();
        VirtualPrint(fixture, 50);
        VirtualPrint(fixture, 100);
        VirtualPrint(fixture, 200);
        File.WriteAllLines("artifacts/test-results.txt", Results);
    }

    private static void VirtualPrintForm()
    {
        using var doc = new PdfDocument("artifacts/feature-tests/filled.pdf");
        using var printer = new PrintDocument();
        printer.PrinterSettings.PrinterName = "Microsoft Print to PDF";
        if (!printer.PrinterSettings.IsValid) return;
        string output = System.IO.Path.GetFullPath("artifacts/form-printed.pdf");
        printer.PrinterSettings.PrintToFile = true; printer.PrinterSettings.PrintFileName = output;
        printer.PrintController = new StandardPrintController();
        printer.DefaultPageSettings.PaperSize = printer.PrinterSettings.PaperSizes.Cast<PaperSize>().First(p => p.Kind == PaperKind.A4);
        printer.DefaultPageSettings.Margins = new Margins(0,0,0,0);
        var (paper, printable) = PrinterOutput.GetGeometry(printer);
        var sheet = PrintLayout.Build(doc.SizeMm, [0], paper, printable, new(PrintMode.Scale, 100))[0];
        printer.PrintPage += (_, args) => { PrinterOutput.Draw(doc, sheet, args); args.HasMorePages = false; };
        printer.Print();
        using var result = new PdfDocument(output);
        var image = result.Render(0,595,842); SaveImage(image, "artifacts/form-printed.png");
        var pixels = new byte[595*842*4]; image.CopyPixels(pixels,595*4,0);
        int dark = 0;
        for(int y=115;y<140;y++) for(int x=50;x<180;x++) if(pixels[(y*595+x)*4]<160)dark++;
        Check(dark > 100, "日本語入力欄が実印刷経路でも出力される");
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
