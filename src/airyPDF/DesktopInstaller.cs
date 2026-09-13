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
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll)))[..12].ToLowerInvariant();
        string root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "airyPDF");
        string target = System.IO.Path.Combine(root, "0.2.1-" + hash);
        Directory.CreateDirectory(target);
        if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
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
        string exe = System.IO.Path.Combine(target, "airyPDF.exe");
        // 元の版は残す。更新中や実行中のファイルを削除しない。
        CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), exe, target);
        CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs), exe, target);
        string pinnedFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
        // 既にピン留めされた自分のリンクのみ更新する。新たなピン留めは行わない。
        if (File.Exists(System.IO.Path.Combine(pinnedFolder, "airyPDF.lnk")))
            CreateShortcut(pinnedFolder, exe, target);
        return exe;
    }
    internal static bool IsOwnInstall(string path)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string direct = System.IO.Path.Combine(local, "Programs", "airyPDF") + System.IO.Path.DirectorySeparatorChar;
        string packages = System.IO.Path.Combine(local, "Packages") + System.IO.Path.DirectorySeparatorChar;
        return string.Equals(System.IO.Path.GetFileName(path), "airyPDF.exe", StringComparison.OrdinalIgnoreCase) &&
            (path.StartsWith(direct, StringComparison.OrdinalIgnoreCase) ||
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
        }
        finally
        {
            if (shortcutObject != null) Marshal.FinalReleaseComObject(shortcutObject);
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
