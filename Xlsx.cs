using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
namespace Tmc;

/// <summary>Минимальный экспорт в .xlsx без внешних библиотек (структура как у файлов самого Excel).</summary>
public static class Xlsx
{
    const string X = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
    const string NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    const string RNS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    static string Esc(string s) => SecurityElement.Escape(new string(s.Where(c => c >= ' ' || c == '\t' || c == '\n').ToArray())) ?? "";

    static void Add(ZipArchive z, string name, string xml)
    {
        var e = z.CreateEntry(name, CompressionLevel.Optimal);
        using var st = e.Open();
        var bytes = new UTF8Encoding(false).GetBytes(xml);
        st.Write(bytes, 0, bytes.Length);
    }

    static string Ref(int c, int r) => ((char)('A' + c)).ToString() + r.ToString(CultureInfo.InvariantCulture);

    /// <summary>numCols — индексы столбцов, которые записываются как числа.</summary>
    public static void Write(string path, string sheetName, string[] headers, IEnumerable<string[]> rows, params int[] numCols)
    {
        var strings = new List<string>();
        var index = new Dictionary<string, int>();
        int total = 0;
        int S(string s)
        {
            total++;
            if (!index.TryGetValue(s, out var i)) { i = strings.Count; strings.Add(s); index[s] = i; }
            return i;
        }

        var data = new StringBuilder();
        data.Append("<row r=\"1\">");
        for (int i = 0; i < headers.Length; i++)
            data.Append("<c r=\"" + Ref(i, 1) + "\" s=\"1\" t=\"s\"><v>" + S(headers[i]) + "</v></c>");
        data.Append("</row>");
        int r = 2;
        foreach (var row in rows)
        {
            data.Append("<row r=\"" + r + "\">");
            for (int c = 0; c < row.Length; c++)
            {
                if (numCols.Contains(c) && decimal.TryParse(row[c], NumberStyles.Number, CultureInfo.CurrentCulture, out var n))
                    data.Append("<c r=\"" + Ref(c, r) + "\"><v>" + n.ToString(CultureInfo.InvariantCulture) + "</v></c>");
                else
                    data.Append("<c r=\"" + Ref(c, r) + "\" t=\"s\"><v>" + S(row[c]) + "</v></c>");
            }
            data.Append("</row>");
            r++;
        }

        var cols = new StringBuilder();
        for (int i = 0; i < headers.Length; i++)
            cols.Append("<col min=\"" + (i + 1) + "\" max=\"" + (i + 1) + "\" width=\"" + (i < 2 ? "28.7109375" : "14.7109375") + "\" customWidth=\"1\"/>");

        var sheet = X + "<worksheet xmlns=\"" + NS + "\" xmlns:r=\"" + RNS + "\">" +
            "<dimension ref=\"A1:" + Ref(headers.Length - 1, r - 1) + "\"/>" +
            "<sheetViews><sheetView tabSelected=\"1\" workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/><selection pane=\"bottomLeft\"/></sheetView></sheetViews>" +
            "<sheetFormatPr defaultRowHeight=\"15\"/>" +
            "<cols>" + cols + "</cols>" +
            "<sheetData>" + data + "</sheetData>" +
            "<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>" +
            "</worksheet>";

        var sst = new StringBuilder(X + "<sst xmlns=\"" + NS + "\" count=\"" + total + "\" uniqueCount=\"" + strings.Count + "\">");
        foreach (var s in strings) sst.Append("<si><t xml:space=\"preserve\">" + Esc(s) + "</t></si>");
        sst.Append("</sst>");

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using (var z = new ZipArchive(fs, ZipArchiveMode.Create, true))
        {
            Add(z, "[Content_Types].xml", X + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/></Types>");
            Add(z, "_rels/.rels", X + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"" + RNS + "/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add(z, "xl/workbook.xml", X + "<workbook xmlns=\"" + NS + "\" xmlns:r=\"" + RNS + "\">" +
                "<bookViews><workbookView xWindow=\"240\" yWindow=\"15\" windowWidth=\"16095\" windowHeight=\"9660\"/></bookViews>" +
                "<sheets><sheet name=\"" + Esc(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add(z, "xl/_rels/workbook.xml.rels", X + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"" + RNS + "/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"" + RNS + "/styles\" Target=\"styles.xml\"/>" +
                "<Relationship Id=\"rId3\" Type=\"" + RNS + "/sharedStrings\" Target=\"sharedStrings.xml\"/></Relationships>");
            Add(z, "xl/styles.xml", X + "<styleSheet xmlns=\"" + NS + "\">" +
                "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font></fonts>" +
                "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                "<dxfs count=\"0\"/><tableStyles count=\"0\" defaultTableStyle=\"TableStyleMedium9\" defaultPivotStyle=\"PivotStyleLight16\"/></styleSheet>");
            Add(z, "xl/worksheets/sheet1.xml", sheet);
            Add(z, "xl/sharedStrings.xml", sst.ToString());
        }
    }
}
