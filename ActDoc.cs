using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
namespace Tmc;

/// <summary>Акт выдачи (форма Ф-02): заполняет Word-шаблон akt_template.docx и сохраняет .docx.</summary>
public static class ActDoc
{
    static string Esc(string s) => SecurityElement.Escape(s) ?? "";

    public static string Create(Issue a)
    {
        string dir;
        try
        {
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Акты ТМЦ");
            Directory.CreateDirectory(dir);
        }
        catch
        {
            dir = Path.Combine(Store.Dir, "acts");
            Directory.CreateDirectory(dir);
        }
        var bytes = Fill(a);
        var path = Path.Combine(dir, $"Акт_{a.Number:0000}.docx");
        try { File.WriteAllBytes(path, bytes); }
        catch (IOException)   // файл открыт в Word — сохраняем под другим именем
        {
            path = Path.Combine(dir, $"Акт_{a.Number:0000}_{DateTime.Now:HHmmss}.docx");
            File.WriteAllBytes(path, bytes);
        }
        return path;
    }

    static byte[] Fill(Issue a)
    {
        using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("akt_template.docx")
            ?? throw new InvalidOperationException("В программе не найден шаблон акта");
        using var ms = new MemoryStream();
        res.CopyTo(ms);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, true))
        {
            var entry = zip.GetEntry("word/document.xml")!;
            string xml;
            using (var r = new StreamReader(entry.Open(), Encoding.UTF8)) xml = r.ReadToEnd();
            xml = Apply(xml, a);
            entry.Delete();
            var ne = zip.CreateEntry("word/document.xml", CompressionLevel.Optimal);
            using var w = new StreamWriter(ne.Open(), new UTF8Encoding(false));
            w.Write(xml);
        }
        return ms.ToArray();
    }

    static string Apply(string xml, Issue a)
    {
        var ru = CultureInfo.GetCultureInfo("ru-RU");

        // 1) список ТМЦ: абзац с длинной чертой превращаем в по одному абзацу на позицию
        var blank68 = new string('_', 68);
        int i = xml.IndexOf(blank68, StringComparison.Ordinal);
        if (i < 0) throw new InvalidOperationException("Шаблон акта изменён: не найдено место для списка ТМЦ");
        int ps = xml.LastIndexOf("<w:p ", i, StringComparison.Ordinal);
        int pe = xml.IndexOf("</w:p>", i, StringComparison.Ordinal) + 6;
        var para = xml.Substring(ps, pe - ps);
        para = Regex.Replace(para, "\\s+w14:(paraId|textId)=\"[^\"]*\"", "");

        var sb = new StringBuilder();
        foreach (var l in a.Lines)
        {
            var it = H.Find(l.ItemId);
            var text = (it == null ? "?" : H.Label(it)) + " — " + H.N(l.Qty) + " " + it?.Unit;
            sb.Append(para.Replace(blank68, Esc(text)));
        }
        if (!string.IsNullOrWhiteSpace(a.Note) && a.Note.Trim() != ".")
        {
            var plain = Regex.Replace(para, "<w:numPr>.*?</w:numPr>", "");
            sb.Append(plain.Replace(blank68, Esc("Примечание: " + a.Note.Trim())));
        }
        xml = xml.Substring(0, ps) + sb + xml.Substring(pe);

        // 2) сотрудник (получатель)
        var blank53 = new string('_', 53);
        var rec = string.IsNullOrWhiteSpace(a.Recipient) ? blank53 : a.Recipient.Trim();
        xml = ReplaceOnce(xml, blank53, Esc(rec));

        // 3) дата
        xml = ReplaceOnce(xml, "____________20    ", Esc(a.Date.ToString("d MMMM yyyy", ru)));
        xml = ReplaceOnce(xml, "> год</w:t>", "> года</w:t>");

        // 4) подписи (таблица в конце шаблона): «ФИО /_____ /»
        //    слева — кто выдал (пользователь программы, от ЦРБ), справа — получатель (сотрудник)
        int t = xml.LastIndexOf("<w:tbl>", StringComparison.Ordinal);
        var head = xml.Substring(0, t);
        var tail = xml.Substring(t);
        const string sign = "<w:t>__________________/</w:t>";
        tail = ReplaceOnce(tail, sign, "<w:t xml:space=\"preserve\">" + Esc(Short(a.User)) + " /</w:t>");
        tail = ReplaceOnce(tail, sign, "<w:t xml:space=\"preserve\">" + Esc(Short(a.Recipient)) + " /</w:t>");
        xml = head + tail;
        return xml;
    }

    /// <summary>«Иванов Иван Иванович» -> «Иванов И.И.»; всё остальное оставляем как есть.</summary>
    static string Short(string s)
    {
        s = (s ?? "").Trim();
        var p = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2 || p.Length > 3 || p.Skip(1).Any(x => !char.IsLetter(x[0]))) return s == "" ? "_____" : s;
        return p[0] + " " + string.Concat(p.Skip(1).Select(x => char.ToUpper(x[0]) + "."));
    }

    static string ReplaceOnce(string s, string find, string repl)
    {
        int i = s.IndexOf(find, StringComparison.Ordinal);
        if (i < 0) throw new InvalidOperationException("Шаблон акта изменён: не найдено «" + find.Trim('_', ' ') + "»");
        return s.Substring(0, i) + repl + s.Substring(i + find.Length);
    }
}
