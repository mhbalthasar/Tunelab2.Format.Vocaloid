using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace VocaloidFormatSupport.Vpr;

/// <summary>VPR 写出目标版本。</summary>
public enum VprVersion
{
    V5 = 5,
    V6 = 6,
}

// VPR 编码细则：ZIP 容器（唯一必需成员 Project/sequence.json，实测恒 STORED）+ 有符号控制器归一化（character/exciter 加减 64）。
internal static class VprCodec
{
    /// <summary>JSON 主成员路径。</summary>
    public const string SequenceEntry = "Project/sequence.json";

    /// <summary>VOCALOID5 VPR 版本号。</summary>
    public const int Version5Major = 5;
    public const int Version5Minor = 0;
    public const int Version5Revision = 0;

    /// <summary>VOCALOID6 VPR 版本号（实测 6.5.1）。</summary>
    public const int Version6Major = 6;
    public const int Version6Minor = 5;
    public const int Version6Revision = 1;

    /// <summary>V6 主调音（A4 基准，Hz）。</summary>
    public const double MainTuning = 440.0;

    /// <summary>directPitches 的终止哨兵（字符串值，恒为末项）。</summary>
    public const string ZeroPitch = "ZeroPitch";

    /// <summary>directPitches 原始值 → MIDI note number（源自 VSMScore.GetRawNoteNumberFromPitch：note = (raw + 6900) / 100）。</summary>
    public static double RawPitchToNumber(double raw) => (raw + 6900.0) / 100.0;

    /// <summary>MIDI note number → directPitches 原始值。</summary>
    public static double NumberToRawPitch(double number) => number * 100.0 - 6900.0;

    /// <summary>VPR 固定 PPQ。</summary>
    public const int Ppq = 480;

    /// <summary>采样率（schema 仅允许 44100/48000/96000）。</summary>
    public const int SamplingRate = 44100;

    /// <summary>有符号控制器的零偏移（canonical = raw + Offset）。</summary>
    public const int SignedOffset = 64;

    static readonly HashSet<string> sSigned = new(StringComparer.Ordinal)
    {
        "character", "exciter",
    };

    /// <summary>该 VPR 控制器是否为有符号编码。</summary>
    public static bool IsSigned(string name) => sSigned.Contains(name);

    /// <summary>VPR 原值 → 规范域（0..127）。</summary>
    public static int ToCanonical(string name, int raw)
        => IsSigned(name) ? raw + SignedOffset : raw;

    /// <summary>规范域（0..127）→ VPR 原值。</summary>
    public static int ToVpr(string name, int canonical)
        => IsSigned(name) ? canonical - SignedOffset : canonical;

    // ── ZIP ──

    /// <summary>打开 .vpr（ZIP）。必要时先把流拷进内存以支持 seek（ZipArchive 读模式需可 seek）。</summary>
    public static ZipArchive OpenRead(Stream stream, out Stream? temp)
    {
        temp = null;
        if (stream.CanSeek)
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        temp = ms;
        return new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
    }

    /// <summary>创建 .vpr（ZIP，STORED）。</summary>
    public static ZipArchive Create(Stream output)
        => new(output, ZipArchiveMode.Create, leaveOpen: true);

    /// <summary>取 sequence.json 条目；容忍部分文件写出的反斜杠分隔名（如 "Project\sequence.json"）。</summary>
    public static ZipArchiveEntry? FindSequence(ZipArchive zip)
    {
        var entry = zip.GetEntry(SequenceEntry);
        if (entry != null)
            return entry;
        foreach (var e in zip.Entries)
            if (e.FullName.Replace('\\', '/') == SequenceEntry)
                return e;
        return null;
    }
}
