using System.Text;

namespace VocaloidFormatSupport.VSQ;

// VSQ 的文本编码 = Shift-JIS（需注册 CodePagesEncodingProvider；宿主未提供时退化为 Latin-1，非 ASCII 歌词失真）。
internal static class VSQText
{
    static Encoding? sSjis;
    static bool sRegistered;

    public static Encoding ShiftJis
    {
        get
        {
            if (sSjis != null)
                return sSjis;

            if (!sRegistered)
            {
                sRegistered = true;
                try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
                catch { /* 宿主未提供 CodePages 程序集：走兜底 */ }
            }

            try { sSjis = Encoding.GetEncoding(932); }
            catch { sSjis = Encoding.Latin1; }
            return sSjis;
        }
    }

    public static string Decode(byte[] bytes) => ShiftJis.GetString(bytes);

    public static byte[] Encode(string? text) => ShiftJis.GetBytes(text ?? string.Empty);
}
