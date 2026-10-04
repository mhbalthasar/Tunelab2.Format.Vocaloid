using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using VocaloidFormatSupport.Common;

namespace VocaloidFormatSupport.VSQ;

// VSQ（VOCALOID2）导入：SMF → 每轨内嵌 INI → VocProject；只读 INI，每 voice 轨 = 一个 VocTrack + VocPart(Pos=0)。
internal static class VSQReader
{
    public static VocProject Read(byte[] bytes)
    {
        var midi = VSQMidi.Parse(bytes);
        var proj = new VocProject();

        if (midi.Tracks.Count > 0)
            ReadConductor(midi.Tracks[0], proj);

        if (proj.Tempos.Count == 0)
            proj.Tempos.Add(new VocTempo { Pos = 0, Bpm = 120 });
        if (proj.TimeSignatures.Count == 0)
            proj.TimeSignatures.Add(new VocTimeSignature { BarIndex = 0, Numerator = 4, Denominator = 4 });

        // 逐 voice 轨解析 INI。
        var parsed = new List<(string TrackName, List<IniSection> Ini)>();
        for (int i = 1; i < midi.Tracks.Count; i++)
        {
            var ini = ParseIniFromTrack(midi.Tracks[i]);
            if (ini == null)
                continue;
            // 没有 [EventList] / [ID#] 的轨不是 voice 轨。
            if (VSQIni.Find(ini, "EventList") == null && !HasAnyIdSection(ini))
                continue;
            parsed.Add((TrackName(midi.Tracks[i]), ini));
        }

        // [Mixer] 只出现在第一轨的 INI 里（实测），但为稳妥起见取首个含 [Mixer] 的 INI。
        IniSection? mixer = null;
        foreach (var (_, ini) in parsed)
        {
            mixer = VSQIni.Find(ini, "Mixer");
            if (mixer != null)
                break;
        }

        for (int j = 0; j < parsed.Count; j++)
        {
            var (smfName, ini) = parsed[j];
            var common = VSQIni.Find(ini, "Common");
            var name = common?.Get("Name");
            if (string.IsNullOrEmpty(name))
                name = smfName;

            var track = new VocTrack { Name = name ?? string.Empty };
            if (mixer != null)
                ApplyMixer(mixer, j, track);
            track.Parts.Add(ReadPart(ini, track.Name));
            proj.Tracks.Add(track);
        }

        return proj;
    }

    // ───────────────────────── conductor ─────────────────────────

    static void ReadConductor(MidiTrack track, VocProject proj)
    {
        var sigs = new List<(double Tick, int Num, int Denom)>();
        foreach (var e in track.Events)
        {
            if (!e.IsMeta)
                continue;
            if (e.MetaType == 0x51 && e.Data.Length >= 3)
            {
                int micros = (e.Data[0] << 16) | (e.Data[1] << 8) | e.Data[2];
                if (micros > 0)
                    proj.Tempos.Add(new VocTempo { Pos = e.Tick, Bpm = 60000000.0 / micros });
            }
            else if (e.MetaType == 0x58 && e.Data.Length >= 2)
            {
                int num = e.Data[0];
                int denom = 1 << e.Data[1];
                if (num > 0 && denom > 0)
                    sigs.Add((e.Tick, num, denom));
            }
        }

        // tick → 小节号（自 0 起，按前一段拍号的拍长累加）。
        sigs.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        double prevTick = 0;
        int prevNum = 4, prevDenom = 4, bar = 0;
        foreach (var (tick, num, denom) in sigs)
        {
            if (tick > prevTick)
            {
                double barLen = prevNum * (VSQCodec.Ppq * 4.0) / prevDenom;
                if (barLen > 0)
                    bar += (int)Math.Floor((tick - prevTick) / barLen);
            }
            proj.TimeSignatures.Add(new VocTimeSignature { BarIndex = bar, Numerator = num, Denominator = denom });
            prevTick = tick; prevNum = num; prevDenom = denom;
        }
    }

    // ───────────────────────── INI 组装 ─────────────────────────

    static bool HasAnyIdSection(List<IniSection> ini)
    {
        foreach (var s in ini)
            if (s.Name.StartsWith("ID#", StringComparison.Ordinal))
                return true;
        return false;
    }

    static string TrackName(MidiTrack track)
    {
        foreach (var e in track.Events)
            if (e.IsMeta && e.MetaType == 0x03)
                return VSQText.Decode(e.Data);
        return string.Empty;
    }

