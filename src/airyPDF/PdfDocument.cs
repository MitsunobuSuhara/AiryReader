using System.Runtime.InteropServices;

namespace AiryPdf;

// PDFiumは全ドキュメント間で直列化する。描画中の回転・解放との競合も防ぐ。
public sealed class PdfDocument : IDisposable
{
    private static readonly object Gate = new();
    private static bool initialized;
    private IntPtr handle;
    private GCHandle pinned;
    public string Path { get; }
    public int Count { get; }
    public bool Dirty { get; private set; }
    public bool CanPrint { get; }
    public bool CanEdit { get; }
    public double PrintPercent { get; set; } = 100;

    public PdfDocument(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        if (new FileInfo(Path).Length > 512L * 1024 * 1024)
            throw new IOException("この試作版では512MBを超えるPDFは開けません。");
        lock (Gate)
        {
            if (!initialized) { Native.FPDF_InitLibrary(); initialized = true; }
            byte[] bytes = File.ReadAllBytes(Path);
            pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            handle = Native.FPDF_LoadMemDocument64(pinned.AddrOfPinnedObject(), (UIntPtr)bytes.LongLength, IntPtr.Zero);
            if (handle == IntPtr.Zero)
            {
                uint error = Native.FPDF_GetLastError();
                pinned.Free();
                throw new IOException(error == 4 ? "パスワード付きPDFには、この試作版は対応していません。" : $"PDFを開けませんでした（エラー {error}）。");
            }
            Count = Native.FPDF_GetPageCount(handle);
            uint permissions = Native.FPDF_GetDocPermissions(handle);
            CanPrint = (permissions & 4) != 0 && (permissions & 2048) != 0;
            CanEdit = (permissions & 8) != 0;
            if (Count < 1) { Dispose(); throw new IOException("表示できるページがありません。"); }
        }
    }

    private T WithPage<T>(int index, Func<IntPtr, T> action)
    {
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            IntPtr page = Native.FPDF_LoadPage(handle, index);
            if (page == IntPtr.Zero) throw new IOException($"{index + 1}ページを読み込めません。");
            try { return action(page); }
            finally { Native.FPDF_ClosePage(page); }
        }
    }

    public Size SizeMm(int index) => WithPage(index, page =>
    {
        var size = new Size(Native.FPDF_GetPageWidthF(page) * 25.4 / 72, Native.FPDF_GetPageHeightF(page) * 25.4 / 72);
        if (size.Width <= 0 || size.Height <= 0 || !double.IsFinite(size.Width + size.Height)) throw new IOException("ページ寸法が不正です。");
        return size;
    });

    public BitmapSource Render(int index, int width, int height) => WithPage(index, page =>
    {
        if (width <= 0 || height <= 0 || (long)width * height > 24_000_000) throw new ArgumentOutOfRangeException(nameof(width));
        IntPtr bitmap = Native.FPDFBitmap_Create(width, height, 0);
        if (bitmap == IntPtr.Zero) throw new IOException("描画用メモリを確保できません。");
        try
        {
            Native.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
            Native.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, 1);
            int stride = Native.FPDFBitmap_GetStride(bitmap);
            // ネイティブバッファ解放前にWPF側へコピーする。
            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null,
                Native.FPDFBitmap_GetBuffer(bitmap), stride * height, stride);
            image.Freeze();
            return image;
        }
        finally { Native.FPDFBitmap_Destroy(bitmap); }
    });

    public void DrawToPrinter(IntPtr dc, int index, int x, int y, int width, int height) => WithPage(index, page =>
    {
        Native.FPDF_RenderPage(dc, page, x, y, width, height, 0, 1 | 0x800);
        return true;
    });

    public void Rotate(int index, int delta)
    {
        if (!CanEdit) throw new InvalidOperationException("このPDFは編集が制限されています。");
        WithPage(index, page => { Native.FPDFPage_SetRotation(page, (Native.FPDFPage_GetRotation(page) + delta + 4) % 4); return true; });
        Dirty = true;
    }

    public void SaveCopy(string destination)
    {
        if (!CanEdit) throw new InvalidOperationException("このPDFは編集が制限されています。");
        if (string.Equals(System.IO.Path.GetFullPath(destination), Path, StringComparison.OrdinalIgnoreCase))
            throw new IOException("元のPDFを保護するため、別の名前を指定してください。");
        // 完成するまで既存の保存先を書き換えない。
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                Exception? writeError = null;
                Native.WriteBlock callback = (_, data, length) =>
                {
                    try { var bytes = new byte[checked((int)length)]; Marshal.Copy(data, bytes, 0, bytes.Length); stream.Write(bytes); return 1; }
                    catch (Exception ex) { writeError = ex; return 0; }
                };
                var writer = new Native.FileWrite { Version = 1, Callback = callback };
                lock (Gate)
                {
                    if (Native.FPDF_SaveAsCopy(handle, ref writer, 2) == 0)
                        throw new IOException("PDFを保存できませんでした。", writeError);
                }
                GC.KeepAlive(callback);
                stream.Flush(true);
            }
            File.Move(temporary, destination, true);
            Dirty = false;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (handle != IntPtr.Zero) { Native.FPDF_CloseDocument(handle); handle = IntPtr.Zero; }
            if (pinned.IsAllocated) pinned.Free();
        }
    }

    private static class Native
    {
        private const string Dll = "pdfium";
        [DllImport(Dll)] internal static extern void FPDF_InitLibrary();
        [DllImport(Dll)] internal static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, UIntPtr size, IntPtr password);
        [DllImport(Dll)] internal static extern uint FPDF_GetLastError();
        [DllImport(Dll)] internal static extern int FPDF_GetPageCount(IntPtr document);
        [DllImport(Dll)] internal static extern uint FPDF_GetDocPermissions(IntPtr document);
        [DllImport(Dll)] internal static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
        [DllImport(Dll)] internal static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Dll)] internal static extern void FPDF_CloseDocument(IntPtr document);
        [DllImport(Dll)] internal static extern float FPDF_GetPageWidthF(IntPtr page);
        [DllImport(Dll)] internal static extern float FPDF_GetPageHeightF(IntPtr page);
        [DllImport(Dll)] internal static extern int FPDFPage_GetRotation(IntPtr page);
        [DllImport(Dll)] internal static extern void FPDFPage_SetRotation(IntPtr page, int rotate);
        [DllImport(Dll)] internal static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
        [DllImport(Dll)] internal static extern void FPDFBitmap_FillRect(IntPtr bitmap, int x, int y, int width, int height, uint color);
        [DllImport(Dll)] internal static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int width, int height, int rotate, int flags);
        [DllImport(Dll)] internal static extern void FPDF_RenderPage(IntPtr dc, IntPtr page, int x, int y, int width, int height, int rotate, int flags);
        [DllImport(Dll)] internal static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
        [DllImport(Dll)] internal static extern int FPDFBitmap_GetStride(IntPtr bitmap);
        [DllImport(Dll)] internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int WriteBlock(IntPtr self, IntPtr data, uint size);
        [StructLayout(LayoutKind.Sequential)] internal struct FileWrite { public int Version; public WriteBlock Callback; }
        [DllImport(Dll)] internal static extern int FPDF_SaveAsCopy(IntPtr document, ref FileWrite writer, uint flags);
    }
}
