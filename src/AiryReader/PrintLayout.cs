namespace AiryReader;

public enum PrintMode { Scale, Fit, TwoUp, FourUp, Booklet, Poster }
public record Placement(int Page, Rect Destination, Rect Clip, double Scale);
public record Sheet(Size Paper, Rect Printable, List<Placement> Items, string Label);
public record PrintOptions(PrintMode Mode, double Percent = 100, double Overlap = 5, bool RightBinding = false);

// 単位はすべてmm。画面のDPIやプリンターのDPIはこの計算へ持ち込まない。
public static class PrintLayout
{
    public static int[] ParsePages(string text, int count)
    {
        if (string.IsNullOrWhiteSpace(text)) return Enumerable.Range(0, count).ToArray();
        var result = new List<int>();
        foreach (string token in text.Split(','))
        {
            var ends = token.Trim().Split('-');
            if (ends.Length is < 1 or > 2 || !int.TryParse(ends[0], out int start)) throw new FormatException("ページ範囲は 1-3,5 のように入力してください。");
            int end = start;
            if (ends.Length == 2 && !int.TryParse(ends[1], out end)) throw new FormatException("ページ範囲が正しくありません。");
            if (start < 1 || end < start || end > count) throw new FormatException($"ページは1〜{count}の昇順で指定してください。");
            for (int n = start; n <= end; n++) if (!result.Contains(n - 1)) result.Add(n - 1);
        }
        return result.ToArray();
    }

    public static List<Sheet> Build(Func<int, Size> size, int[] pages, Size paper, Rect printable, PrintOptions options, Rect? selection = null)
    {
        if (printable.Width <= 0 || printable.Height <= 0 || !new Rect(paper).Contains(printable)) throw new ArgumentException("印刷可能範囲を確認できません。");
        if (!double.IsFinite(options.Percent) || options.Percent < 1 || options.Percent > 1000) throw new ArgumentException("印刷倍率は1〜1000%で指定してください。");
        if (selection.HasValue && (pages.Length != 1 || options.Mode is PrintMode.TwoUp or PrintMode.FourUp or PrintMode.Booklet))
            throw new ArgumentException("範囲印刷は現在ページの原寸・指定倍率・用紙に合わせる・ポスターで使えます。");
        var output = new List<Sheet>();
        Placement Place(int page, Rect cell, bool fit)
        {
            Size full = size(page);
            Rect source = selection ?? new Rect(full);
            if (source.Width <= 0 || source.Height <= 0 || !new Rect(full).Contains(source)) throw new ArgumentException("選択範囲がページの外です。");
            double scale = fit ? Math.Min(cell.Width / source.Width, cell.Height / source.Height) : options.Percent / 100;
            var dest = new Rect(cell.X + (cell.Width - source.Width * scale) / 2 - source.X * scale,
                cell.Y + (cell.Height - source.Height * scale) / 2 - source.Y * scale, full.Width * scale, full.Height * scale);
            Rect selectedOnPaper = new(dest.X + source.X * scale, dest.Y + source.Y * scale, source.Width * scale, source.Height * scale);
            selectedOnPaper.Intersect(cell);
            return new(page, dest, selectedOnPaper, scale);
        }
        if (options.Mode == PrintMode.Booklet)
        {
            int padded = (pages.Length + 3) / 4 * 4;
            for (int i = 0; i < padded / 4; i++)
            {
                AddBookSide([padded - 1 - 2 * i, 2 * i], $"{i + 1}枚目・表");
                AddBookSide([2 * i + 1, padded - 2 - 2 * i], $"{i + 1}枚目・裏");
            }
            void AddBookSide(int[] indices, string label)
            {
                if (options.RightBinding) Array.Reverse(indices);
                var items = new List<Placement>();
                for (int column = 0; column < 2; column++)
                    if (indices[column] < pages.Length)
                        items.Add(Place(pages[indices[column]], new Rect(printable.X + column * printable.Width / 2, printable.Y, printable.Width / 2, printable.Height), true));
                output.Add(new(paper, printable, items, label));
            }
        }
        else if (options.Mode == PrintMode.Poster)
        {
            double overlap = options.Overlap;
            if (!double.IsFinite(overlap) || overlap < 0 || overlap >= Math.Min(printable.Width, printable.Height)) throw new ArgumentException("重なり幅は0以上、印刷可能な幅・高さ未満にしてください。");
            foreach (int page in pages)
            {
                Size full = size(page); Rect source = selection ?? new Rect(full); double scale = options.Percent / 100;
                int cols = Math.Max(1, (int)Math.Ceiling((source.Width * scale - overlap) / (printable.Width - overlap)));
                int rows = Math.Max(1, (int)Math.Ceiling((source.Height * scale - overlap) / (printable.Height - overlap)));
                if ((long)cols * rows + output.Count > 500) throw new ArgumentException("分割数が500枚を超えます。倍率や用紙を見直してください。");
                for (int row = 0; row < rows; row++) for (int col = 0; col < cols; col++)
                {
                    double x = printable.X - col * (printable.Width - overlap), y = printable.Y - row * (printable.Height - overlap);
                    Rect clip = Rect.Intersect(printable, new Rect(x, y, source.Width * scale, source.Height * scale));
                    output.Add(new(paper, printable, [new(page, new Rect(x - source.X * scale, y - source.Y * scale, full.Width * scale, full.Height * scale), clip, scale)], $"PDF {page + 1}・行{row + 1}/{rows} 列{col + 1}/{cols}"));
                }
            }
        }
        else
        {
            int perSheet = options.Mode == PrintMode.FourUp ? 4 : options.Mode == PrintMode.TwoUp ? 2 : 1;
            int cols = perSheet == 4 || perSheet == 2 && paper.Width > paper.Height ? 2 : 1;
            int rows = perSheet / cols;
            for (int offset = 0; offset < pages.Length; offset += perSheet)
            {
                var items = new List<Placement>();
                for (int n = 0; n < perSheet && offset + n < pages.Length; n++)
                    items.Add(Place(pages[offset + n], new Rect(printable.X + n % cols * printable.Width / cols,
                        printable.Y + n / cols * printable.Height / rows, printable.Width / cols, printable.Height / rows), options.Mode != PrintMode.Scale));
                output.Add(new(paper, printable, items, $"{output.Count + 1}面目"));
            }
        }
        if (output.Count == 0) throw new ArgumentException("印刷するページがありません。");
        return output;
    }

    public static bool IsClipped(Placement placement, Size full, Rect? selection)
    {
        Rect source = selection ?? new Rect(full);
        Rect content = new(placement.Destination.X + source.X * placement.Scale, placement.Destination.Y + source.Y * placement.Scale,
            source.Width * placement.Scale, source.Height * placement.Scale);
        const double tolerance = 0.02;
        Rect clip = placement.Clip; clip.Inflate(tolerance, tolerance);
        return !clip.Contains(content);
    }
}
