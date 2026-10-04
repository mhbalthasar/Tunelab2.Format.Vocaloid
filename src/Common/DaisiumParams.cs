using System;
using System.Collections.Generic;

namespace VocaloidFormatSupport.Common;

// DaisiumForTuneLab 参数体系的人工镜像：键名/量程/默认值须与 DaisiumForTuneLab `Engine/Declarations.cs` 同步（本插件不引用其程序集）。
public static class DaisiumParams
{
    // 引擎身份 id（写入 SoundSourceInfo.Type）= DaisiumForTuneLab 的 manifest engine id。
    public const string EngineType = "Daisium";

    // 导出时 singer/voice 的 CompID 兜底值：必须是「B 开头 + 16 位纯大写字母数字」。
    public const string DefaultCompId = "BEKDC85ZLWXHZECF";

    // note 属性键（镜像 Declarations.BuildNoteProperties）。
    public const string KeyPhoneme = "Phoneme";
    public const string KeyMouth = "Mouth";
    public const string KeySampleToneOffset = "SampleToneOffset";
    public const string KeyAmplitudeGain = "AmplitudeGain";

    // part 属性键。
    public const string KeyPitchMode = "PitchMode";
    public const string KeyMinSegmentSpacing = "MinSegmentSpacing";

    // VOCALOID 侧 note 的 opening 默认值（= 全开）；Daisium Mouth 默认 1 与之对应。
    public const int VocOpeningDefault = 127;

    // VOCALOID 侧 note 的 velocity 默认值。
    public const int VocVelocityDefault = 64;

    /// <summary>Daisium Mouth 属性值域（Slider.Linear 默认 1，0..1）。</summary>
    public const double MouthDefault = 1.0;

    /// <summary>归一化 CompID：必须严格「B 开头 + 16 位纯大写字母数字」，否则返回 <see cref="DefaultCompId"/>。</summary>
    public static string NormalizeCompId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length != 16 || id[0] != 'B')
            return DefaultCompId;
        foreach (var c in id)
        {
            var ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z');
            if (!ok)
                return DefaultCompId;
        }
        return id;
    }

    /// <summary>CompID 是否合法（B + 15 位大写字母数字）。</summary>
    public static bool IsValidCompId(string? id)
        => !string.IsNullOrEmpty(id) && id.Length == 16 && id[0] == 'B' && NormalizeCompId(id) == id;
}

// VOCALOID note 级、Daisium 侧无对应的参数：导入丢弃、导出写这些默认值（取自 VOCALOID4/5 编辑器内置样式）。
public static class VocNoteDefaults
{
    public const int Accent = 50;
    public const int BendDepth = 8;
    public const int BendLength = 0;
    public const int Decay = 50;
    public const int FallPort = 0;
    public const int RisePort = 0;
    public const int Opening = 127;
    public const int Velocity = 64;
}

/// <summary>一条 Daisium 自动化轨与对应 VOCALOID 控制器之间的映射与域换算（同名直映 + 线性域换算，未映射者丢弃）。</summary>
public sealed class AutomationSpec
{
    /// <summary>Daisium 轨键（= 工程序列化引用，勿改）。</summary>
    public required string Key { get; init; }

    /// <summary>人类可读名（仅文档/日志用）。</summary>
    public required string DisplayName { get; init; }

    /// <summary>Daisium 轨值域下界。</summary>
    public required double Min { get; init; }

    /// <summary>Daisium 轨值域上界。</summary>
    public required double Max { get; init; }

    /// <summary>Daisium 轨默认值。</summary>
    public required double Default { get; init; }

    /// <summary>对应的 VOCALOID 控制器名（小写，可多个别名）；VPR 用长名。</summary>
    public required string[] VocNames { get; init; }

    /// <summary>VSQX3 的 mCtrl 控制器名（3 字母，如 DYN/PIT/POR）；为空 = VSQX3 无此控制器（如 GWL/XSY，仅 V4+ 有）⇒ 不发射。</summary>
    public required string VSQX3Id { get; init; }

