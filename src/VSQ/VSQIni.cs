using System;
using System.Collections.Generic;
using System.Text;

namespace VocaloidFormatSupport.VSQ;

// VSQ 内嵌 INI 的文本模型：按出现顺序保存若干 [Section]、每节按顺序保存 key=value（顺序即语义，不可退化为字典）。
internal sealed class IniSection
{
    public IniSection(string name) => Name = name;

    /// <summary>节名（不含方括号），如 Common / ID#0001 / DynamicsBPList。</summary>
    public string Name { get; }

    /// <summary>按出现顺序的字段。</summary>
    public List<KeyValuePair<string, string>> Fields { get; } = new();

    readonly Dictionary<string, string> mMap = new(StringComparer.Ordinal);

    public void Add(string key, string value)
    {
        Fields.Add(new KeyValuePair<string, string>(key, value));
        mMap[key] = value;          // 后写覆盖（同名 key 取最后一个）
    }

    public string? Get(string key) => mMap.TryGetValue(key, out var v) ? v : null;
}

internal static class VSQIni
{
    public static List<IniSection> Parse(string text)
    {
        var sections = new List<IniSection>();
        IniSection? cur = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
                continue;

            if (line[0] == '[')
            {
                int close = line.IndexOf(']');
                var name = close > 0 ? line.Substring(1, close - 1) : line.Substring(1);
                cur = new IniSection(name);
                sections.Add(cur);
                continue;
            }

            if (cur == null)
                continue;

            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            cur.Add(line.Substring(0, eq), line.Substring(eq + 1));
        }

        return sections;
    }

    public static string Build(IEnumerable<IniSection> sections)
    {
        var sb = new StringBuilder();
        foreach (var s in sections)
        {
            sb.Append('[').Append(s.Name).Append("]\n");
            foreach (var kv in s.Fields)
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
        }
        return sb.ToString();
    }

    public static IniSection? Find(List<IniSection> sections, string name)
    {
        foreach (var s in sections)
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                return s;
        return null;
    }
}
