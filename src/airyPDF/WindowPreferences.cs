using System.Text.Json;
namespace AiryPdf;
public static class WindowPreferences
{
    private sealed record Preferences(double Width, double Height, bool Maximized);
    private static string SettingsPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "airyPDF", "window.json");
    public static void Restore(Window window)
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var saved = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsPath));
            if (saved == null || !double.IsFinite(saved.Width) || !double.IsFinite(saved.Height)) return;
            window.Width = Math.Clamp(saved.Width, window.MinWidth, Math.Max(window.MinWidth, SystemParameters.WorkArea.Width));
            window.Height = Math.Clamp(saved.Height, window.MinHeight, Math.Max(window.MinHeight, SystemParameters.WorkArea.Height));
            if (saved.Maximized) window.WindowState = WindowState.Maximized;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }
    public static void Save(Window window)
    {
        try
        {
            var bounds = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Preferences(bounds.Width, bounds.Height, window.WindowState == WindowState.Maximized)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
