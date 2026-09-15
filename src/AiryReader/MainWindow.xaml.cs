using System.ComponentModel;
using System.Text;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;

namespace AiryReader;

public partial class MainWindow : Window
{
    private sealed class TabState(PdfDocument document)
    {
        public PdfDocument Document = document;
        public int Page;
        public double Zoom = 1;

        public double ScrollOffset;
    }
    private sealed class TextTabState(string path, System.Windows.Documents.FlowDocument document, string text, Encoding encoding, bool editable)
    {
        public string Path = path;
        public System.Windows.Documents.FlowDocument Document = document;
        public string Text = text;
        public Encoding Encoding = encoding;
        public bool Editable = editable;
        public bool Dirty;
        public double Zoom = 1;
    }
    private TabState? Current => (Tabs.SelectedItem as TabItem)?.Tag as TabState;
    private TextTabState? CurrentText => (Tabs.SelectedItem as TabItem)?.Tag as TextTabState;
    private sealed class ImageTabState(string path, BitmapSource image)
    {
        public string Path = path; public BitmapSource Image = image;
        public double Zoom = 1;
        public int Rotation;
    }
    private ImageTabState? CurrentImage => (Tabs.SelectedItem as TabItem)?.Tag as ImageTabState;
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
    private readonly List<System.Windows.Documents.Run> textMatches = [];
    private readonly List<int> textEditorMatches = [];
    private int textEditorQueryLength;
    private int textMatchIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        WindowPreferences.Restore(this);
        zoomTimer.Tick += async (_, _) => { zoomTimer.Stop(); await RenderVisible(); };
        DpiChanged += (_, _) => { zoomTimer.Stop(); zoomTimer.Start(); };
    }
    private void Error(Exception ex) => MessageBox.Show(this, ex.Message, "AiryReader", MessageBoxButton.OK, MessageBoxImage.Warning);
    public async void NewText() => await NewTextAsync();
    private void NewTextClick(object s, RoutedEventArgs e) => NewText();
    private TabItem CreateTab(string title, string toolTip, object state)
    {
        var tab = new TabItem { ToolTip = toolTip, Tag = state };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Content = "×", Tag = tab, ToolTip = "タブを閉じる  Ctrl+W", FontSize = 14, Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(7, 0, -3, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        close.Click += CloseTabClick; header.Children.Add(close); tab.Header = header;
        return tab;
    }
    private static void SetTabTitle(TabItem tab, string title)
    {
        if (tab.Header is StackPanel header && header.Children.OfType<TextBlock>().FirstOrDefault() is { } label) label.Text = title;
    }
    internal int TabCountForTest => Tabs.Items.Count;
    internal async Task NewTextAsync()
    {
        var state = new TextTabState("", LightweightTextRenderer.BuildPlain(""), "", new UTF8Encoding(false), true);
        var tab = CreateTab("無題.txt", "新しいテキスト", state);
        opening = true; Tabs.Items.Add(tab); Tabs.SelectedItem = tab; opening = false;
        await RenderCurrent(); TextEditor.Focus();
    }
    public async void OpenPaths(IEnumerable<string> paths) => await OpenPathsAsync(paths);
    public async Task OpenPathsAsync(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            try
            {
                string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (new[] { ".md", ".markdown", ".txt" }.Contains(extension))
                {
                    var loaded = await ReadTextAsync(path);
                    string text = loaded.Text;
                    var document = extension == ".txt" ? LightweightTextRenderer.BuildPlain(text) : LightweightTextRenderer.Build(text);
                    var reader = new TextTabState(path, document, text, loaded.Encoding, extension == ".txt");
                    var readerTab = CreateTab(System.IO.Path.GetFileName(path), path, reader);
                    opening = true; Tabs.Items.Add(readerTab); Tabs.SelectedItem = readerTab; opening = false;
                    await RenderCurrent(); continue;
                }
                if (new[] { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp" }.Contains(extension))
                {
                    byte[] bytes = await File.ReadAllBytesAsync(path);
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    using (var stream = new MemoryStream(bytes)) { bitmap.StreamSource = stream; bitmap.EndInit(); }
                    bitmap.Freeze();
                    var image = new ImageTabState(path, bitmap);
                    var imageTab = CreateTab(System.IO.Path.GetFileName(path), path, image);
                    opening = true; Tabs.Items.Add(imageTab); Tabs.SelectedItem = imageTab; opening = false;
                    await RenderCurrent(); continue;
                }                Status.Text = "PDFを読み込んでいます…";
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
                var tab = CreateTab(System.IO.Path.GetFileName(path), path, state);
                opening = true; Tabs.Items.Add(tab); Tabs.SelectedItem = tab; opening = false;
                await RenderCurrent();
            }
            catch (Exception ex) { Error(ex); Status.Text = "ファイルを開けませんでした。"; }
        }
    }
    private static async Task<(string Text, Encoding Encoding)> ReadTextAsync(string path)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path);
        try { var encoding = new UTF8Encoding(false, true); return (encoding.GetString(bytes), new UTF8Encoding(false)); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var encoding = Encoding.GetEncoding(932); return (encoding.GetString(bytes), encoding);
        }
    }    private void OpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "対応ファイル|*.pdf;*.md;*.markdown;*.txt;*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp|PDF|*.pdf|文章|*.md;*.markdown;*.txt|画像|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp", Multiselect = true };
        if (dialog.ShowDialog(this) == true) OpenPaths(dialog.FileNames);
    }
    private void FilesDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) OpenPaths(files.Where(x => new[] { ".pdf", ".md", ".markdown", ".txt", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp" }.Contains(System.IO.Path.GetExtension(x), StringComparer.OrdinalIgnoreCase)));
    }
    private async void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Tabs && !opening) await RenderCurrent();
    }
    private async Task RenderCurrent()
    {
        ++renderVersion;
        var state = Current;
        var textDocument = CurrentText;
        var image = CurrentImage;
        changingLayout = true;
        PagesHost.Children.Clear(); pageViews.Clear();
        ClearTextSearch();
        TextSearchBar.Visibility = Visibility.Collapsed;
        Welcome.Visibility = state == null && textDocument == null && image == null ? Visibility.Visible : Visibility.Collapsed;
        Viewer.Visibility = state != null ? Visibility.Visible : Visibility.Collapsed;
        MarkdownViewer.Visibility = textDocument != null && !textDocument.Editable ? Visibility.Visible : Visibility.Collapsed;
        TextEditor.Visibility = textDocument?.Editable == true ? Visibility.Visible : Visibility.Collapsed;
        SaveTextButton.Visibility = textDocument?.Editable == true ? Visibility.Visible : Visibility.Collapsed;
        ImageViewer.Visibility = image != null ? Visibility.Visible : Visibility.Collapsed;
        DocumentToolbar.Visibility = state != null || textDocument != null || image != null ? Visibility.Visible : Visibility.Collapsed;
        PageControls.Visibility = state != null ? Visibility.Visible : Visibility.Collapsed;
        RotationControls.Visibility = state != null || image != null ? Visibility.Visible : Visibility.Collapsed;
        FitWidthButton.Visibility = state != null || image != null ? Visibility.Visible : Visibility.Collapsed;
        PrintButton.Visibility = state != null ? Visibility.Visible : Visibility.Collapsed;
        ContentGrid.Background = textDocument != null || image != null ? Brushes.White : new SolidColorBrush(Color.FromRgb(188, 195, 204));
        MarkdownViewer.Document = textDocument?.Editable == false ? textDocument.Document : null;
        if (textDocument?.Editable == true && TextEditor.Text != textDocument.Text) TextEditor.Text = textDocument.Text;
        ReaderImage.Source = image?.Image;
        if (textDocument != null) { MarkdownViewer.Zoom = textDocument.Zoom * 100; TextEditor.FontSize = 15 * textDocument.Zoom; }
        if (image != null) ApplyImageLayout(image);
        if (!ZoomText.IsKeyboardFocusWithin && (state != null || textDocument != null || image != null)) ZoomText.Text = (ActiveZoom * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            if (textDocument != null) { UpdateNonPdfStatus(); return; }
            if (image != null) { UpdateNonPdfStatus(); return; }
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
    private double ActiveZoom => Current?.Zoom ?? CurrentImage?.Zoom ?? CurrentText?.Zoom ?? 1;
    private void Zoom(double factor, Point? anchor = null)
    {
        if (Current is { } state) { ZoomPdf(state, factor, anchor); return; }
        if (CurrentImage is { } image) { ZoomImage(image, factor, anchor); return; }
        if (CurrentText is { } text)
        {
            text.Zoom = Math.Clamp(text.Zoom * factor, .1, 8);
            if (Math.Abs(text.Zoom - 1) < 1e-10) text.Zoom = 1;
            MarkdownViewer.Zoom = text.Zoom * 100;
            TextEditor.FontSize = 15 * text.Zoom;
            if (!ZoomText.IsKeyboardFocusWithin) ZoomText.Text = (text.Zoom * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            UpdateNonPdfStatus();
        }
    }
    private void ZoomPdf(TabState state, double factor, Point? anchor)
    {
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
    private void ZoomImage(ImageTabState state, double factor, Point? anchor)
    {
        double old = state.Zoom; state.Zoom = Math.Clamp(old * factor, .1, 8);
        if (Math.Abs(state.Zoom - 1) < 1e-10) state.Zoom = 1;
        double ratio = state.Zoom / old;
        Point point = anchor ?? new Point(ImageViewer.ViewportWidth / 2, ImageViewer.ViewportHeight / 2);
        Point imageAnchor = ImageViewer.TranslatePoint(point, ReaderImage);
        ApplyImageLayout(state); ImageViewer.UpdateLayout();
        Point moved = ReaderImage.TranslatePoint(new Point(imageAnchor.X * ratio, imageAnchor.Y * ratio), ImageViewer);
        ImageViewer.ScrollToHorizontalOffset(ImageViewer.HorizontalOffset + moved.X - point.X);
        ImageViewer.ScrollToVerticalOffset(ImageViewer.VerticalOffset + moved.Y - point.Y);
        if (!ZoomText.IsKeyboardFocusWithin) ZoomText.Text = (state.Zoom * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        UpdateNonPdfStatus();
    }
    private void ApplyImageLayout(ImageTabState state)
    {
        ReaderImage.Width = state.Image.PixelWidth * state.Zoom;
        ReaderImage.Height = state.Image.PixelHeight * state.Zoom;
        ReaderImage.LayoutTransform = new RotateTransform(state.Rotation);
        RenderOptions.SetBitmapScalingMode(ReaderImage, state.Zoom >= 1 ? BitmapScalingMode.HighQuality : BitmapScalingMode.Fant);
    }
    private void UpdateNonPdfStatus()
    {
        if (CurrentText is { } text) Status.Text = text.Editable ? $"{(string.IsNullOrEmpty(text.Path) ? "無題.txt" : System.IO.Path.GetFileName(text.Path))}  ·  編集可能  ·  Ctrl＋Sで保存" : $"{System.IO.Path.GetFileName(text.Path)}  ·  表示 {text.Zoom * 100:0.##}%  ·  Ctrl＋Fで検索";
        else if (CurrentImage is { } image) Status.Text = $"{System.IO.Path.GetFileName(image.Path)}  ·  {image.Image.PixelWidth} × {image.Image.PixelHeight} px  ·  表示 {image.Zoom * 100:0.##}%";
    }
    private void ZoomInputGotFocus(object s, KeyboardFocusChangedEventArgs e) => ZoomText.SelectAll();
    private void ApplyZoomInput()
    {
        if (Current == null && CurrentImage == null && CurrentText == null) return;
        string input = ZoomText.Text.Trim().TrimEnd('%', '％');
        if ((double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double percent) ||
            double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out percent)) && double.IsFinite(percent) && percent >= 10 && percent <= 800)
            Zoom(percent / 100 / ActiveZoom);
        else Status.Text = "表示倍率は10～800％で入力してください。";
        ZoomText.Text = (ActiveZoom * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
    private void ZoomInputKeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { ApplyZoomInput(); e.Handled = true; } }
    private void ZoomInputLostFocus(object s, KeyboardFocusChangedEventArgs e) => ApplyZoomInput();
    private void ZoomInClick(object s, RoutedEventArgs e) => StepZoom(1.2);
    private void ZoomOutClick(object s, RoutedEventArgs e) => StepZoom(1 / 1.2);
    private void FitClick(object s, RoutedEventArgs e)
    {
        if (Current is not null && PageSurface.Width > 0) Zoom((Viewer.ViewportWidth - 56) / PageSurface.Width);
        else if (CurrentImage is { } image)
        {
            bool side = Math.Abs(image.Rotation) % 180 == 90;
            double width = side ? image.Image.PixelHeight : image.Image.PixelWidth, height = side ? image.Image.PixelWidth : image.Image.PixelHeight;
            double target = Math.Min((ImageViewer.ViewportWidth - 24) / Math.Max(1, width), (ImageViewer.ViewportHeight - 24) / Math.Max(1, height));
            Zoom(Math.Clamp(target, .1, 8) / image.Zoom);
        }
    }
    private void StepZoom(double factor, Point? anchor = null)
    {
        if ((Current == null && CurrentImage == null && CurrentText == null) || Environment.TickCount64 < zoomPauseUntil) return;
        double current = ActiveZoom, target = current * factor;
        bool stopAt100 = (current < 1 && target >= 1) || (current > 1 && target <= 1);
        if (stopAt100) target = 1;
        Zoom(target / current, anchor);
        if (stopAt100) zoomPauseUntil = Environment.TickCount64 + 450;
        ZoomText.Text = (ActiveZoom * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
    internal void ZoomByWheel(int delta, Point? anchor = null)
    {
        if (delta != 0) StepZoom(delta > 0 ? 1.12 : 1 / 1.12, anchor);
    }
    internal double ActiveZoomForTest => ActiveZoom;
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
    internal double MarkdownOffset => FindVisualChild<ScrollViewer>(MarkdownViewer)?.VerticalOffset ?? 0;
    internal void ScrollMarkdownByWheel(int delta)
    {
        if (FindVisualChild<ScrollViewer>(MarkdownViewer) is not { } scroll || delta == 0) return;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset - Math.Sign(delta) * 180);
    }
    private void MarkdownWheel(object s, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) ZoomByWheel(e.Delta);
        else ScrollMarkdownByWheel(e.Delta);
        e.Handled = true;
    }
    private void ImageWheel(object s, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { ZoomByWheel(e.Delta, e.GetPosition(ImageViewer)); e.Handled = true; }
    }
    private void ImageClick(object s, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && CurrentImage is { } image) { if (Math.Abs(image.Zoom - 1) < .001) FitClick(s, e); else Zoom(1 / image.Zoom, e.GetPosition(ImageViewer)); e.Handled = true; }
    }
    private void ViewerWheel(object s, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { ZoomByWheel(e.Delta, e.GetPosition(Viewer)); e.Handled = true; }
    }
    private async void Rotate(int delta)
    {
        if (CurrentImage is { } image)
        {
            image.Rotation = (image.Rotation + delta * 90) % 360;
            ApplyImageLayout(image); UpdateNonPdfStatus(); return;
        }
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
        foreach (TabItem tab in Tabs.Items) if (tab.Tag == state) SetTabTitle(tab, System.IO.Path.GetFileName(state.Document.Path) + (state.Document.Dirty ? " *" : ""));
    }
    private bool Save(TabState state)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PDFファイル|*.pdf", FileName = System.IO.Path.GetFileNameWithoutExtension(state.Document.Path) + "_編集.pdf", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return false;
        try { state.Document.SaveCopy(dialog.FileName); UpdateTabTitle(state); Status.Text = $"保存しました: {dialog.FileName}"; return true; }
        catch (Exception ex) { Error(ex); return false; }
    }
    private void SaveClick(object s, RoutedEventArgs e)
    {
        if (Current is { } state) Save(state);
        else if (CurrentText is { Editable: true } text) SaveText(text);
    }
    private void SaveTextClick(object s, RoutedEventArgs e) { if (CurrentText is { Editable: true } text) SaveText(text); }
    internal bool SaveTextForTest() => CurrentText is { Editable: true } text && SaveText(text);
    private bool SaveText(TextTabState state, bool saveAs = false)
    {
        string destination = state.Path;
        if (saveAs || string.IsNullOrEmpty(destination))
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "テキストファイル|*.txt", FileName = string.IsNullOrEmpty(destination) ? "無題.txt" : System.IO.Path.GetFileName(destination), OverwritePrompt = true };
            if (dialog.ShowDialog(this) != true) return false;
            destination = dialog.FileName;
        }
        return SaveTextToPath(state, destination);
    }
    private bool SaveTextToPath(TextTabState state, string destination)
    {
        try
        {
            File.WriteAllText(destination, TextEditor.Text, state.Encoding);
            state.Path = destination; state.Text = TextEditor.Text; state.Dirty = false; UpdateTextTabTitle(state);
            foreach (TabItem tab in Tabs.Items) if (tab.Tag == state) tab.ToolTip = destination;
            Status.Text = $"保存しました: {destination}"; return true;
        }
        catch (Exception ex) { Error(ex); return false; }
    }
    internal bool SaveTextToPathForTest(string destination) => CurrentText is { Editable: true } text && SaveTextToPath(text, destination);
    private void TextEditorChanged(object s, TextChangedEventArgs e)
    {
        if (opening || CurrentText is not { Editable: true } state) return;
        state.Dirty = TextEditor.Text != state.Text; UpdateTextTabTitle(state);
    }
    private void UpdateTextTabTitle(TextTabState state)
    {
        foreach (TabItem tab in Tabs.Items) if (tab.Tag == state) SetTabTitle(tab, (string.IsNullOrEmpty(state.Path) ? "無題.txt" : System.IO.Path.GetFileName(state.Path)) + (state.Dirty ? " *" : ""));
    }
    private bool CanClose(TextTabState state)
    {
        if (!state.Editable || !state.Dirty) return true;
        var result = MessageBox.Show(this, $"{System.IO.Path.GetFileName(state.Path)} の変更を保存しますか？", "未保存の変更", MessageBoxButton.YesNoCancel);
        return result == MessageBoxResult.No || result == MessageBoxResult.Yes && SaveText(state);
    }
    private bool CanClose(TabState state)
    {
        if (!state.Document.Dirty) return true;
        var result = MessageBox.Show(this, $"{System.IO.Path.GetFileName(state.Document.Path)} の変更を保存しますか？", "未保存の変更", MessageBoxButton.YesNoCancel);
        return result == MessageBoxResult.No || result == MessageBoxResult.Yes && Save(state);
    }
    private void CloseTabClick(object s, RoutedEventArgs e)
    {
        if (s is Button { Tag: TabItem tab }) CloseTab(tab);
        e.Handled = true;
    }
    private void CloseClick(object s, RoutedEventArgs e)
    {
        if (Tabs.SelectedItem is TabItem tab) CloseTab(tab);
    }
    private void CloseTab(TabItem tab)
    {
        if (tab.Tag is TabState state && !CanClose(state)) return;
        if (tab.Tag is TextTabState text && !CanClose(text)) return;
        ++renderVersion; Tabs.Items.Remove(tab);
        if (tab.Tag is TabState pdf) pdf.Document.Dispose();
    }
    private void WindowClosing(object? s, CancelEventArgs e)
    {
        var states = Tabs.Items.Cast<TabItem>().Select(t => t.Tag).OfType<TabState>().ToArray();
        var textStates = Tabs.Items.Cast<TabItem>().Select(t => t.Tag).OfType<TextTabState>().ToArray();
        if (states.Any(state => !CanClose(state)) || textStates.Any(state => !CanClose(state))) { e.Cancel = true; return; }
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
            "AiryReader 1.2.8\n\n対応形式：PDF、Markdown、TXT、JPEG、PNG、TIFF、BMP\nファイルを開く：Ctrl＋O、またはドラッグ＆ドロップ\nページ移動：ホイールで連続スクロール、ページ番号入力、左右のボタン\nPDF・画像の拡大縮小：Ctrl＋ホイール、＋／−、倍率入力、画面幅に合わせる\n画像：回転アイコン、ダブルクリックで100％／画面内表示\nMarkdown：Ctrl＋ホイールで文字倍率、Ctrl＋Fで検索、選択・コピー\nTXT：単体起動で新しいメモ、＋またはCtrl＋Tでタブ追加、×またはCtrl＋Wで閉じる、Ctrl＋Sで保存、Ctrl＋Fで検索\n共通：Ctrl＋0で100％、Ctrl＋＋／－で倍率変更\n印刷：Ctrl＋P\nPDFの入力・注釈・検索・署名確認：Ctrl＋F\nパスワードはファイルを開く際に入力します。保存・ログには残しません。\n\n新しいPDFの印刷倍率は100%。指定倍率では自動縮小せず、欠けをプレビューで知らせます。\nドライバー側の拡大縮小・Nアップは無効にしてください。\n回転を保存するときは別名保存します。\n\n寸法確認用PDFには縦横100mmの基準線があります。\n会社での印刷は利用者評価で用途上合格（約0.1mmのずれに見えるとの報告）。",
            "AiryReader — 使い方", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void ToolsClick(object sender, RoutedEventArgs e)
    {
        if (Current is not { } state) return;
        new PdfToolsWindow(state.Document, state.Page, GoPage, async path => await OpenPathsAsync([path])) { Owner = this }.ShowDialog();
    }
    private static IEnumerable<System.Windows.Documents.Run> FindRuns(DependencyObject parent)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is System.Windows.Documents.Run run) yield return run;
            else if (child is DependencyObject nested) foreach (var item in FindRuns(nested)) yield return item;
        }
    }
    private void ClearTextSearch()
    {
        foreach (var run in textMatches) run.Background = null;
        textMatches.Clear(); textEditorMatches.Clear(); textEditorQueryLength = 0; textMatchIndex = -1;
        if (TextSearchCount != null) TextSearchCount.Text = "";
    }
    private void ShowTextSearch()
    {
        if (CurrentText == null) return;
        TextSearchBar.Visibility = Visibility.Visible;
        TextSearchInput.Focus(); TextSearchInput.SelectAll();
    }
    private void SearchText(bool forward)
    {
        if (CurrentText is not { } state) return;
        string query = TextSearchInput.Text;
        ClearTextSearch();
        if (string.IsNullOrWhiteSpace(query)) return;
        if (state.Editable)
        {
            textEditorQueryLength = query.Length;
            for (int at = 0; at <= TextEditor.Text.Length - query.Length;)
            {
                int found = TextEditor.Text.IndexOf(query, at, StringComparison.CurrentCultureIgnoreCase);
                if (found < 0) break;
                textEditorMatches.Add(found); at = found + Math.Max(1, query.Length);
            }
            if (textEditorMatches.Count == 0) { TextSearchCount.Text = "0件"; return; }
            textMatchIndex = forward ? 0 : textEditorMatches.Count - 1; ShowTextMatch(); return;
        }
        textMatches.AddRange(FindRuns(state.Document).Where(run => run.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)));
        foreach (var run in textMatches) run.Background = new SolidColorBrush(Color.FromRgb(255, 240, 150));
        if (textMatches.Count == 0) { TextSearchCount.Text = "0件"; return; }
        textMatchIndex = forward ? 0 : textMatches.Count - 1;
        ShowTextMatch();
    }
    private void MoveTextMatch(int delta)
    {
        int count = CurrentText?.Editable == true ? textEditorMatches.Count : textMatches.Count;
        if (count == 0) { SearchText(delta >= 0); return; }
        if (CurrentText?.Editable != true) textMatches[textMatchIndex].Background = new SolidColorBrush(Color.FromRgb(255, 240, 150));
        textMatchIndex = (textMatchIndex + delta + count) % count;
        ShowTextMatch();
    }
    private void ShowTextMatch()
    {
        if (CurrentText?.Editable == true)
        {
            TextEditor.Focus(); TextEditor.Select(textEditorMatches[textMatchIndex], textEditorQueryLength);
            TextEditor.ScrollToLine(TextEditor.GetLineIndexFromCharacterIndex(textEditorMatches[textMatchIndex]));
            TextSearchCount.Text = $"{textMatchIndex + 1} / {textEditorMatches.Count}"; return;
        }
        var run = textMatches[textMatchIndex];
        run.Background = new SolidColorBrush(Color.FromRgb(255, 190, 80));
        run.BringIntoView();
        TextSearchCount.Text = $"{textMatchIndex + 1} / {textMatches.Count}";
    }
    private void TextSearchKeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) MoveTextMatch(-1); else SearchText(true); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseTextSearch(s, e); e.Handled = true; }
    }
    internal int SearchTextForTest(string query) { ShowTextSearch(); TextSearchInput.Text = query; SearchText(true); return CurrentText?.Editable == true ? textEditorMatches.Count : textMatches.Count; }
    private void TextSearchGotFocus(object s, KeyboardFocusChangedEventArgs e) => TextSearchInput.SelectAll();
    private void PreviousTextMatch(object s, RoutedEventArgs e) => MoveTextMatch(-1);
    private void NextTextMatch(object s, RoutedEventArgs e) => MoveTextMatch(1);
    private void CloseTextSearch(object s, RoutedEventArgs e) { ClearTextSearch(); TextSearchBar.Visibility = Visibility.Collapsed; if (CurrentText?.Editable == true) TextEditor.Focus(); else MarkdownViewer.Focus(); }

    private void WindowKeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.F3 && CurrentText != null) { if (TextSearchBar.Visibility != Visibility.Visible) ShowTextSearch(); else MoveTextMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); e.Handled = true; return; }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
        {
            if (CurrentText is { Editable: true } text) SaveText(text, true);
            e.Handled = true; return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key is Key.N or Key.T) { NewText(); e.Handled = true; }
            if (e.Key == Key.O) { OpenClick(s, e); e.Handled = true; }
            if (e.Key == Key.P) { PrintClick(s, e); e.Handled = true; }
            if (e.Key == Key.S) { SaveClick(s, e); e.Handled = true; }
            if (e.Key == Key.F) { if (CurrentText != null) ShowTextSearch(); else ToolsClick(s, e); e.Handled = true; }
            if (e.Key is Key.Add or Key.OemPlus) { StepZoom(1.2); e.Handled = true; }
            if (e.Key is Key.Subtract or Key.OemMinus) { StepZoom(1 / 1.2); e.Handled = true; }
            if (e.Key is Key.D0 or Key.NumPad0) { if (ActiveZoom > 0) Zoom(1 / ActiveZoom); e.Handled = true; }
            if (e.Key == Key.W) { CloseClick(s, e); e.Handled = true; }
        }
    }
}