    /// <summary>把一轨里所有 "DM:NNNN:..." 文本 meta 按序号拼接、Shift-JIS 解码、解析成 INI 小节。</summary>
    static List<IniSection>? ParseIniFromTrack(MidiTrack track)
    {
        var chunks = new SortedDictionary<int, byte[]>();
        foreach (var e in track.Events)
        {
            if (!e.IsMeta || e.MetaType != 0x01)
                continue;
            var d = e.Data;
            if (d.Length < 4 || d[0] != (byte)'D' || d[1] != (byte)'M' || d[2] != (byte)':')
                continue;

            int i = 3, idx = 0;
            bool any = false;
            while (i < d.Length && d[i] >= (byte)'0' && d[i] <= (byte)'9')
            {
                idx = idx * 10 + (d[i] - (byte)'0');
                i++;
                any = true;
            }
            if (!any || i >= d.Length || d[i] != (byte)':')
                continue;
            i++;

            var body = new byte[d.Length - i];
            Array.Copy(d, i, body, 0, body.Length);
            chunks[idx] = body;
        }

        if (chunks.Count == 0)
            return null;

        using var ms = new MemoryStream();
        foreach (var kv in chunks)
            ms.Write(kv.Value, 0, kv.Value.Length);

        return VSQIni.Parse(VSQText.Decode(ms.ToArray()));
    }

    // ───────────────────────── [Mixer] ─────────────────────────

    static void ApplyMixer(IniSection mixer, int index, VocTrack track)
    {
        var feder = ParseInt(mixer.Get("Feder" + index), 0);
        var panpot = ParseInt(mixer.Get("Panpot" + index), 0);
        track.GainDb = feder / 10.0;
        track.Pan = Math.Clamp(panpot / (double)VSQCodec.PanpotScale, -1, 1);
        track.Mute = ParseInt(mixer.Get("Mute" + index), 0) != 0;
        track.Solo = ParseInt(mixer.Get("Solo" + index), 0) != 0;
    }

    // ───────────────────────── part ─────────────────────────

    static VocPart ReadPart(List<IniSection> ini, string name)
    {
        var part = new VocPart
        {
            Name = name,
            Pos = 0,
            CompId = DaisiumParams.DefaultCompId,   // VSQ 无 CompID ⇒ 用兜底
        };

        var idMap = IndexSections(ini, "ID#");
        var hMap = IndexSections(ini, "h#");

        // [EventList]：tick → ID#XXXX（逗号分隔），末尾 tick=EOS。
        double eos = 0;
        var list = VSQIni.Find(ini, "EventList");
        if (list != null)
        {
            foreach (var kv in list.Fields)
            {
                if (!double.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tick))
                    continue;
                if (string.Equals(kv.Value, "EOS", StringComparison.OrdinalIgnoreCase))
                {
                    eos = tick;
                    continue;
                }
                foreach (var raw in kv.Value.Split(','))
                {
                    var id = raw.Trim();
                    if (id.Length == 0)
                        continue;
                    if (!idMap.TryGetValue(id, out var sec))
                        continue;
                    var type = sec.Get("Type");
                    if (!string.Equals(type, "Anote", StringComparison.OrdinalIgnoreCase))
                        continue;
                    part.Notes.Add(ReadNote(sec, hMap, tick));
                }
            }
        }

        // *BPList → 控制器（Daisium 有对应者；Reso*/EpR* 等无对应 ⇒ 丢弃）。
        foreach (var sec in ini)
        {
            if (!sec.Name.EndsWith("BPList", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(sec.Name, VSQCodec.OpeningSection, StringComparison.OrdinalIgnoreCase))
                continue;
            var ctrl = VSQCodec.ControllerForSection(sec.Name);
            if (ctrl == null)
                continue;
            var vc = new VocController { Name = ctrl };
            foreach (var kv in sec.Fields)
                if (double.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t)
                    && int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    vc.Events.Add(new VocControllerEvent(t, v));
            if (vc.Events.Count > 0)
                part.Controllers.Add(vc);
        }

        // [OpeningBPList] → 按音符起点采样 → note.Opening（→ Mouth）。
        var opening = VSQIni.Find(ini, VSQCodec.OpeningSection);
        if (opening != null)
        {
            var pts = ParsePoints(opening);
            if (pts.Count > 0)
                foreach (var n in part.Notes)
                    n.Opening = SampleAt(pts, n.Pos, VSQCodec.OpeningDefault);
        }

        double maxEnd = 0;
        foreach (var n in part.Notes)
            maxEnd = Math.Max(maxEnd, n.Pos + n.Dur);
        part.Duration = Math.Max(eos, maxEnd);

        return part;
    }