    /// <summary>VSQX4 的 cc 单字母 id（DYN/BRE/BRI/CLE/GEN/PIT/PBS/POR/XSY/GWL ↔ D B R C G P S T X W）。</summary>
    public required string VSQXCcId { get; init; }

    /// <summary>VPR 控制器名（camelCase，如 pitchBend/pitchBendSens）；为空回退 <see cref="VocNames"/>[0]。</summary>
    public string? VprName { get; init; }

    /// <summary>VPR 导出时使用的控制器名。</summary>
    public string VprControllerName => VprName ?? VocNames[0];

    /// <summary>VOCALOID 整数域下界（规范域 = VSQX cc 域）。</summary>
    public double VocMin { get; init; }

    /// <summary>VOCALOID 整数域上界（规范域 = VSQX cc 域）。</summary>
    public double VocMax { get; init; } = 127;

    /// <summary>VOCALOID 域是否需要取反（GEN/character：VOCALOID 值升 = Daisium 值降）。</summary>
    public bool Invert { get; init; }

    /// <summary>VOCALOID 规范域整数值 → Daisium 曲线值。</summary>
    public double ToDaisium(double voc) => ToDaisium(voc, VocMin, VocMax);

    /// <summary>Daisium 曲线值 → VOCALOID 规范域整数值。</summary>
    public int ToVoc(double daisium) => ToVoc(daisium, VocMin, VocMax);

    // 允许调用方覆盖 VOCALOID 域（VPR 对 character/exciter 用有符号编码，见 VprCodec）。
    internal double ToDaisium(double voc, double vocMin, double vocMax)
    {
        var n = (voc - vocMin) / (vocMax - vocMin);
        n = Math.Clamp(n, 0, 1);
        if (Invert)
            n = 1 - n;
        return Min + n * (Max - Min);
    }

    internal int ToVoc(double daisium, double vocMin, double vocMax)
    {
        var n = (daisium - Min) / (Max - Min);
        n = Math.Clamp(n, 0, 1);
        if (Invert)
            n = 1 - n;
        return (int)Math.Round(vocMin + n * (vocMax - vocMin));
    }
}

// Daisium 自动化轨表 + VOCALOID 控制器映射表（单一真相源）；VOCALOID 规范域：常规 0..127、PIT ±8191、PBS 0..24。
public static class AutomationMap
{
    // Daisium 轨（量程/默认值镜像自 Declarations.Tracks + BuildAutomationConfigs 的 PIT/PBS）。
    public static readonly AutomationSpec[] Tracks =
    [
        new() { Key = "DYN", DisplayName = "Dynamics",      Min = -1, Max = 1,  Default = 0, VocNames = ["dynamics"],                   VSQX3Id = "DYN", VSQXCcId = "D" },
        new() { Key = "BRI", DisplayName = "Brightness",    Min = -1, Max = 1,  Default = 0, VocNames = ["brightness"],                 VSQX3Id = "BRI", VSQXCcId = "R" },
        new() { Key = "BRE", DisplayName = "Breathiness",   Min = 0,  Max = 1,  Default = 0, VocNames = ["breathiness"],                VSQX3Id = "BRE", VSQXCcId = "B" },
        new() { Key = "CLE", DisplayName = "Clearness",     Min = 0,  Max = 1,  Default = 0, VocNames = ["clearness"],                  VSQX3Id = "CLE", VSQXCcId = "C" },
        new() { Key = "GEN", DisplayName = "Gender",        Min = -1, Max = 1,  Default = 0, VocNames = ["character", "gender"],       VSQX3Id = "GEN", VSQXCcId = "G", Invert = true },
        new() { Key = "GWL", DisplayName = "Growl",         Min = 0,  Max = 1,  Default = 0, VocNames = ["growl"],                      VSQX3Id = "",    VSQXCcId = "W" },
        new() { Key = "XSY", DisplayName = "CrossSynth",    Min = 0,  Max = 1,  Default = 0, VocNames = ["crosssynthesis", "xsy", "xsynth"], VSQX3Id = "", VSQXCcId = "X" },
        new() { Key = "PIT", DisplayName = "PitchBend",     Min = -1, Max = 1,  Default = 0, VocNames = ["pitchbend"],                 VSQX3Id = "PIT", VSQXCcId = "P", VocMin = -8192, VocMax = 8191, VprName = "pitchBend" },
        new() { Key = "PBS", DisplayName = "PitchBendSens", Min = 0,  Max = 24, Default = 2, VocNames = ["pitchbendsens", "pitchbendsensitivity"], VSQX3Id = "PBS", VSQXCcId = "S", VocMax = 24, VprName = "pitchBendSens" },
    ];

