using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using VocaloidFormatSupport.Common;

namespace VocaloidFormatSupport.Vpr;

// VocProject → VPR（ZIP + Project/sequence.json）；支持 V5 与 V6 两种版本（V6 增 mainTuning/langID/aiExp/directPitches）。
internal static class VprWriter
{
    const string Vendor = "Yamaha Corporation";
    const string DefaultTitle = "Untitled";
    const string DefaultStyleName = "No Effect";

    // note 级 singingSkill 默认：V5 实测 duration=158，V6 实测 160；weight 恒 64/64。
    const int SingingSkillDurationV5 = 158;
    const int SingingSkillDurationV6 = 160;
    const int SingingSkillWeight = 64;

    // V6 aiExp 各维默认（实测全 0.5）。
    const double AiExpDefault = 0.5;

    /// <summary>写出 VPR（version 决定 V5 / V6 结构）。</summary>
    public static void Write(Stream output, VocProject voc, VprVersion version)
    {
        using var zip = VprCodec.Create(output);
        var entry = zip.CreateEntry(VprCodec.SequenceEntry, CompressionLevel.NoCompression);
        using var es = entry.Open();
        WriteJson(es, voc, version);
        es.Flush();
    }

    static void WriteJson(Stream s, VocProject voc, VprVersion version)
    {
        var options = new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        using var w = new Utf8JsonWriter(s, options);

        w.WriteStartObject();
        WriteMasterTrack(w, voc, ProjectEndTick(voc), version);
        w.WriteString("title", DefaultTitle);
        WriteTracks(w, voc, version);
        w.WriteString("vender", Vendor);

        w.WritePropertyName("version");
        w.WriteStartObject();
        w.WriteNumber("major", version == VprVersion.V6 ? VprCodec.Version6Major : VprCodec.Version5Major);
        w.WriteNumber("minor", version == VprVersion.V6 ? VprCodec.Version6Minor : VprCodec.Version5Minor);
        w.WriteNumber("revision", version == VprVersion.V6 ? VprCodec.Version6Revision : VprCodec.Version5Revision);
        w.WriteEndObject();

        WriteVoices(w, voc);
        w.WriteEndObject();
        w.Flush();
    }

    // ── masterTrack ──

    static void WriteMasterTrack(Utf8JsonWriter w, VocProject voc, long endTick, VprVersion version)
    {
        w.WritePropertyName("masterTrack");
        w.WriteStartObject();
        w.WriteNumber("samplingRate", VprCodec.SamplingRate);
        if (version == VprVersion.V6)
            w.WriteNumber("mainTuning", VprCodec.MainTuning);

        w.WritePropertyName("loop");
        w.WriteStartObject();
        w.WriteBoolean("isEnabled", false);
        w.WriteNumber("begin", 0);
        w.WriteNumber("end", endTick);
        w.WriteEndObject();

        w.WritePropertyName("tempo");
        w.WriteStartObject();
        w.WriteBoolean("isFolded", false);
        w.WriteNumber("height", 0.0);
        w.WritePropertyName("global");
        w.WriteStartObject();
        w.WriteBoolean("isEnabled", false);
        w.WriteNumber("value", 12000);
        w.WriteEndObject();
        if (version == VprVersion.V6)
        {
            w.WritePropertyName("ara");
            w.WriteStartObject();
            w.WriteBoolean("isEnabled", false);
            w.WriteEndObject();
        }
        w.WritePropertyName("events");
        w.WriteStartArray();
        if (voc.Tempos.Count == 0)
            WriteTempoEvent(w, 0, 120);
        else
            foreach (var t in voc.Tempos)
                WriteTempoEvent(w, t.Pos, t.Bpm);
        w.WriteEndArray();
        w.WriteEndObject();

        w.WritePropertyName("timeSig");
        w.WriteStartObject();
        w.WriteBoolean("isFolded", false);
        w.WritePropertyName("events");
        w.WriteStartArray();
        if (voc.TimeSignatures.Count == 0)
            WriteTimeSigEvent(w, 0, 4, 4);
        else
            foreach (var ts in voc.TimeSignatures)
                WriteTimeSigEvent(w, ts.BarIndex, ts.Numerator, ts.Denominator);
        w.WriteEndArray();
        w.WriteEndObject();

        w.WritePropertyName("volume");
        w.WriteStartObject();
        w.WriteBoolean("isFolded", false);
        w.WriteNumber("height", 0.0);
        w.WritePropertyName("events");
        w.WriteStartArray();
        w.WriteStartObject();
        w.WriteNumber("pos", 0);
        w.WriteNumber("value", 0);
        w.WriteEndObject();
        w.WriteEndArray();
        w.WriteEndObject();

        w.WriteEndObject();
    }

    static void WriteTempoEvent(Utf8JsonWriter w, double pos, double bpm)
    {
        w.WriteStartObject();
        w.WriteNumber("pos", (long)Math.Round(pos));
        w.WriteNumber("value", (long)Math.Round(Math.Clamp(bpm * 100.0, 2000, 30000)));   // schema: 2000..30000
        w.WriteEndObject();
    }

