using System.Globalization;
using System.Text;

namespace AiryPdf;

public static class CalibrationPdf
{
    public static void Create(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var sizes = new[] { new Size(210,297), new Size(297,210), new Size(420,297), new Size(210,297) };
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Count 4 /Kids [4 0 R 6 0 R 8 0 R 10 0 R] >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        string N(double n) => n.ToString("0.#####", CultureInfo.InvariantCulture);
        string P(double mm) => N(mm * 72 / 25.4);
        for (int page = 0; page < sizes.Length; page++)
        {
            Size size = sizes[page];
            var content = new StringBuilder();
            void Line(double x1, double y1, double x2, double y2) => content.AppendLine($"{P(x1)} {P(y1)} m {P(x2)} {P(y2)} l S");
            void Text(double x, double y, double font, string text) => content.AppendLine($"BT /F1 {N(font)} Tf {P(x)} {P(y)} Td ({text}) Tj ET");
            content.AppendLine("0 0 0 rg 0 0 0 RG 0.5 w");
            Text(15, size.Height - 20, 21, $"airyPDF - PRINT CHECK  /  PAGE {page + 1}");
            Text(15, size.Height - 30, 11, $"{(page == 2 ? "A3" : "A4")}  {size.Width} x {size.Height} mm  /  TOP");
            Text(15, size.Height - 40, 10, "Print at Actual size / 100%. Compare the SAME PDF in Acrobat Reader.");
            Text(15, size.Height - 47, 10, "Measure the CENTER of the end ticks. Each interval is 10 mm.");
            content.AppendLine("0.5 w");
            Line(30, 40, 130, 40); Line(30, 40, 30, 140);
            for (int tick = 0; tick <= 10; tick++)
            {
                double extent = tick % 5 == 0 ? 3 : 1.5;
                Line(30 + tick * 10, 40 - extent, 30 + tick * 10, 40 + extent);
                Line(30 - extent, 40 + tick * 10, 30 + extent, 40 + tick * 10);
            }
            Text(52, 28, 12, "100 mm horizontal");
            Text(36, 134, 12, "100 mm vertical");
            content.AppendLine("0.65 G [3 3] 0 d 0.3 w");
            content.AppendLine($"{P(5)} {P(5)} {P(size.Width - 10)} {P(size.Height - 10)} re S");
            content.AppendLine("[] 0 d 0 G");
            Text(15, 14, 10, $"PAGE {page + 1}  /  BOTTOM - Check this direction on duplex and booklet output.");
            for (int n = 0; n <= page; n++) content.AppendLine($"{P(size.Width - 25 - n * 12)} {P(65)} {P(8)} {P(8)} re f");
            content.AppendLine("1 0 0 rg"); content.AppendLine($"{P(size.Width-45)} {P(85)} {P(8)} {P(8)} re f");
            content.AppendLine("0 0.65 0 rg"); content.AppendLine($"{P(size.Width-33)} {P(85)} {P(8)} {P(8)} re f");
            content.AppendLine("0 0 1 rg"); content.AppendLine($"{P(size.Width-21)} {P(85)} {P(8)} {P(8)} re f");
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {P(size.Width)} {P(size.Height)}] /Resources << /Font << /F1 3 0 R >> >> /Contents {5+page*2} 0 R >>");
            string commands = content.ToString();
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(commands)} >>\nstream\n{commands}endstream");
        }
        using var stream = File.Create(path); var offsets = new List<long>();
        void Write(string value) => stream.Write(Encoding.ASCII.GetBytes(value));
        Write("%PDF-1.7\n");
        for (int i=0; i<objects.Count; i++) { offsets.Add(stream.Position); Write($"{i+1} 0 obj\n{objects[i]}\nendobj\n"); }
        long xref=stream.Position; Write($"xref\n0 {objects.Count+1}\n0000000000 65535 f \n");
        foreach(long offset in offsets) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
}
