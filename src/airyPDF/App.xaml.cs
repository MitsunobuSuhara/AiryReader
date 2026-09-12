namespace AiryPdf;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test") || e.Args.Contains("--ui-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (File.Exists("artifacts/test-failure.txt")) File.Delete("artifacts/test-failure.txt");
            try { if (e.Args.Contains("--ui-test")) await SelfTest.RunUiAsync(); else SelfTest.Run(); Shutdown(0); }
            catch (Exception ex) { Directory.CreateDirectory("artifacts"); File.WriteAllText("artifacts/test-failure.txt", ex.ToString()); Shutdown(1); }
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        window.OpenPaths(e.Args.Where(File.Exists));
    }
}
