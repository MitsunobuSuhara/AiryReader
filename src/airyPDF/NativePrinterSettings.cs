using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AiryPdf;

public static class NativePrinterSettings
{
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string name, out IntPtr printer, IntPtr defaults);
    [DllImport("winspool.drv")] private static extern bool ClosePrinter(IntPtr printer);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode)]
    private static extern int DocumentProperties(IntPtr owner, IntPtr printer, string name, IntPtr output, IntPtr input, int mode);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr handle);
    public static bool Show(Window owner, PrintDocument document)
    {
        if (!OpenPrinter(document.PrinterSettings.PrinterName, out IntPtr printer, IntPtr.Zero)) throw new IOException("プリンターの詳細設定を開けません。");
        IntPtr memory = IntPtr.Zero, data = IntPtr.Zero;
        try
        {
            memory = document.PrinterSettings.GetHdevmode(document.DefaultPageSettings);
            data = GlobalLock(memory);
            if (data == IntPtr.Zero) throw new IOException("印刷設定を読み込めません。");
            int result = DocumentProperties(new WindowInteropHelper(owner).Handle, printer, document.PrinterSettings.PrinterName, data, data, 4 | 8 | 2);
            if (result < 0) throw new IOException("プリンタードライバーからエラーが返されました。");
            if (result != 1) return false;
            document.PrinterSettings.SetHdevmode(memory);
            document.DefaultPageSettings.SetHdevmode(memory);
            return true;
        }
        finally
        {
            if (data != IntPtr.Zero) GlobalUnlock(memory);
            if (memory != IntPtr.Zero) GlobalFree(memory);
            ClosePrinter(printer);
        }
    }
}