    static void WriteTimeSigEvent(Utf8JsonWriter w, int bar, int numer, int denom)
    {
        w.WriteStartObject();
        w.WriteNumber("bar", bar);
        w.WriteNumber("numer", Math.Clamp(numer, 1, 255));
        w.WriteNumber("denom", denom is 1 or 2 or 4 or 8 or 16 or 32 ? denom : 4);
        w.WriteEndObject();
    }

    // ── voices（去重的 compID）──

    static void WriteVoices(Utf8JsonWriter w, VocProject voc)
    {
        var seen = new List<string>();
        foreach (var t in voc.Tracks)
            foreach (var p in t.Parts)
            {
                var id = DaisiumParams.NormalizeCompId(p.CompId);
                if (!seen.Contains(id))
                    seen.Add(id);
            }
        if (seen.Count == 0)
            seen.Add(DaisiumParams.DefaultCompId);

        w.WritePropertyName("voices");
        w.WriteStartArray();
        foreach (var id in seen)
        {
            w.WriteStartObject();
            w.WriteString("compID", id);
            w.WriteString("name", id);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    // ── tracks / parts / notes ──

    static void WriteTracks(Utf8JsonWriter w, VocProject voc, VprVersion version)
    {
        w.WritePropertyName("tracks");
        w.WriteStartArray();
        foreach (var t in voc.Tracks)
            WriteTrack(w, t, version);
        w.WriteEndArray();
    }

    static void WriteTrack(Utf8JsonWriter w, VocTrack t, VprVersion version)
    {
        w.WriteStartObject();
        w.WriteNumber("busNo", 0);
        w.WriteNumber("color", 0);
        w.WriteNumber("height", 0.0);
        w.WriteBoolean("isFolded", false);
        if (version == VprVersion.V6)
            w.WriteNumber("lastScrollPositionNoteNumber", 60);
        w.WriteBoolean("isMuted", t.Mute);
        w.WriteBoolean("isSoloMode", t.Solo);
        w.WriteString("name", t.Name ?? string.Empty);
        WriteMixerEnvelope(w, "panpot", (long)Math.Round(Math.Clamp(t.Pan, -1, 1) * 64), -64, 64, 40.0);
        WriteMixerEnvelope(w, "volume", (long)Math.Round(Math.Clamp(t.GainDb, -89.8, 6.0) * 10), -898, 60, 40.0);

        w.WritePropertyName("parts");
        w.WriteStartArray();
        foreach (var p in t.Parts)
            WritePart(w, p, version);
        w.WriteEndArray();

        w.WriteNumber("type", 0);
        w.WriteEndObject();
    }

    static void WriteMixerEnvelope(Utf8JsonWriter w, string name, long value, long min, long max, double height)
    {
        w.WritePropertyName(name);
        w.WriteStartObject();
        w.WriteBoolean("isFolded", true);
        w.WriteNumber("height", height);
        w.WritePropertyName("events");
        w.WriteStartArray();
        w.WriteStartObject();
        w.WriteNumber("pos", 0);
        w.WriteNumber("value", Math.Clamp(value, min, max));
        w.WriteEndObject();
        w.WriteEndArray();
        w.WriteEndObject();
    }

    static void WritePart(Utf8JsonWriter w, VocPart p, VprVersion version)
    {
        w.WriteStartObject();
        w.WriteNumber("duration", Math.Max(1, (long)Math.Round(p.Duration)));

        w.WritePropertyName("notes");
        w.WriteStartArray();
        foreach (var n in p.Notes)
            WriteNote(w, n, version);
        w.WriteEndArray();

        w.WriteNumber("pos", (long)Math.Round(p.Pos));
        w.WriteString("styleName", DefaultStyleName);

        w.WritePropertyName("voice");
        w.WriteStartObject();
        w.WriteString("compID", DaisiumParams.NormalizeCompId(p.CompId));
        w.WriteNumber("langID", 0);
        w.WriteEndObject();

        w.WritePropertyName("controllers");
        w.WriteStartArray();
        foreach (var c in p.Controllers)
            WriteController(w, c);
        w.WriteEndArray();

        w.WriteEndObject();
    }

    static void WriteNote(Utf8JsonWriter w, VocNote n, VprVersion version)
    {
        w.WriteStartObject();
        if (version == VprVersion.V6)
            WriteAiExp(w);
        if (version == VprVersion.V6)
            WriteDirectPitches(w, n);
        w.WriteNumber("duration", Math.Max(1, (long)Math.Round(n.Dur)));

        w.WritePropertyName("exp");
        w.WriteStartObject();
        if (version == VprVersion.V6)
        {
            w.WriteNumber("accent", VocNoteDefaults.Accent);
            w.WriteNumber("decay", VocNoteDefaults.Decay);
            w.WriteNumber("bendDepth", VocNoteDefaults.BendDepth);
            w.WriteNumber("bendLength", VocNoteDefaults.BendLength);
        }
        w.WriteNumber("opening", Math.Clamp(n.Opening, 0, 127));
        w.WriteEndObject();

        if (version == VprVersion.V6)
            w.WriteBoolean("isAiVibratoEnabled", false);
        w.WriteBoolean("isProtected", n.PhonemeLocked);
        if (version == VprVersion.V6)
            w.WriteNumber("langID", 0);
        if (!string.IsNullOrEmpty(n.Lyric))
            w.WriteString("lyric", n.Lyric);
        w.WriteNumber("number", Math.Clamp(n.Number, 0, 127));
        if (n.PhonemeLocked && !string.IsNullOrEmpty(n.Phoneme))
            w.WriteString("phoneme", n.Phoneme);
        w.WriteNumber("pos", (long)Math.Round(n.Pos));

        w.WritePropertyName("singingSkill");
        w.WriteStartObject();
        w.WriteNumber("duration", version == VprVersion.V6 ? SingingSkillDurationV6 : SingingSkillDurationV5);
        w.WritePropertyName("weight");
        w.WriteStartObject();
        w.WriteNumber("pre", SingingSkillWeight);
        w.WriteNumber("post", SingingSkillWeight);
        w.WriteEndObject();
        w.WriteEndObject();

        w.WriteNumber("velocity", Math.Clamp(n.Velocity, 0, 127));
        WriteVibrato(w, n.Vibrato);
        w.WriteEndObject();
    }

    // V6 note 级 aiExp：全部维度写默认 0.5。
    static void WriteAiExp(Utf8JsonWriter w)
    {
        w.WritePropertyName("aiExp");
        w.WriteStartObject();
        foreach (var key in new[]
        {
            "pitchFine", "pitchDriftStart", "pitchDriftEnd", "pitchScalingCenter", "pitchScalingOrigin",
            "pitchTransitionStart", "pitchTransitionEnd", "amplitudeWhole", "amplitudeStart", "amplitudeEnd",
            "vibratoLeadingDepth", "vibratoFollowingDepth",
        })
            w.WriteNumber(key, AiExpDefault);
        w.WriteEndObject();
    }

    // V6 note 级绝对音高线：pos 相对 note，value = 原始值；末尾追加 "ZeroPitch" 终止哨兵。
    static void WriteDirectPitches(Utf8JsonWriter w, VocNote n)
    {
        if (n.DirectPitches.Count == 0)
            return;
        w.WritePropertyName("directPitches");
        w.WriteStartArray();
        foreach (var p in n.DirectPitches)
        {
            w.WriteStartObject();
            w.WriteNumber("pos", (long)Math.Round(p.Pos));
            w.WriteNumber("value", VprCodec.NumberToRawPitch(p.Number));
            w.WriteEndObject();
        }
        w.WriteStartObject();
        w.WriteNumber("pos", (long)Math.Round(n.DirectPitches[^1].Pos) + 1);
        w.WriteString("value", VprCodec.ZeroPitch);
        w.WriteEndObject();
        w.WriteEndArray();
    }

    static void WriteVibrato(Utf8JsonWriter w, VocVibrato? vib)
    {
        w.WritePropertyName("vibrato");
        w.WriteStartObject();
        if (vib is { Dur: > 0 })
        {
            w.WriteNumber("type", Math.Clamp(vib.Type, 0, 16));
            w.WriteNumber("duration", Math.Max(0, (long)Math.Round(vib.Dur)));
            WriteEnvelopePoint(w, "depths", Math.Clamp((int)Math.Round(vib.Depth), 0, 127));
            WriteEnvelopePoint(w, "rates", Math.Clamp((int)Math.Round(vib.Rate), 0, 127));
        }
        else
        {
            w.WriteNumber("type", 0);
            w.WriteNumber("duration", 0);
        }
        w.WriteEndObject();
    }

    static void WriteEnvelopePoint(Utf8JsonWriter w, string name, int value)
    {
        w.WritePropertyName(name);
        w.WriteStartArray();
        w.WriteStartObject();
        w.WriteNumber("pos", 0);
        w.WriteNumber("value", value);
        w.WriteEndObject();
        w.WriteEndArray();
    }

    static void WriteController(Utf8JsonWriter w, VocController c)
    {
        w.WriteStartObject();
        w.WriteString("name", c.Name);
        w.WritePropertyName("events");
        w.WriteStartArray();
        foreach (var e in c.Events)
        {
            w.WriteStartObject();
            w.WriteNumber("pos", (long)Math.Round(e.Pos));
            w.WriteNumber("value", VprCodec.ToVpr(c.Name, e.Value));
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    // ── 计算工程结束 tick（loop.end）──

    static long ProjectEndTick(VocProject voc)
    {
        double end = 0;
        foreach (var t in voc.Tracks)
            foreach (var p in t.Parts)
            {
                end = Math.Max(end, p.Pos + p.Duration);
                foreach (var n in p.Notes)
                    end = Math.Max(end, p.Pos + n.Pos + n.Dur);
            }
        return Math.Max(1, (long)Math.Round(end));
    }
}