    static VocNote ReadNote(IniSection sec, Dictionary<string, IniSection> hMap, double tick)
    {
        var note = new VocNote
        {
            Pos = tick,
            Dur = ParseInt(sec.Get("Length"), 0),
            Number = ParseInt(sec.Get("Note#"), 60),
            Velocity = ParseInt(sec.Get("Dynamics"), DaisiumParams.VocVelocityDefault),
        };

        var lyricHandle = sec.Get("LyricHandle");
        if (lyricHandle != null && hMap.TryGetValue(lyricHandle, out var lsec))
            ApplyLyric(lsec, note);

        var vibHandle = sec.Get("VibratoHandle");
        if (vibHandle != null && hMap.TryGetValue(vibHandle, out var vsec))
            ApplyVibrato(vsec, note, ParseInt(sec.Get("VibratoDelay"), -1));

        return note;
    }

    static void ApplyLyric(IniSection sec, VocNote note)
    {
        var l0 = sec.Get("L0");
        if (string.IsNullOrEmpty(l0))
            return;

        var toks = SplitQuoted(l0);
        if (toks.Count >= 1)
            note.Lyric = toks[0];
        if (toks.Count >= 2)
            note.Phoneme = toks[1];
        // 末字段 = protect：非 0 表示音素被钉死（⇒ 存 Lyric+Phoneme）。
        if (toks.Count >= 4)
            note.PhonemeLocked = ParseInt(toks[^1], 0) != 0;
    }

    static void ApplyVibrato(IniSection sec, VocNote note, int delay)
    {
        double dur = delay >= 0 ? note.Dur - delay : ParseInt(sec.Get("Length"), 0);
        dur = Math.Clamp(dur, 0, note.Dur);
        if (dur <= 0)
            return;

        double depth = ParseInt(sec.Get("StartDepth"), 64);
        double rate = ParseInt(sec.Get("StartRate"), 64);
        if (ParseInt(sec.Get("DepthBPNum"), 0) > 0)
            depth = MaxOfList(sec.Get("DepthBPY"), depth);
        if (ParseInt(sec.Get("RateBPNum"), 0) > 0)
            rate = MaxOfList(sec.Get("RateBPY"), rate);

        note.Vibrato = new VocVibrato
        {
            Type = ParseVibratoType(sec.Get("IconID")),
            Dur = dur,
            Depth = depth,
            Rate = rate,
        };
    }

    /// <summary>从 IconID="$040400TT" 取颤音预设类型 TT（十六进制）。</summary>
    static int ParseVibratoType(string? iconId)
    {
        if (string.IsNullOrEmpty(iconId))
            return 0;
        var s = iconId.Trim();
        if (s.StartsWith("$", StringComparison.Ordinal))
            s = s.Substring(1);
        if (s.Length < 2)
            return 0;
        return int.TryParse(s.Substring(s.Length - 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    static double MaxOfList(string? csv, double fallback)
    {
        if (string.IsNullOrEmpty(csv))
            return fallback;
        double max = fallback;
        foreach (var raw in csv.Split(','))
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > max)
                max = v;
        return max;
    }

    // ───────────────────────── 工具 ─────────────────────────

    static Dictionary<string, IniSection> IndexSections(List<IniSection> ini, string prefix)
    {
        var d = new Dictionary<string, IniSection>(StringComparer.Ordinal);
        foreach (var s in ini)
            if (s.Name.StartsWith(prefix, StringComparison.Ordinal))
                d[s.Name] = s;
        return d;
    }

    static List<(double X, int Y)> ParsePoints(IniSection sec)
    {
        var pts = new List<(double, int)>();
        foreach (var kv in sec.Fields)
            if (double.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
                && int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
                pts.Add((x, y));
        pts.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return pts;
    }

    static int SampleAt(List<(double X, int Y)> pts, double x, int fallback)
    {
        if (pts.Count == 0)
            return fallback;
        int val = pts[0].Item2;
        foreach (var (px, py) in pts)
        {
            if (px > x)
                break;
            val = py;
        }
        return val;
    }

    /// <summary>按逗号切分，但把 "..." 视为单个 token（去掉引号）。用于 L0。</summary>
    static List<string> SplitQuoted(string s)
    {
        var list = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '"')
            {
                int j = s.IndexOf('"', i + 1);
                if (j < 0)
                    j = s.Length;
                list.Add(s.Substring(i + 1, j - i - 1));
                i = j + 1;
                if (i < s.Length && s[i] == ',')
                    i++;
            }
            else
            {
                int j = s.IndexOf(',', i);
                if (j < 0)
                    j = s.Length;
                list.Add(s.Substring(i, j - i));
                i = j + 1;
            }
        }
        return list;
    }

    static int ParseInt(string? s, int fallback)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
