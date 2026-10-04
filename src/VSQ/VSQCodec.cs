using System;

namespace VocaloidFormatSupport.VSQ;

// VSQ（VOCALOID2）格式细则（单一真相源）：SMF Format 1（大端、division=480）+ 每 voice 轨内嵌 VOCALOID INI；⚠️ 音符全在 INI（无 note 事件），用户约定导出只写 INI、导入只读 INI。
internal static class VSQCodec
{
    /// <summary>VSQ 固定 PPQ（division = 0x01E0）。</summary>
    public const int Ppq = 480;

    /// <summary>[Common] Version 字段（实测真实文件恒为 DSB301）。</summary>
    public const string FormatVersion = "DSB301";

    /// <summary>[Common] Color 默认（实测 Meltdown/红豆 皆近此值）。</summary>
    public const string DefaultColor = "181,162,123";

    /// <summary>[Master] PreMeasure（实测恒为 4）。</summary>
    public const int PreMeasure = 4;

    /// <summary>单条 text meta 载荷上限。</summary>
    public const int MaxTextPayload = 127;

    /// <summary>DM 分片正文上限（= 127 - len("DM:NNNN:")）。</summary>
    public const int MaxTextBody = 119;

    // singer 默认（用户约定一律默认 Miku）；⚠️ 真实文件 IDS 写作小写 "miku"，此处按用户指示写 "Miku"。
    public const string DefaultSingerIds = "Miku";
    public const int DefaultSingerLanguage = 0;
    public const int DefaultSingerProgram = 9;
    public const int DefaultSingerOriginal = 9;
    public const string DefaultSingerIconId = "$07010009";

    // —— note 级 VOCALOID2 独有样式（Daisium 无对应 ⇒ 导入丢弃、导出写中性默认）——
    public const int NoteDynamics = 64;
    public const int NotePmBendDepth = 0;
    public const int NotePmBendLength = 0;
    public const int NotePmPortamentoUse = 0;
    public const int NoteDecGainRate = 50;
    public const int NoteAccent = 50;

    /// <summary>口开度曲线小节名。</summary>
    public const string OpeningSection = "OpeningBPList";

    /// <summary>Opening 默认（全开）。</summary>
    public const int OpeningDefault = 127;

    /// <summary>L0 第 3 字段（phoneticTiming）导出默认值（实测以 0.000000 为主）。</summary>
    public const string DefaultPhoneticTiming = "0.000000";

    /// <summary>VSQ 的 PBS（PitchBendSens）实际值域（实测 1..12）。</summary>
    public const int PitchBendSensMin = 1;
    public const int PitchBendSensMax = 12;

    /// <summary>track 混音：Panpot 值域 [-64, 64]（与 VPR 一致）。</summary>
    public const int PanpotScale = 64;

    // 控制器名 ↔ BPList 小节（导入 section→名，character 优先于 gender；导出名→section）。
    static readonly (string Controller, string Section)[] sMap =
    [
        ("dynamics", "DynamicsBPList"),
        ("pitchbend", "PitchBendBPList"),
        ("pitchbendsens", "PitchBendSensBPList"),
        ("pitchbendsensitivity", "PitchBendSensBPList"),
        ("character", "GenderFactorBPList"),
        ("gender", "GenderFactorBPList"),
        ("portamento", "PortamentoTimingBPList"),
    ];

    /// <summary>控制器名 → VSQ BPList 小节名；无对应返回 null（⇒ 该格式丢弃）。</summary>
    public static string? SectionForController(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        foreach (var (c, s) in sMap)
            if (string.Equals(c, name, StringComparison.OrdinalIgnoreCase))
                return s;
        return null;
    }

    /// <summary>VSQ BPList 小节名 → 控制器名；无对应返回 null（⇒ 丢弃）。</summary>
    public static string? ControllerForSection(string? section)
    {
        if (string.IsNullOrEmpty(section))
            return null;
        foreach (var (c, s) in sMap)
            if (string.Equals(s, section, StringComparison.OrdinalIgnoreCase))
                return c;
        return null;
    }
}