    // 「Daisium 无、VOCALOID 有」的 part 级控制器：导入丢弃、导出写默认常量曲线（VSQX3Id/VSQXCcId 为空 = 该格式不发射）。
    public static readonly (string Name, string VSQX3Id, string VSQXCcId, int DefaultValue)[] DiscardedPartControllers =
    [
        ("portamento", "POR", "T", 64),   // POR：中性 64
        ("exciter",    "",    "",  64),   // EXC：VOCALOID4/5 有、无 VSQX cc id（仅 VPR 出现）
        ("air",        "",    "",   0),   // AIR：VOCALOID5 独有（仅 VPR 出现），中性 0
    ];

    static readonly Dictionary<string, AutomationSpec> sByVocName = BuildVocNameIndex();
    static readonly Dictionary<string, AutomationSpec> sByCcId = BuildCcIdIndex();
    static readonly Dictionary<string, AutomationSpec> sByV3Id = BuildV3IdIndex();

    static Dictionary<string, AutomationSpec> BuildVocNameIndex()
    {
        var d = new Dictionary<string, AutomationSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in Tracks)
            foreach (var name in spec.VocNames)
                d[name] = spec;
        return d;
    }

    static Dictionary<string, AutomationSpec> BuildCcIdIndex()
    {
        var d = new Dictionary<string, AutomationSpec>(StringComparer.Ordinal);
        foreach (var spec in Tracks)
            d[spec.VSQXCcId] = spec;
        return d;
    }

    static Dictionary<string, AutomationSpec> BuildV3IdIndex()
    {
        var d = new Dictionary<string, AutomationSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in Tracks)
        {
            if (spec.VSQX3Id.Length == 0)
                continue;   // VSQX3 无此控制器
            d[spec.VSQX3Id] = spec;
        }
        return d;
    }

    /// <summary>按 VOCALOID 控制器名（VPR 长名/别名，大小写不敏感）查 Daisium 轨；无对应返回 null（⇒ 丢弃）。</summary>
    public static AutomationSpec? FindByVocName(string? name)
        => !string.IsNullOrEmpty(name) && sByVocName.TryGetValue(name, out var s) ? s : null;

    /// <summary>按 VSQX4 cc 单字母 id 查 Daisium 轨；无对应返回 null（⇒ 丢弃）。</summary>
    public static AutomationSpec? FindByCcId(string? ccId)
        => !string.IsNullOrEmpty(ccId) && sByCcId.TryGetValue(ccId, out var s) ? s : null;

    /// <summary>按 VSQX3 mCtrl 3 字母 id 查 Daisium 轨；无对应返回 null（⇒ 丢弃）。</summary>
    public static AutomationSpec? FindByV3Id(string? id)
        => !string.IsNullOrEmpty(id) && sByV3Id.TryGetValue(id, out var s) ? s : null;

    public static AutomationSpec? FindByKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        foreach (var spec in Tracks)
            if (spec.Key == key)
                return spec;
        return null;
    }
}
