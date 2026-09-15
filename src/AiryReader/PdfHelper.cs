using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace AiryReader;
public static class PdfHelper
{
    public static async Task<JsonElement> RunAsync(Dictionary<string,object?> request)
    {
        string helper = System.IO.Path.Combine(AppContext.BaseDirectory, "Helper", "airy-pdf-helper.exe");
        if (!File.Exists(helper)) throw new IOException("PDF編集エンジンがありません。アプリの更新版を登録してください。");
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false) };
        using var process = Process.Start(start) ?? throw new IOException("PDF編集エンジンを起動できません。");
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request)); process.StandardInput.Close();
        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new IOException("PDF処理が時間内に完了しませんでした。"); }
        string response = await output; await errors;
        if (string.IsNullOrWhiteSpace(response)) throw new IOException("PDF処理が応答しませんでした。");
        using var json = JsonDocument.Parse(response);
        if (!json.RootElement.GetProperty("ok").GetBoolean()) throw new IOException(json.RootElement.GetProperty("error").GetString());
        return json.RootElement.GetProperty("result").Clone();
    }
}
