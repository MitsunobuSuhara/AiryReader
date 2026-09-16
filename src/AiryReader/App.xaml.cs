using System.Threading;
namespace AiryReader;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = "Local\\AiryReader.SingleInstance.v1";
    private const string InstancePipeName = "AiryReader.OpenFiles.v1";
    private Mutex? instanceMutex;
    private CancellationTokenSource? pipeCancellation;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test") || e.Args.Contains("--ui-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            AiryReader.MainWindow.SuppressRecentFilesForTest = true;
            if (File.Exists("artifacts/test-failure.txt")) File.Delete("artifacts/test-failure.txt");
            try { if (e.Args.Contains("--ui-test")) await SelfTest.RunUiAsync(); else SelfTest.Run(); Shutdown(0); }
            catch (Exception ex) { Directory.CreateDirectory("artifacts"); File.WriteAllText("artifacts/test-failure.txt", ex.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--unregister"))
        {
            if (MessageBox.Show("AiryReaderのアプリ登録とショートカットを解除しますか？\n実行ファイルと設定は復旧用に残ります。", "AiryReader 登録解除", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
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
            string logFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiryReader");
            Directory.CreateDirectory(logFolder);
            try
            {
                string installed = DesktopInstaller.Install();
                File.WriteAllText(System.IO.Path.Combine(logFolder, "install-result.txt"), installed);
                if (!e.Args.Contains("--install-quiet")) MessageBox.Show("スタートメニューに AiryReader を登録しました。", "AiryReader");
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
        string[] files = e.Args.Where(File.Exists).Select(System.IO.Path.GetFullPath).ToArray();
        instanceMutex = new Mutex(true, InstanceMutexName, out bool firstInstance);
        if (!firstInstance)
        {
            if (!await ForwardFilesAsync(files)) MessageBox.Show("起動中のAiryReaderへファイルを渡せませんでした。少し待ってから開き直してください。", "AiryReader");
            instanceMutex.Dispose(); instanceMutex = null; Shutdown(0); return;
        }
        var window = new MainWindow();
        MainWindow = window;
        pipeCancellation = new CancellationTokenSource();
        _ = ListenForFilesAsync(window, pipeCancellation.Token);
        window.Closed += (_, _) => { pipeCancellation.Cancel(); instanceMutex?.ReleaseMutex(); instanceMutex?.Dispose(); instanceMutex = null; };
        window.Show();
        if (files.Length == 0) window.NewText();
        else window.OpenPaths(files);
    }
    private static async Task<bool> ForwardFilesAsync(string[] files)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", InstancePipeName, System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.Asynchronous);
                await pipe.ConnectAsync(150);
                using var writer = new BinaryWriter(pipe, System.Text.Encoding.UTF8, leaveOpen: true);
                writer.Write(files.Length);
                foreach (string file in files) writer.Write(file);
                writer.Flush(); await pipe.FlushAsync(); return true;
            }
            catch (TimeoutException) { await Task.Delay(100); }
            catch (IOException) { await Task.Delay(100); }
            catch (UnauthorizedAccessException) { await Task.Delay(100); }
        }
        return false;
    }

    private async Task ListenForFilesAsync(MainWindow window, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new System.IO.Pipes.NamedPipeServerStream(InstancePipeName, System.IO.Pipes.PipeDirection.In, 1,
                    System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new BinaryReader(pipe, System.Text.Encoding.UTF8, leaveOpen: true);
                int count = Math.Clamp(reader.ReadInt32(), 0, 1000);
                string[] files = Enumerable.Range(0, count).Select(_ => reader.ReadString()).Where(File.Exists).ToArray();
                await Dispatcher.InvokeAsync(() =>
                {
                    if (files.Length > 0) window.OpenPaths(files);
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    window.Show(); window.Activate();
                });
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) when (!cancellationToken.IsCancellationRequested) { }
        }
    }
}
