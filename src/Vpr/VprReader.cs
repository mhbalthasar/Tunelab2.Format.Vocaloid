using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VocaloidFormatSupport.Common;

namespace VocaloidFormatSupport.Vpr;

// VPR（VOCALOID5/6）导入：ZIP → Project/sequence.json → VocProject；只取与 Daisium 对应字段，VOCALOID 独有字段忽略，part 外事件（pos<0）丢弃。
internal static class VprReader
{
    public static VocProject Read(Stream json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var proj = new VocProject();

        if (root.TryGetProperty("masterTrack", out var mt))
        {
            if (mt.TryGetProperty("tempo", out var tempo) && tempo.TryGetProperty("events", out var tev))
                foreach (var e in tev.EnumerateArray())
                    proj.Tempos.Add(new VocTempo { Pos = GetDouble(e, "pos"), Bpm = GetDouble(e, "value") / 100.0 });

            if (mt.TryGetProperty("timeSig", out var ts) && ts.TryGetProperty("events", out var sev))
                foreach (var e in sev.EnumerateArray())
                    proj.TimeSignatures.Add(new VocTimeSignature
                    {
                        BarIndex = GetInt(e, "bar"),
                        Numerator = GetInt(e, "numer", 4),
                        Denominator = GetInt(e, "denom", 4),
                    });
        }

        if (root.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tracks.EnumerateArray())
                proj.Tracks.Add(ReadTrack(t));
        }

        return proj;
    }

    static VocTrack ReadTrack(JsonElement t)
    {
        var track = new VocTrack
        {
            Name = GetString(t, "name"),
            GainDb = GetVolume(t, "volume") / 10.0,   // VOCALOID 音量单位 = 0.1 dB
            Pan = GetVolume(t, "panpot") / 64.0,      // panpot ∈ [-64,64] → [-1,1]
            Mute = GetBool(t, "isMuted"),
            Solo = GetBool(t, "isSoloMode"),
        };

        if (t.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            foreach (var p in parts.EnumerateArray())
            {
                // 只处理含 notes 的（vocal）part；wav / region part 跳过。
                if (!p.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Array)
                    continue;
                track.Parts.Add(ReadPart(p, notes));
            }

        return track;
    }

    static VocPart ReadPart(JsonElement p, JsonElement notes)
    {
        var part = new VocPart
        {
            Name = GetString(p, "name"),
            Pos = GetDouble(p, "pos"),
            Duration = GetDouble(p, "duration"),
            CompId = ReadCompId(p),
        };

        foreach (var n in notes.EnumerateArray())
            part.Notes.Add(ReadNote(n));

        if (p.TryGetProperty("controllers", out var ctrls) && ctrls.ValueKind == JsonValueKind.Array)
            foreach (var c in ctrls.EnumerateArray())
                part.Controllers.Add(ReadController(c));

        return part;
    }

    static string ReadCompId(JsonElement p)
    {
        if (p.TryGetProperty("voice", out var v) && v.ValueKind == JsonValueKind.Object)
            return GetString(v, "compID");
        return string.Empty;
    }

    static VocNote ReadNote(JsonElement n)
    {
        var note = new VocNote
        {
            Pos = GetDouble(n, "pos"),
            Dur = GetDouble(n, "duration"),
            Number = GetInt(n, "number", 60),
            Velocity = GetInt(n, "velocity", DaisiumParams.VocVelocityDefault),
            Lyric = GetString(n, "lyric"),
            Phoneme = GetString(n, "phoneme"),
            PhonemeLocked = GetBool(n, "isProtected"),
            Opening = DaisiumParams.VocOpeningDefault,
        };

        if (n.TryGetProperty("exp", out var exp) && exp.ValueKind == JsonValueKind.Object)
            note.Opening = GetInt(exp, "opening", DaisiumParams.VocOpeningDefault);

        if (n.TryGetProperty("vibrato", out var vib) && vib.ValueKind == JsonValueKind.Object)
        {
            var dur = GetDouble(vib, "duration");
            if (dur > 0)
            {
                note.Vibrato = new VocVibrato
                {
                    Type = GetInt(vib, "type"),
                    Dur = dur,
                    Depth = MaxEventValue(vib, "depths"),
                    Rate = MaxEventValue(vib, "rates"),
                };
            }
        }

        // V6 绝对音高线：pos = note 相对 tick，value = 原始值（字符串 "ZeroPitch" 为终止哨兵）。
        if (n.TryGetProperty("directPitches", out var dps) && dps.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in dps.EnumerateArray())
            {
                if (!e.TryGetProperty("value", out var val) || val.ValueKind == JsonValueKind.String)
                    break;
                if (val.ValueKind != JsonValueKind.Number)
                    continue;
                note.DirectPitches.Add(new VocPitchPoint(GetDouble(e, "pos"), VprCodec.RawPitchToNumber(val.GetDouble())));
            }
        }

        return note;
    }

    static VocController ReadController(JsonElement c)
    {
        var name = GetString(c, "name");
        var vc = new VocController { Name = name };
        if (c.TryGetProperty("events", out var ev) && ev.ValueKind == JsonValueKind.Array)
            foreach (var e in ev.EnumerateArray())
            {
                var pos = GetDouble(e, "pos");
                if (pos < 0)
                    continue;   // part 外的残留点丢弃
                vc.Events.Add(new VocControllerEvent(pos, VprCodec.ToCanonical(name, GetInt(e, "value"))));
            }
        return vc;
    }

    /// <summary>包络 events 的峰值（vibrato depths/rates 取代表值）。</summary>
    static double MaxEventValue(JsonElement parent, string key)
    {
        if (!parent.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return 0;
        double max = 0;
        foreach (var e in arr.EnumerateArray())
        {
            var v = GetDouble(e, "value");
            if (v > max)
                max = v;
        }
        return max;
    }

    // ── JSON 读取小工具 ──

    static string GetString(JsonElement e, string key)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    static double GetDouble(JsonElement e, string key, double def = 0)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : def;

    static int GetInt(JsonElement e, string key, int def = 0)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? (int)Math.Round(v.GetDouble())
            : def;

    static bool GetBool(JsonElement e, string key)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v)
           && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
           && v.GetBoolean();

    /// <summary>读 volume/panpot 这类 {events:[{pos,value}]} 包络的首个值（pos=0 的基线）。</summary>
    static double GetVolume(JsonElement parent, string key)
    {
        if (!parent.TryGetProperty(key, out var env) || env.ValueKind != JsonValueKind.Object)
            return 0;
        if (!env.TryGetProperty("events", out var ev) || ev.ValueKind != JsonValueKind.Array)
            return 0;
        foreach (var e in ev.EnumerateArray())
            return GetDouble(e, "value");   // 首个事件即基线
        return 0;
    }
}
