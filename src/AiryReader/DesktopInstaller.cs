using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace AiryReader;

public static class DesktopInstaller
{
    public static string Install()
    {
        string source = AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar);
        string dll = System.IO.Path.Combine(source, "AiryReader.dll");
        if (!File.Exists(dll) || !File.Exists(System.IO.Path.Combine(source, "coreclr.dll")))
            throw new IOException(".NET同梱の実行用フォルダから登録してください。");
        if (!new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            throw new IOException("Program Filesへの登録には管理者権限が必要です。");
        string root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AiryReader");
        string target = NormalizeDirectoryCase(root);
        Directory.CreateDirectory(target);
        if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            // コピー前に既存ファイルを確認し、起動中の版への途中までの上書きを防ぐ。
            foreach (string existing in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
            {
                string incoming = System.IO.Path.Combine(source, System.IO.Path.GetRelativePath(target, existing));
                if (!File.Exists(incoming) || SHA256.HashData(File.ReadAllBytes(existing)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(incoming)))) continue;
                try { using var check = File.Open(existing, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { throw new IOException("AiryReaderを閉じてから、もう一度セットアップしてください。"); }
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
        foreach (string name in new[] { "AiryReader.exe", "AiryReader.dll", "AiryReader.deps.json", "AiryReader.runtimeconfig.json", "AiryReader.pdb", "AiryReader.ico" }) NormalizeFileCase(target, name);
        string exe = ResolveShellPath(System.IO.Path.Combine(target, "AiryReader.exe"));
        target = System.IO.Path.GetDirectoryName(exe)!;
        // 元の版は残す。更新中や実行中のファイルを削除しない。
        CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), exe, target);
        CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs), exe, target);
        string pinnedFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
        // 既にピン留めされた自分のリンクのみ更新する。新たなピン留めは行わない。
        if (File.Exists(System.IO.Path.Combine(pinnedFolder, "airyPDF.lnk")) || File.Exists(System.IO.Path.Combine(pinnedFolder, "AiryReader.lnk")))
            CreateShortcut(pinnedFolder, exe, target);
        foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.Programs), pinnedFolder }) RemoveLegacyShortcut(folder);
        RegisterApplication(exe, target);
        RemoveLegacyInstall(source);
        return exe;
    }
    private static string NormalizeDirectoryCase(string desired)
    {
        string parent = System.IO.Path.GetDirectoryName(desired)!;
        string? existing = Directory.EnumerateDirectories(parent).FirstOrDefault(path =>
            string.Equals(System.IO.Path.GetFileName(path), System.IO.Path.GetFileName(desired), StringComparison.OrdinalIgnoreCase));
        if (existing == null || string.Equals(existing, desired, StringComparison.Ordinal)) return desired;
        string temporary = System.IO.Path.Combine(parent, ".AiryReader-case-" + Guid.NewGuid().ToString("N"));
        Directory.Move(existing, temporary);
        Directory.Move(temporary, desired);
        return desired;
    }
    private static void NormalizeFileCase(string folder, string desiredName)
    {
        string desired = System.IO.Path.Combine(folder, desiredName);
        string? existing = Directory.EnumerateFiles(folder).FirstOrDefault(path =>
            string.Equals(System.IO.Path.GetFileName(path), desiredName, StringComparison.OrdinalIgnoreCase));
        if (existing == null || string.Equals(existing, desired, StringComparison.Ordinal)) return;
        string temporary = System.IO.Path.Combine(folder, ".AiryReader-case-" + Guid.NewGuid().ToString("N"));
        File.Move(existing, temporary);
        File.Move(temporary, desired);
    }
    private static void RemoveLegacyInstall(string source)
    {
        string legacy = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "airyPDF");
        if (!Directory.Exists(legacy) || source.StartsWith(legacy + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        if (!File.Exists(System.IO.Path.Combine(legacy, "airyPDF.exe")) || !File.Exists(System.IO.Path.Combine(legacy, "coreclr.dll"))) return;
        foreach (string entry in Directory.EnumerateFileSystemEntries(legacy, "*", SearchOption.AllDirectories))
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) return;
        Directory.Delete(legacy, true);
    }
    private static void RegisterApplication(string exe, string folder)
    {
        string[] documentExtensions = [".pdf", ".md", ".markdown", ".txt"];
        string[] imageExtensions = [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp"];
        string[] supportedExtensions = [.. documentExtensions, .. imageExtensions];
        string[] retiredExtensions = [".html", ".htm"];
        using var app = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\AiryReader.exe");
        app.SetValue("FriendlyAppName", "AiryReader");
        using (var icon = app.CreateSubKey("DefaultIcon")) icon.SetValue("", IconLocation(exe));
        using (var command = app.CreateSubKey(@"shell\open\command")) command.SetValue("", "\"" + exe + "\" \"%1\"");
        using (var types = app.CreateSubKey("SupportedTypes"))
        {
            foreach (string extension in supportedExtensions) types.SetValue(extension, "");
            foreach (string extension in retiredExtensions) types.DeleteValue(extension, false);
        }

        // Windows 10/11の「既定のアプリ」にAiryReaderと対応形式を表示する正式な登録。
        using (var capabilities = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\AiryReader\Capabilities"))
        {
            capabilities.SetValue("ApplicationName", "AiryReader");
            capabilities.SetValue("ApplicationDescription", "軽量ファイル閲覧・PDF印刷");
            capabilities.SetValue("ApplicationIcon", IconLocation(exe));
            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (string extension in supportedExtensions) associations.SetValue(extension, ProgId(extension));
            foreach (string extension in retiredExtensions) associations.DeleteValue(extension, false);
        }
        using (var registered = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            registered.SetValue("AiryReader", @"Software\AiryReader\Capabilities");
        // 拡張子ごとのProgIDに分け、Windowsの既定アプリ選択で各形式を独立して選べるようにする。
        RegisterProgId("AiryReader.Document", "AiryReaderで開く文書", exe);
        RegisterProgId("AiryReader.Image", "AiryReaderで開く画像", exe);
        foreach (string extension in supportedExtensions)
        {
            string progId = ProgId(extension);
            RegisterProgId(progId, documentExtensions.Contains(extension) ? "AiryReaderで開く文書" : "AiryReaderで開く画像", exe);
            using var openWith = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + extension + @"\OpenWithProgids");
            openWith.SetValue(progId, Array.Empty<byte>(), Microsoft.Win32.RegistryValueKind.None);
            openWith.DeleteValue("AiryReader.Document", false);
            openWith.DeleteValue("AiryReader.Image", false);
        }
        foreach (string extension in retiredExtensions)
            using (var openWith = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + extension + @"\OpenWithProgids", true))
                openWith?.DeleteValue("AiryReader.Document", false);

        // Windowsの既定アプリが旧実行名を記憶していても、新しい実行ファイルへ安全につなぐ。
        using (var legacy = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\airyPDF.exe"))
        {
            legacy.SetValue("FriendlyAppName", "AiryReader");
            using (var command = legacy.CreateSubKey(@"shell\open\command")) command.SetValue("", "\"" + exe + "\" \"%1\"");
            using var types = legacy.CreateSubKey("SupportedTypes");
            foreach (string extension in supportedExtensions) types.SetValue(extension, "");
            foreach (string extension in retiredExtensions) types.DeleteValue(extension, false);
        }
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\airyPDF", false);
        using var uninstall = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\AiryReader");
        uninstall.SetValue("DisplayName", "AiryReader");
        uninstall.SetValue("DisplayVersion", "1.3.1");
        uninstall.SetValue("Publisher", "AiryReader");
        uninstall.SetValue("InstallLocation", folder);
        uninstall.SetValue("DisplayIcon", IconLocation(exe));
        uninstall.SetValue("UninstallString", "\"" + exe + "\" --unregister");
        uninstall.SetValue("NoModify", 1); uninstall.SetValue("NoRepair", 1);
        SHChangeNotify(0x08000000, 0, null!, IntPtr.Zero);
    }
    private static string ProgId(string extension) => "AiryReader" + extension.ToLowerInvariant();
    private static void RegisterProgId(string progId, string description, string exe)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + progId);
        key.SetValue("", description);
        key.SetValue("FriendlyTypeName", description);
        using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue("", IconLocation(exe));
        using (var command = key.CreateSubKey(@"shell\open\command")) command.SetValue("", "\"" + exe + "\" \"%1\"");
    }
    public static void Unregister()
    {
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Applications\AiryReader.exe", false);
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AiryReader.Document", false);
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AiryReader.Image", false);
        foreach (string extension in new[] { ".pdf", ".md", ".markdown", ".txt", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp" })
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + ProgId(extension), false);
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\AiryReader\Capabilities", false);
        using (var registered = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true)) registered?.DeleteValue("AiryReader", false);
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\AiryReader", false);
        foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar") })
        {
            string link = System.IO.Path.Combine(folder, "AiryReader.lnk");
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
        string direct = System.IO.Path.Combine(local, "Programs", "AiryReader") + System.IO.Path.DirectorySeparatorChar;
        string programFiles = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AiryReader") + System.IO.Path.DirectorySeparatorChar;
        string legacyProgramFiles = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "airyPDF") + System.IO.Path.DirectorySeparatorChar;
        string packages = System.IO.Path.Combine(local, "Packages") + System.IO.Path.DirectorySeparatorChar;
        return (string.Equals(System.IO.Path.GetFileName(path), "AiryReader.exe", StringComparison.OrdinalIgnoreCase) || string.Equals(System.IO.Path.GetFileName(path), "airyPDF.exe", StringComparison.OrdinalIgnoreCase)) &&
            (path.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase) || path.StartsWith(legacyProgramFiles, StringComparison.OrdinalIgnoreCase) || path.StartsWith(direct, StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith(packages, StringComparison.OrdinalIgnoreCase) && path.Contains("\\LocalCache\\Local\\Programs\\airyPDF\\", StringComparison.OrdinalIgnoreCase));
    }
    private static void RemoveLegacyShortcut(string folder)
    {
        string path = System.IO.Path.Combine(folder, "airyPDF.lnk");
        if (!File.Exists(path)) return;
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")!; dynamic shell = Activator.CreateInstance(shellType)!; object? item = null;
        try { dynamic shortcut = shell.CreateShortcut(path); item = shortcut; if (IsOwnInstall((string)shortcut.TargetPath)) { File.Delete(path); SHChangeNotify(0x4, 0x1005, path, IntPtr.Zero); } }
        finally { if (item != null) Marshal.FinalReleaseComObject(item); Marshal.FinalReleaseComObject(shell); }
    }
    private static string IconLocation(string exe) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(exe)!, "AiryReader.ico") + ",0";
    private static void CreateShortcut(string folder, string exe, string working)
    {
        Directory.CreateDirectory(folder);
        Type shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("ショートカットを作成できません。");
        dynamic shell = Activator.CreateInstance(shellType)!;
        object? shortcutObject = null;
        try
        {
            string path = System.IO.Path.Combine(folder, "AiryReader.lnk");
            dynamic shortcut = shell.CreateShortcut(path); shortcutObject = shortcut;
            if (File.Exists(path) && !string.IsNullOrEmpty((string)shortcut.TargetPath) && !IsOwnInstall((string)shortcut.TargetPath))
                throw new IOException("既存のAiryReaderショートカットが別の場所を指しています。上書きせず停止しました。");
            shortcut.TargetPath = exe; shortcut.WorkingDirectory = working; shortcut.IconLocation = IconLocation(exe);
            shortcut.Description = "AiryReader — 軽量ファイル閲覧・PDF印刷";
            shortcut.Save();
            NormalizeFileCase(folder, "AiryReader.lnk");
            path = System.IO.Path.Combine(folder, "AiryReader.lnk");
            SHChangeNotify(0x2000, 0x1005, path, IntPtr.Zero);
        }
        finally
        {
            if (shortcutObject != null) Marshal.FinalReleaseComObject(shortcutObject);
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
