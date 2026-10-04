using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace VocaloidFormatSupport.Common;

// XML 读写小工具（VSQX 专用）：读按 LocalName 匹配以通吃 vsq3/vsq4 两代命名空间；写为 UTF-8 无 BOM + Tab 缩进 + 手写声明。
internal static class XmlExt
{
    // ── 读 ──

    public static XElement? Elem(this XElement? parent, string localName)
    {
        if (parent == null)
            return null;
        foreach (var e in parent.Elements())
            if (e.Name.LocalName == localName)
                return e;
        return null;
    }

    public static IEnumerable<XElement> Elems(this XElement? parent, string localName)
    {
        if (parent == null)
            return Enumerable.Empty<XElement>();
        return parent.Elements().Where(e => e.Name.LocalName == localName);
    }

    public static string Str(this XElement? parent, string localName, string def = "")
        => parent.Elem(localName)?.Value ?? def;

    public static int Int(this XElement? parent, string localName, int def = 0)
        => int.TryParse(parent.Elem(localName)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

    public static double Dbl(this XElement? parent, string localName, double def = 0)
        => double.TryParse(parent.Elem(localName)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

    // ── 写 ──

    /// <summary>写到宿主流：UTF-8 无 BOM、Tab 缩进、手写声明；不 Dispose/Close 宿主流。</summary>
    public static void WriteXml(Stream output, XDocument doc)
    {
        const string declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n";
        output.Write(Encoding.ASCII.GetBytes(declaration));

        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = "\t",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CloseOutput = false,
        };
        using var writer = XmlWriter.Create(output, settings);
        doc.Save(writer);
        writer.Flush();
    }

    /// <summary>CDATA 文本节点（VSQX 的 seqName/comment/lyric/... 均为 CDATA）。</summary>
    public static XCData CData(string? text) => new(text ?? string.Empty);
}
