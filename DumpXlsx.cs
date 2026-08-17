using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

// Minimal .xlsx reader: unzipped workbook parts only, no Excel/COM dependency.
// Prints one sheet as tab-separated rows so the valve schedule can be read directly.
class DumpXlsx
{
    static void Main(string[] args)
    {
        string dir = args[0];                       // folder containing xl\
        string sheet = args.Length > 1 ? args[1] : "sheet1.xml";
        int maxRows = args.Length > 2 ? int.Parse(args[2]) : 500;

        var shared = new List<string>();
        string ssPath = Path.Combine(dir, "xl", "sharedStrings.xml");
        if (File.Exists(ssPath))
        {
            var d = new XmlDocument();
            d.Load(ssPath);
            foreach (XmlNode si in d.DocumentElement.ChildNodes)
                shared.Add(si.InnerText);
        }

        var doc = new XmlDocument();
        doc.Load(Path.Combine(dir, "xl", "worksheets", sheet));
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("m", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

        int printed = 0;
        foreach (XmlNode row in doc.SelectNodes("//m:sheetData/m:row", nsm))
        {
            var cells = new SortedDictionary<int, string>();
            foreach (XmlNode c in row.SelectNodes("m:c", nsm))
            {
                string reference = c.Attributes["r"] != null ? c.Attributes["r"].Value : "A1";
                int col = 0;
                foreach (char ch in reference)
                {
                    if (ch >= 'A' && ch <= 'Z') col = col * 26 + (ch - 'A' + 1);
                    else break;
                }
                string t = c.Attributes["t"] != null ? c.Attributes["t"].Value : "";
                XmlNode v = c.SelectSingleNode("m:v", nsm);
                string val = "";
                if (t == "s" && v != null)
                {
                    int idx = int.Parse(v.InnerText, CultureInfo.InvariantCulture);
                    if (idx >= 0 && idx < shared.Count) val = shared[idx];
                }
                else if (t == "inlineStr")
                {
                    XmlNode isn = c.SelectSingleNode("m:is", nsm);
                    if (isn != null) val = isn.InnerText;
                }
                else if (v != null) val = v.InnerText;
                cells[col] = val.Replace("\t", " ").Replace("\n", " ").Trim();
            }
            if (cells.Count == 0) continue;
            int max = 0;
            foreach (int k in cells.Keys) if (k > max) max = k;
            var parts = new string[max];
            foreach (var kv in cells) parts[kv.Key - 1] = kv.Value;
            for (int i = 0; i < parts.Length; i++) if (parts[i] == null) parts[i] = "";
            Console.WriteLine(string.Join("\t", parts));
            if (++printed >= maxRows) break;
        }
    }
}
