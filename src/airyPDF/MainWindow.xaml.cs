using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace AiryPdf;

public partial class MainWindow : Window
{
    private sealed class TabState(PdfDocument document)
    {
        public PdfDocument Document = document;
        public int Page;
        public double Zoom = 1;

        public double ScrollOffset;
    }
    private sealed class MarkdownTabState(string path, System.Windows.Documents.FlowDocument document)
    {
        public string Path = path;
        public System.Windows.Documents.FlowDocument Document = document;
    }
    private TabState? Current => (Tabs.SelectedItem as TabItem)?.Tag as TabState;
    private MarkdownTabState? CurrentMarkdown => (Tabs.SelectedItem as TabItem)?.Tag as MarkdownTabState;
    private readonly DispatcherTimer zoomTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private sealed class PageView
    {
        public Grid Surface = new() { Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 24), UseLayoutRounding = true, SnapsToDevicePixels = true };
        public Image Image = new() { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        public int RenderWidth;
    }
    private readonly List<PageView> pageViews = [];
    private Grid PageSurface => pageViews[Current!.Page].Surface;
    private bool changingLayout;
    private int renderVersion;
    private bool opening;
    private long zoomPauseUntil;

    public MainWindow()
    {
        InitializeComponent();
        WindowPreferences.Restore(this);
        zoomTimer.Tick += async (_, _) => { zoomTimer.Stop(); await RenderVisible(); };
        DpiChanged += (_, _) => { zoomTimer.Stop(); zoomTimer.Start(); };
    }
    private void Error(Exception ex) => MessageBox.Show(this, ex.Message, "airyPDF", MessageBoxButton.OK, MessageBoxImage.Warning);
    public async void OpenPaths(IEnumerable<string> paths) => await OpenPathsAsync(paths);
    public async Task OpenPathsAsync(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            try
            {
                if (string.Equals(System.IO.Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase) || string.Equals(System.IO.Path.GetExtension(path), ".markdown", StringComparison.OrdinalIgnoreCase))
                {
                    var markdown = new MarkdownTabState(path, MarkdownRenderer.Build(await File.ReadAllTextAsync(path)));
                    var markdownTab = new TabItem { Header = System.IO.Path.GetFileName(path), ToolTip = path, Tag = markdown };
                    opening = true; Tabs.Items.Add(markdownTab); Tabs.SelectedItem = markdownTab; opening = false;
                    await RenderCurrent();
                    continue;
                }
                Status.Text = "PDFを読み込んでいます…";
                PdfDocument doc;
                string? password = null;
                while (true)
                {
                    try { doc = await Task.Run(() => new PdfDocument(path, password)); break; }
                    catch (PdfPasswordException)
                    {
                        password = TextPrompt.Ask(this, "PDFのパスワード", password == null ? "開くためのパスワードを入力してください。" : "パスワードが正しくありません。再入力してください。", true);
                        if (password == null) return;
                    }
                }
                var state = new TabState(doc);
                var tab = new TabItem { Header = System.IO.Path.GetFileName(path), ToolTip = path, Tag = state };
                opening = true; Tabs.Items.Add(tab); Tabs.SelectedItem = tab; opening = false;
                await RenderCurrent();
            }
            catch (Exception ex) { Error(ex); Status.Text = "PDFを開けませんでした。"; }
        }
    }
    private void OpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PDF・Markdown|*.pdf;*.md;*.markdown|PDFファイル|*.pdf|Markdownファイル|*.md;*.markdown", Multiselect = true };
        if (dialog.ShowDialog(this) == true) OpenPaths(dialog.FileNames);
    }
    private void FilesDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) OpenPaths(files.Where(x => new[] { ".pdf", ".md", ".markdown" }.Contains(System.IO.Path.GetExtension(x), StringComparer.OrdinalIgnoreCase)));
    }
    private async void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Tabs && !opening) await RenderCurrent();
    }
    private async Task RenderCurrent()
    {
        ++renderVersion;
        var state = Current;
        var markdown = CurrentMarkdown;
        changingLayout = true;
        PagesHost.Children.Clear(); pageViews.Clear();
        Welcome.Visibility = state == null && markdown == null ? Visibility.Visible : Visibility.Collapsed;
        Viewer.Visibility = state != null ? Visibility.Visible : Visibility.Collapsed;
        MarkdownViewer.Visibility = markdown != null ? Visibility.Visible : Visibility.Collapsed;
        DocumentToolbar.Visibility = state != null ? Visibility.Visible : Visibility.Collapsed;
        PrintButton.Visibility = markdown == null ? Visibility.Visible : Visibility.Collapsed;
        ContentGrid.Background = markdown != null ? Brushes.White : new SolidColorBrush(Color.FromRgb(188, 195, 204));
        MarkdownViewer.Document = markdown?.Document;
        try
        {
            if (markdown != null) { Status.Text = $"{System.IO.Path.GetFileName(markdown.Path)}  ·  読み取り専用"; return; }
            if (state == null) return;
            for (int i = 0; i < state.Document.Count; i++)
            {
                var view = new PageView();
                RenderOptions.SetBitmapScalingMode(view.Image, BitmapScalingMode.NearestNeighbor);
                view.Surface.Tag = i;
                view.Surface.Children.Add(view.Image);
                pageViews.Add(view); PagesHost.Children.Add(view.Surface);
            }
            SizePages(); Viewer.UpdateLayout();
            Viewer.ScrollToVerticalOffset(state.ScrollOffset);
            Viewer.UpdateLayout(); UpdatePageInfo();
        }
        finally { changingLayout = false; }
        await RenderVisible();
    }
    private void SizePages()
    {
        if (Current is not { } state) return;
        for (int i = 0; i < pageViews.Count; i++)
        {
            Size mm = state.Document.SizeMm(i);
            pageViews[i].Surface.Width = mm.Width * 96 / 25.4 * state.Zoom;
            pageViews[i].Surface.Height = mm.Height * 96 / 25.4 * state.Zoom;
        }
    }
    private void UpdatePageInfo()
    {
        if (Current is not { } state) return;
        Size mm = state.Document.SizeMm(state.Page);
        PageNumber.Text = (state.Page + 1).ToString(); PageCount.Text = $"/ {state.Document.Count}";
        if (!ZoomText.IsKeyboardFocusWithin) ZoomText.Text = (state.Zoom * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        Status.Text = $"{System.IO.Path.GetFileName(state.Document.Path)}  ·  {mm.Width:F1} × {mm.Height:F1} mm  ·  印刷倍率 {state.Document.PrintPercent:0.##}%";
    }
    private void ViewerScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (changingLayout || Current is not { } state || pageViews.Count == 0) return;
        state.ScrollOffset = Viewer.VerticalOffset;
        if (!changingLayout)
        {
            double marker = Math.Min(200, Viewer.ViewportHeight / 3);
            int page = pageViews.FindIndex(v => v.Surface.TranslatePoint(new Point(0, v.Surface.Height), Viewer).Y > marker);
            if (page >= 0 && page != state.Page) { state.Page = page; }
            UpdatePageInfo();
        }
        zoomTimer.Stop(); zoomTimer.Start();
    }
    private async Task RenderVisible()
    {
        int version = ++renderVersion;
        if (Current is not { } state) return;
        try
        {
            // 全ページの画像を保持せず、画面付近だけ描画して大きなPDFのメモリを抑える。
            var visible = pageViews.Select((v, i) => (View: v, Page: i))
                .Where(x => { double top = x.View.Surface.TranslatePoint(new Point(), Viewer).Y;
                    return top < Viewer.ViewportHeight + 300 && top + x.View.Surface.Height > -300; }).ToArray();
            foreach (var view in pageViews.Except(visible.Select(x => x.View))) { view.Image.Source = null; view.RenderWidth = 0; }
            foreach (var item in visible)
            {
                var surface = item.View.Surface;
                double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                double factor = Math.Min(dpi, Math.Sqrt(12_000_000.0 / Math.Max(1, visible.Length) / (surface.ActualWidth * surface.ActualHeight)));
                int w = Math.Max(1, (int)Math.Round(surface.ActualWidth * factor)), h = Math.Max(1, (int)Math.Round(surface.ActualHeight * factor));
                if (item.View.RenderWidth == w && item.View.Image.Source != null) continue;
                var bitmap = await Task.Run(() => state.Document.Render(item.Page, w, h, lcdText: true));
                if (version != renderVersion || Current != state) return;
                item.View.Image.Source = bitmap; item.View.RenderWidth = w;
            }
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { if (version == renderVersion) Error(ex); }
    }
    private void GoPage(int page)
    {
        if (Current is not { } state || pageViews.Count == 0) return;
        state.Page = Math.Clamp(page, 0, state.Document.Count - 1);
        Viewer.ScrollToVerticalOffset(PageSurface.TranslatePoint(new Point(), PagesHost).Y + 24);
        UpdatePageInfo();
    }
    private void PreviousClick(object s, RoutedEventArgs e) => GoPage((Current?.Page ?? 0) - 1);
    private void NextClick(object s, RoutedEventArgs e) => GoPage((Current?.Page ?? 0) + 1);
    private void PageNumberKeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter && int.TryParse(PageNumber.Text, out int n)) GoPage(n - 1); }
    private void Zoom(double factor, Point? anchor = null)
    {
        if (Current is not { } state) return;
        ++renderVersion;
        zoomPauseUntil = 0;
        double old = state.Zoom; state.Zoom = Math.Clamp(old * factor, .1, 8);
        if (Math.Abs(state.Zoom - 1) < 1e-10) state.Zoom = 1;
        double ratio = state.Zoom / old;
        Point point = anchor ?? new Point(Viewer.ViewportWidth / 2, Viewer.ViewportHeight / 2);
        var anchorView = pageViews.FirstOrDefault(v => v.Surface.TranslatePoint(new Point(0, v.Surface.Height), Viewer).Y > point.Y) ?? pageViews[^1];
        Point pageAnchor = Viewer.TranslatePoint(point, anchorView.Surface);
        changingLayout = true;
        SizePages(); if (!ZoomText.IsKeyboardFocusWithin) ZoomText.Text = (state.Zoom * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        Viewer.UpdateLayout();
        Point moved = anchorView.Surface.TranslatePoint(new Point(pageAnchor.X * ratio, pageAnchor.Y * ratio), Viewer);
        Viewer.ScrollToHorizontalOffset(Viewer.HorizontalOffset + moved.X - point.X);
        Viewer.ScrollToVerticalOffset(Viewer.VerticalOffset + moved.Y - point.Y);
        Viewer.UpdateLayout(); changingLayout = false;
        state.ScrollOffset = Viewer.VerticalOffset;
        zoomTimer.Stop(); zoomTimer.Start();
    }
    private void ZoomInputGotFocus(object s, KeyboardFocusChangedEventArgs e) => ZoomText.SelectAll();
    private void ApplyZoomInput()
    {
        if (Current is not { } state) return;
        string input = ZoomText.Text.Trim().TrimEnd('%', '％');
        if ((double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double percent) ||
            double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out percent)) && double.IsFinite(percent) && percent >= 10 && percent <= 800)
            Zoom(percent / 100 / state.Zoom);
        else Status.Text = "表示倍率は10～800％で入力してください。";
        ZoomText.Text = (state.Zoom * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
    private void ZoomInputKeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { ApplyZoomInput(); e.Handled = true; } }
    private void ZoomInputLostFocus(object s, KeyboardFocusChangedEventArgs e) => ApplyZoomInput();
    private void ZoomInClick(object s, RoutedEventArgs e) => StepZoom(1.2);
    private void ZoomOutClick(object s, RoutedEventArgs e) => StepZoom(1 / 1.2);
    private void FitClick(object s, RoutedEventArgs e) { if (Current is not null && PageSurface.Width > 0) Zoom((Viewer.ViewportWidth - 56) / PageSurface.Width); }
    private void StepZoom(double factor, Point? anchor = null)
    {
        if (Current is not { } state || Environment.TickCount64 < zoomPauseUntil) return;
        double target = state.Zoom * factor;
        bool stopAt100 = (state.Zoom < 1 && target >= 1) || (state.Zoom > 1 && target <= 1);
        if (stopAt100) target = 1;
        Zoom(target / state.Zoom, anchor);
        // 連続ホイールやボタン連打でも原寸の停止を見失わない。
        if (stopAt100) zoomPauseUntil = Environment.TickCount64 + 450;
        ZoomText.Text = (state.Zoom * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
    internal void ZoomByWheel(int delta, Point? anchor = null)
    {
        if (delta != 0) StepZoom(delta > 0 ? 1.12 : 1 / 1.12, anchor);
    }
    private void ViewerWheel(object s, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { ZoomByWheel(e.Delta, e.GetPosition(Viewer)); e.Handled = true; }
    }
    private async void Rotate(int delta)
    {
        if (Current is not { } state) return;
        try
        {
            state.Document.Rotate(state.Page, delta);
            UpdateTabTitle(state); await RenderCurrent();
        }
        catch (Exception ex) { Error(ex); }
    }
    private void RotateLeftClick(object s, RoutedEventArgs e) => Rotate(-1);
    private void RotateRightClick(object s, RoutedEventArgs e) => Rotate(1);
    private void UpdateTabTitle(TabState state)
    {
        foreach (TabItem tab in Tabs.Items) if (tab.Tag == state) tab.Header = System.IO.Path.GetFileName(state.Document.Path) + (state.Document.Dirty ? " *" : "");
    }
    private bool Save(TabState state)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PDFファイル|*.pdf", FileName = System.IO.Path.GetFileNameWithoutExtension(state.Document.Path) + "_編集.pdf", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return false;
        try { state.Document.SaveCopy(dialog.FileName); UpdateTabTitle(state); Status.Text = $"保存しました: {dialog.FileName}"; return true; }
        catch (Exception ex) { Error(ex); return false; }
    }
    private void SaveClick(object s, RoutedEventArgs e) { if (Current is { } state) Save(state); }
    private bool CanClose(TabState state)
    {
        if (!state.Document.Dirty) return true;
        var result = MessageBox.Show(this, $"{System.IO.Path.GetFileName(state.Document.Path)} の変更を保存しますか？", "未保存の変更", MessageBoxButton.YesNoCancel);
        return result == MessageBoxResult.No || result == MessageBoxResult.Yes && Save(state);
    }
    private void CloseClick(object s, RoutedEventArgs e)
    {
        if (Tabs.SelectedItem is not TabItem tab) return;
        if (tab.Tag is TabState state && !CanClose(state)) return;
        ++renderVersion; Tabs.Items.Remove(tab);
        if (tab.Tag is TabState pdf) pdf.Document.Dispose();
    }
    private void WindowClosing(object? s, CancelEventArgs e)
    {
        var states = Tabs.Items.Cast<TabItem>().Select(t => t.Tag).OfType<TabState>().ToArray();
        if (states.Any(state => !CanClose(state))) { e.Cancel = true; return; }
        WindowPreferences.Save(this);
        ++renderVersion; zoomTimer.Stop(); foreach (var state in states) state.Document.Dispose();
    }
    private void PrintClick(object s, RoutedEventArgs e)
    {
        if (Current is not { } state) return;
        try { new PrintWindow(state.Document, state.Page, null) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { Error(ex); }
    }
    private void CalibrationClick(object s, RoutedEventArgs e)
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Samples", "print-check.pdf");
        if (!File.Exists(path)) { Error(new IOException("寸法確認用PDFが見つかりません。実行用フォルダ全体を使用してください。")); return; }
        OpenPaths([path]);
    }
    private void HelpClick(object s, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "airyPDF 1.1\n\nPDFを開く：Ctrl＋O、またはドラッグ＆ドロップ\nページ移動：ホイールで連続スクロール、ページ番号入力、左右のボタン\n拡大縮小：Ctrl＋ホイール、＋／−、倍率の手入力、画面幅に合わせる\n印刷：Ctrl＋P\n入力・注釈・検索・署名：Ctrl＋F、Ctrl＋F\nパスワードはファイルを開く際に入力します。保存・ログには残しません。\n\n新しいPDFの印刷倍率は100%。指定倍率では自動縮小せず、欠けをプレビューで知らせます。\nドライバー側の拡大縮小・Nアップは無効にしてください。\n回転を保存するときは別名保存します。\n\n寸法確認用PDFには縦横100mmの基準線があります。\n会社での印刷は利用者評価で用途上合格（約0.1mmのずれに見えるとの報告）。",
            "airyPDF — 使い方", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void ToolsClick(object sender, RoutedEventArgs e)
    {
        if (Current is not { } state) return;
        new PdfToolsWindow(state.Document, state.Page, GoPage, async path => await OpenPathsAsync([path])) { Owner = this }.ShowDialog();
    }
    private void WindowKeyDown(object s, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.O) { OpenClick(s, e); e.Handled = true; }
            if (e.Key == Key.P) { PrintClick(s, e); e.Handled = true; }
            if (e.Key == Key.S) { SaveClick(s, e); e.Handled = true; }
            if (e.Key == Key.F) { ToolsClick(s, e); e.Handled = true; }
            if (e.Key == Key.W) { CloseClick(s, e); e.Handled = true; }
        }
    }
}
