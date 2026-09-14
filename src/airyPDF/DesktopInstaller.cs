using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace AiryPdf;

public static class DesktopInstaller
{
    public static string Install()
    {
        string source = AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar);
        string dll = System.IO.Path.Combine(source, "airyPDF.dll");
        if (!File.Exists(dll) || !File.Exists(System.IO.Path.Combine(source, "coreclr.dll")))
            throw new IOException(".NET同梱の実行用フォルダから登録してください。");
        if (!new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            throw new IOException("Program Filesへの登録には管理者権限が必要です。");
        string root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "airyPDF");
        string target = root;
        Directory.CreateDirectory(target);
        if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            // コピー前に既存ファイルを確認し、起動中の版への途中までの上書きを防ぐ。
            foreach (string existing in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
            {
                string incoming = System.IO.Path.Combine(source, System.IO.Path.GetRelativePath(target, existing));
                if (!File.Exists(incoming) || SHA256.HashData(File.ReadAllBytes(existing)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(incoming)))) continue;
                try { using var check = File.Open(existing, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { throw new IOException("airyPDFを閉じてから、もう一度セットアップしてください。"); }
            }
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = System.IO.Path.GetRelativePath(source, file);
                string destination = System.IO.Path.GetFullPath(System.IO.Path.Combine(target, relative));
                if (!destination.StartsWith(target + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("コピー先がアプリのフォルダ外です。");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                if (File.Exists(destination) && SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(destination)))) continue;
                File.Copy(file, destination, true);
            }
        }
        string exe = ResolveShellPath(System.IO.Path.Combine(target, "airyPDF.exe"));
        target = System.IO.Path.GetDirectoryName(exe)!;
        // 元の版は残す。更新中や実行中のファイルを削除しない。
        CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), exe, target);
        CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs), exe, target);
        string pinnedFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
        // 既にピン留めされた自分のリンクのみ更新する。新たなピン留めは行わない。
        if (File.Exists(System.IO.Path.Combine(pinnedFolder, "airyPDF.lnk")))
            CreateShortcut(pinnedFolder, exe, target);
        RegisterApplication(exe, target);
        return exe;
    }
    private static void RegisterApplication(string exe, string folder)
    {
        using var app = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\airyPDF.exe");
        app.SetValue("FriendlyAppName", "airyPDF");
        using (var command = app.CreateSubKey(@"shell\open\command")) command.SetValue("", "\"" + exe + "\" \"%1\"");
        using (var types = app.CreateSubKey("SupportedTypes")) types.SetValue(".pdf", "");
        using var uninstall = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\airyPDF");
        uninstall.SetValue("DisplayName", "airyPDF");
        uninstall.SetValue("DisplayVersion", "1.1.0");
        uninstall.SetValue("Publisher", "airyPDF");
        uninstall.SetValue("InstallLocation", folder);
        uninstall.SetValue("DisplayIcon", exe + ",0");
        uninstall.SetValue("UninstallString", "\"" + exe + "\" --unregister");
        uninstall.SetValue("NoModify", 1); uninstall.SetValue("NoRepair", 1);
    }
    public static void Unregister()
    {
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Applications\airyPDF.exe", false);
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\airyPDF", false);
        foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar") })
        {
            string link = System.IO.Path.Combine(folder, "airyPDF.lnk");
            if (!File.Exists(link)) continue;
            Type shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            object? shortcutObject = null;
            try { dynamic shortcut = shell.CreateShortcut(link); shortcutObject = shortcut;
                if (IsOwnInstall((string)shortcut.TargetPath)) { File.Delete(link); SHChangeNotify(0x4, 0x1005, link, IntPtr.Zero); } }
            finally { if (shortcutObject != null) Marshal.FinalReleaseComObject(shortcutObject); Marshal.FinalReleaseComObject(shell); }
        }
        // 実行ファイルとユーザー設定は復旧用に残す。
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, System.Text.StringBuilder path, uint length, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int change, uint flags, string path, IntPtr unused);
    internal static string ResolveShellPath(string path)
    {
        // パッケージ環境ではLocalAppDataへの書込みが転送される。Explorerからも読める実パスを登録する。
        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new System.Text.StringBuilder(32768);
        uint length = GetFinalPathNameByHandle(file, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw new IOException("アイコンと起動先の実際の場所を確認できません。");
        string resolved = buffer.ToString();
        if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + resolved[8..];
        return resolved.StartsWith(@"\\?\", StringComparison.Ordinal) ? resolved[4..] : resolved;
    }
    internal static bool IsOwnInstall(string path)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string direct = System.IO.Path.Combine(local, "Programs", "airyPDF") + System.IO.Path.DirectorySeparatorChar;
        string programFiles = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "airyPDF") + System.IO.Path.DirectorySeparatorChar;
        string packages = System.IO.Path.Combine(local, "Packages") + System.IO.Path.DirectorySeparatorChar;
        return string.Equals(System.IO.Path.GetFileName(path), "airyPDF.exe", StringComparison.OrdinalIgnoreCase) &&
            (path.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase) || path.StartsWith(direct, StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith(packages, StringComparison.OrdinalIgnoreCase) && path.Contains("\\LocalCache\\Local\\Programs\\airyPDF\\", StringComparison.OrdinalIgnoreCase));
    }
    private static void CreateShortcut(string folder, string exe, string working)
    {
        Directory.CreateDirectory(folder);
        Type shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("ショートカットを作成できません。");
        dynamic shell = Activator.CreateInstance(shellType)!;
        object? shortcutObject = null;
        try
        {
            string path = System.IO.Path.Combine(folder, "airyPDF.lnk");
            dynamic shortcut = shell.CreateShortcut(path); shortcutObject = shortcut;
            if (File.Exists(path) && !string.IsNullOrEmpty((string)shortcut.TargetPath) && !IsOwnInstall((string)shortcut.TargetPath))
                throw new IOException("既存のairyPDFショートカットが別の場所を指しています。上書きせず停止しました。");
            shortcut.TargetPath = exe; shortcut.WorkingDirectory = working; shortcut.IconLocation = exe + ",0";
            shortcut.Description = "airyPDF — PDF閲覧・印刷チェック";
            shortcut.Save();
            SHChangeNotify(0x2000, 0x1005, path, IntPtr.Zero);
        }
        finally
        {
            if (shortcutObject != null) Marshal.FinalReleaseComObject(shortcutObject);
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
