using System.Collections.Generic;

namespace VocaloidFormatSupport.Common;

// 中立模型：以 VOCALOID 形态承载数据（控制器整数原值、音符 part 相对 tick，时基一律 PPQ 480）。
public sealed class VocProject
{
    public List<VocTempo> Tempos { get; } = new();
    public List<VocTimeSignature> TimeSignatures { get; } = new();
    public List<VocTrack> Tracks { get; } = new();
}

public sealed class VocTempo
{
    /// <summary>全局 tick。</summary>
    public double Pos { get; set; }
    public double Bpm { get; set; }
}

public sealed class VocTimeSignature
{
    /// <summary>0 基小节号。</summary>
    public int BarIndex { get; set; }
    public int Numerator { get; set; } = 4;
    public int Denominator { get; set; } = 4;
}

public sealed class VocTrack
{
    public string Name { get; set; } = string.Empty;

    /// <summary>音量，已换算为 dB（VOCALOID 音量单位 = 0.1 dB）。</summary>
    public double GainDb { get; set; }

    /// <summary>声像，[-1, 1]。</summary>
    public double Pan { get; set; }
    public bool Mute { get; set; }
    public bool Solo { get; set; }
    public List<VocPart> Parts { get; } = new();
}

public sealed class VocPart
{
    public string Name { get; set; } = string.Empty;

    /// <summary>part 起点，全局绝对 tick。</summary>
    public double Pos { get; set; }

    /// <summary>part 长度（tick）。</summary>
    public double Duration { get; set; }

    /// <summary>singer CompID（原样保留；导出时经 DaisiumParams.NormalizeCompId 归一化）。</summary>
    public string CompId { get; set; } = string.Empty;

    public List<VocNote> Notes { get; } = new();
    public List<VocController> Controllers { get; } = new();
}

public sealed class VocNote
{
    /// <summary>part 相对 tick。</summary>
    public double Pos { get; set; }
    public double Dur { get; set; }

    /// <summary>MIDI note number。</summary>
    public int Number { get; set; } = 60;

    public int Velocity { get; set; } = DaisiumParams.VocVelocityDefault;
    public string Lyric { get; set; } = string.Empty;

    /// <summary>显式音素串（原样）。</summary>
    public string Phoneme { get; set; } = string.Empty;

    /// <summary>音素是否被钉死（VSQX <c>&lt;phnms lock="1"&gt;</c> / VPR <c>isProtected</c>）。</summary>
    public bool PhonemeLocked { get; set; }

    /// <summary>口开度 0..127（VPR exp.opening）。</summary>
    public int Opening { get; set; } = DaisiumParams.VocOpeningDefault;

    public VocVibrato? Vibrato { get; set; }

    /// <summary>绝对音高线（V6 VPR directPitches）：note 相对 tick，值 = MIDI note number（连续）。空 = 无。</summary>
    public List<VocPitchPoint> DirectPitches { get; } = new();
}

/// <summary>绝对音高线的一个采样点：Pos = note 相对 tick，Number = MIDI note number。</summary>
public readonly record struct VocPitchPoint(double Pos, double Number);

public sealed class VocController
{
    /// <summary>VOCALOID 控制器名（小写长名，如 "dynamics"）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>事件（pos = part 相对 tick；value = 规范域整数）。</summary>
    public List<VocControllerEvent> Events { get; } = new();
}

public readonly record struct VocControllerEvent(double Pos, int Value);

public sealed class VocVibrato
{
    /// <summary>VOCALOID 颤音预设类型（0..15）；Daisium VibratoInfo 无此字段，导出恒 0。</summary>
    public int Type { get; set; }

    /// <summary>颤音时长（tick）。</summary>
    public double Dur { get; set; }

    /// <summary>深度包络代表值 0..127（取包络峰值）。</summary>
    public double Depth { get; set; }

    /// <summary>速率包络代表值 0..127（取包络峰值）。</summary>
    public double Rate { get; set; }
}
