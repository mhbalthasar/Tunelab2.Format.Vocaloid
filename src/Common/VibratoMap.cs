using System;

namespace VocaloidFormatSupport.Common;

// VOCALOID 颤音 (vibRate/vibDep, 0..127) ⇄ Daisium VibratoInfo (Frequency Hz / Amplitude 半音) 的线性经验换算（用户约定，常数集中于此）；包络取峰值。
public static class VibratoMap
{
    /// <summary>rate 下界对应的频率（Hz）。</summary>
    public const double RateMinHz = 2.0;

    /// <summary>rate 上界对应的频率（Hz）。</summary>
    public const double RateMaxHz = 10.0;

    /// <summary>depth 上界对应的峰值幅度（半音）。</summary>
    public const double DepthMaxSemitones = 1.0;

    const double VocMax = 127.0;

    /// <summary>VOCALOID rate(0..127) → 频率(Hz)。</summary>
    public static double RateToHz(double rate)
        => RateMinHz + Clamp01(rate / VocMax) * (RateMaxHz - RateMinHz);

    /// <summary>频率(Hz) → VOCALOID rate(0..127)。</summary>
    public static int HzToRate(double hz)
        => ToVoc(hz, RateMinHz, RateMaxHz);

    /// <summary>VOCALOID depth(0..127) → 峰值幅度(半音)。</summary>
    public static double DepthToSemitones(double depth)
        => Clamp01(depth / VocMax) * DepthMaxSemitones;

    /// <summary>峰值幅度(半音) → VOCALOID depth(0..127)。</summary>
    public static int SemitonesToDepth(double semitones)
        => ToVoc(semitones, 0, DepthMaxSemitones);

    static int ToVoc(double v, double min, double max)
    {
        var n = (v - min) / (max - min);
        return (int)Math.Round(Clamp01(n) * VocMax);
    }

    static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
}
