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
        if (e.Args.Contains("--unregister"))
        {
            if (MessageBox.Show("airyPDFのアプリ登録とショートカットを解除しますか？\n実行ファイルと設定は復旧用に残ります。", "airyPDF 登録解除", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                DesktopInstaller.Unregister();
            Shutdown(0); return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--make-check")
        {
            CalibrationPdf.Create(e.Args[1]); Shutdown(0); return;
        }
        if (e.Args.Contains("--install") || e.Args.Contains("--install-quiet"))
        {
            if (!new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            {
                try
                {
                    string exe = DesktopInstaller.ResolveShellPath(Environment.ProcessPath!);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas", Arguments = e.Args.Contains("--install-quiet") ? "--install-quiet" : "--install", WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden });
                    Shutdown(0);
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "管理者確認が完了しませんでした"); Shutdown(1); }
                return;
            }
            string logFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "airyPDF");
            Directory.CreateDirectory(logFolder);
            try
            {
                string installed = DesktopInstaller.Install();
                File.WriteAllText(System.IO.Path.Combine(logFolder, "install-result.txt"), installed);
                if (!e.Args.Contains("--install-quiet")) MessageBox.Show("デスクトップとスタートメニューに airyPDF を登録しました。", "airyPDF");
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(System.IO.Path.Combine(logFolder, "install-error.txt"), ex.ToString());
                if (!e.Args.Contains("--install-quiet")) MessageBox.Show(ex.Message, "登録できませんでした");
                Shutdown(1);
            }
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        window.OpenPaths(e.Args.Where(File.Exists));
    }
}
