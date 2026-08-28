using System.Text;

namespace JACO.SalesDiscount.Web.Services;

public static class CsvHelper
{
    public static string ToCsv<T>(List<T> rows, string[] header, Func<T, string[]> selector)
    {
        string Escape(string v) => v.Contains(',') || v.Contains('"') || v.Contains('\n') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", header.Select(Escape)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", selector(row).Select(Escape)));
        return sb.ToString();
    }

    public static byte[] ToCsvBytes<T>(List<T> rows, string[] header, Func<T, string[]> selector)
    {
        var csv = ToCsv(rows, header, selector);
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
    }
}
