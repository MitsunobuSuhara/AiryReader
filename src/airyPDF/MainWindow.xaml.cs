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
        public Rect? Region;
    }
    private TabState? Current => (Tabs.SelectedItem as TabItem)?.Tag as TabState;
    private readonly DispatcherTimer zoomTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private int renderVersion;
    private bool opening;
    private Point? selectionStart;
    public MainWindow()
    {
        InitializeComponent();
        zoomTimer.Tick += async (_, _) => { zoomTimer.Stop(); await RenderCurrent(); };
    }
    private void Error(Exception ex) => MessageBox.Show(this, ex.Message, "airyPDF", MessageBoxButton.OK, MessageBoxImage.Warning);
    public async void OpenPaths(IEnumerable<string> paths) => await OpenPathsAsync(paths);
    public async Task OpenPathsAsync(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            try
            {
                Status.Text = "PDFを読み込んでいます…";
                var doc = await Task.Run(() => new PdfDocument(path));
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
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PDFファイル|*.pdf", Multiselect = true };
        if (dialog.ShowDialog(this) == true) OpenPaths(dialog.FileNames);
    }
    private void FilesDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) OpenPaths(files.Where(x => string.Equals(System.IO.Path.GetExtension(x), ".pdf", StringComparison.OrdinalIgnoreCase)));
    }
    private async void TabChanged(object sender, SelectionChangedEventArgs e) { if (e.Source == Tabs && !opening) { PageImage.Source = null; Viewer.ScrollToTop(); await RenderCurrent(); } }
    private async Task RenderCurrent()
    {
        int version = ++renderVersion;
        var state = Current;
        Welcome.Visibility = state == null ? Visibility.Visible : Visibility.Collapsed;
        if (state == null) { PageImage.Source = null; PageSurface.Width = PageSurface.Height = 0; return; }
        try
        {
            int page = state.Page;
            Size mm = state.Document.SizeMm(page);
            PageSurface.Width = mm.Width * 96 / 25.4 * state.Zoom;
            PageSurface.Height = mm.Height * 96 / 25.4 * state.Zoom;
            PageNumber.Text = (page + 1).ToString(); PageCount.Text = $"/ {state.Document.Count}"; ZoomText.Text = $"{state.Zoom:P0}";
            DrawSelection();
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double factor = Math.Min(dpi, Math.Sqrt(12_000_000 / (PageSurface.Width * PageSurface.Height)));
            int w = Math.Max(1, (int)(PageSurface.Width * factor)), h = Math.Max(1, (int)(PageSurface.Height * factor));
            var bitmap = await Task.Run(() => state.Document.Render(page, w, h));
            if (version != renderVersion || Current != state) return;
            PageImage.Source = bitmap;
            Status.Text = $"{System.IO.Path.GetFileName(state.Document.Path)}  ·  {mm.Width:F1} × {mm.Height:F1} mm  ·  印刷倍率 {state.Document.PrintPercent:0.##}%" + (state.Region.HasValue ? "  ·  範囲選択中" : "");
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { if (version == renderVersion) Error(ex); }
    }
    private async void GoPage(int page)
    {
        if (Current is not { } state) return;
        state.Page = Math.Clamp(page, 0, state.Document.Count - 1); state.Region = null;
        PageImage.Source = null; Viewer.ScrollToTop(); await RenderCurrent();
    }
    private void PreviousClick(object s, RoutedEventArgs e) => GoPage((Current?.Page ?? 0) - 1);
    private void NextClick(object s, RoutedEventArgs e) => GoPage((Current?.Page ?? 0) + 1);
    private void PageNumberKeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter && int.TryParse(PageNumber.Text, out int n)) GoPage(n - 1); }
    private void Zoom(double factor, Point? anchor = null)
    {
        if (Current is not { } state) return;
        ++renderVersion;
        double old = state.Zoom; state.Zoom = Math.Clamp(old * factor, .1, 8);
        double ratio = state.Zoom / old;
        Point point = anchor ?? new Point(Viewer.ViewportWidth / 2, Viewer.ViewportHeight / 2);
        Point pageAnchor = Viewer.TranslatePoint(point, PageSurface);
        PageSurface.Width *= ratio; PageSurface.Height *= ratio;
        ZoomText.Text = $"{state.Zoom:P0}"; DrawSelection();
        Viewer.UpdateLayout();
        Point moved = PageSurface.TranslatePoint(new Point(pageAnchor.X * ratio, pageAnchor.Y * ratio), Viewer);
        Viewer.ScrollToHorizontalOffset(Viewer.HorizontalOffset + moved.X - point.X);
        Viewer.ScrollToVerticalOffset(Viewer.VerticalOffset + moved.Y - point.Y);
        zoomTimer.Stop(); zoomTimer.Start();
    }
    private void ZoomInClick(object s, RoutedEventArgs e) => Zoom(1.2);
    private void ZoomOutClick(object s, RoutedEventArgs e) => Zoom(1 / 1.2);
    private void FitClick(object s, RoutedEventArgs e) { if (Current is not null && PageSurface.Width > 0) Zoom((Viewer.ViewportWidth - 56) / PageSurface.Width); }
    private void ViewerWheel(object s, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { Zoom(e.Delta > 0 ? 1.12 : 1 / 1.12, e.GetPosition(Viewer)); e.Handled = true; }
    }
    private async void Rotate(int delta)
    {
        if (Current is not { } state) return;
        try
        {
            if (AllPages.IsChecked == true) for (int i = 0; i < state.Document.Count; i++) state.Document.Rotate(i, delta);
            else state.Document.Rotate(state.Page, delta);
            state.Region = null; UpdateTabTitle(state); await RenderCurrent();
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
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PDFファイル|*.pdf", FileName = System.IO.Path.GetFileNameWithoutExtension(state.Document.Path) + "_回転.pdf", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return false;
        try { state.Document.SaveCopy(dialog.FileName); UpdateTabTitle(state); Status.Text = $"保存しました: {dialog.FileName}"; return true; }
        catch (Exception ex) { Error(ex); return false; }
    }
    private void SaveClick(object s, RoutedEventArgs e) { if (Current is { } state) Save(state); }
    private bool CanClose(TabState state)
    {
        if (!state.Document.Dirty) return true;
        var result = MessageBox.Show(this, $"{System.IO.Path.GetFileName(state.Document.Path)} の回転を保存しますか？", "未保存の変更", MessageBoxButton.YesNoCancel);
        return result == MessageBoxResult.No || result == MessageBoxResult.Yes && Save(state);
    }
    private void CloseClick(object s, RoutedEventArgs e)
    {
        if (Current is { } state && CanClose(state)) { var tab = Tabs.SelectedItem; ++renderVersion; Tabs.Items.Remove(tab); state.Document.Dispose(); }
    }
    private void WindowClosing(object? s, CancelEventArgs e)
    {
        var states = Tabs.Items.Cast<TabItem>().Select(t => (TabState)t.Tag).ToArray();
        if (states.Any(state => !CanClose(state))) { e.Cancel = true; return; }
        ++renderVersion; zoomTimer.Stop(); foreach (var state in states) state.Document.Dispose();
    }
    private void SelectionStart(object s, MouseButtonEventArgs e)
    {
        if (SelectRegion.IsChecked != true || Current is null) return;
        selectionStart = e.GetPosition(PageSurface); PageSurface.CaptureMouse(); e.Handled = true;
    }
    private void SelectionMove(object s, MouseEventArgs e)
    {
        if (selectionStart is not { } start || Current is not { } state) return;
        var point = e.GetPosition(PageSurface);
        var rect = new Rect(start, new Point(Math.Clamp(point.X, 0, PageSurface.Width), Math.Clamp(point.Y, 0, PageSurface.Height)));
        double unit = 25.4 / 96 / state.Zoom;
        state.Region = new Rect(rect.X * unit, rect.Y * unit, rect.Width * unit, rect.Height * unit); DrawSelection();
    }
    private void SelectionEnd(object s, MouseButtonEventArgs e)
    {
        selectionStart = null; PageSurface.ReleaseMouseCapture();
        if (Current is { } state && state.Region is { } rect && (rect.Width < 1 || rect.Height < 1)) state.Region = null;
        DrawSelection();
    }
    private void DrawSelection()
    {
        if (Current is not { Region: { } rect } state) { SelectionBox.Visibility = Visibility.Collapsed; return; }
        double unit = 96 / 25.4 * state.Zoom;
        SelectionBox.Visibility = Visibility.Visible; Canvas.SetLeft(SelectionBox, rect.X * unit); Canvas.SetTop(SelectionBox, rect.Y * unit);
        SelectionBox.Width = rect.Width * unit; SelectionBox.Height = rect.Height * unit;
    }
    private void ClearSelectionClick(object s, RoutedEventArgs e) { if (Current is { } state) state.Region = null; DrawSelection(); }
    private void PrintClick(object s, RoutedEventArgs e)
    {
        if (Current is not { } state) return;
        try { new PrintWindow(state.Document, state.Page, state.Region) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { Error(ex); }
    }
    private void WindowKeyDown(object s, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.O) { OpenClick(s, e); e.Handled = true; }
            if (e.Key == Key.P) { PrintClick(s, e); e.Handled = true; }
            if (e.Key == Key.W) { CloseClick(s, e); e.Handled = true; }
        }
    }
}
