using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using VocaloidFormatSupport.Common;

namespace VocaloidFormatSupport.VSQ;

// VSQ（VOCALOID2）导出：VocProject → SMF（每 voice 轨内嵌 INI，按用户约定只写 INI）；singer 恒写默认 Miku，opening 恒写常量，VSQ 无对应的控制器/样式丢弃或写中性默认。
internal static class VSQWriter
{
    public static void Write(Stream output, VocProject proj)
    {
        var midi = new MidiFile { Format = 1, Division = VSQCodec.Ppq };
        midi.Tracks.Add(BuildConductor(proj));

        for (int i = 0; i < proj.Tracks.Count; i++)
            midi.Tracks.Add(BuildVoice(proj, i));

        VSQMidi.Write(output, midi);
    }

    // ───────────────────────── conductor（track0）─────────────────────────

    static MidiTrack BuildConductor(VocProject proj)
    {
        var track = new MidiTrack();
        track.Events.Add(Meta(0x03, 0, VSQText.Encode("Master Track")));

        long last = 0;
        foreach (var t in proj.Tempos)
        {
            int micros = (int)Math.Round(60000000.0 / Math.Max(1e-6, t.Bpm));
            if (micros < 1) micros = 1;
            if (micros > 0xFFFFFF) micros = 0xFFFFFF;
            var data = new[] { (byte)((micros >> 16) & 0xFF), (byte)((micros >> 8) & 0xFF), (byte)(micros & 0xFF) };
            long tick = (long)Math.Round(t.Pos);
            track.Events.Add(Meta(0x51, tick, data));
            last = Math.Max(last, tick);
        }

        // 小节号 → tick（按前一段拍号累加）。
        double cur = 0;
        int prevBar = 0, prevNum = 4, prevDenom = 4;
        foreach (var ts in proj.TimeSignatures)
        {
            double barLen = prevNum * (VSQCodec.Ppq * 4.0) / prevDenom;
            cur += (ts.BarIndex - prevBar) * barLen;
            long tick = (long)Math.Round(cur);
            int denomPow = 0, d = Math.Max(1, ts.Denominator);
            while (d > 1 && denomPow < 24) { d >>= 1; denomPow++; }
            var data = new byte[] { (byte)ts.Numerator, (byte)denomPow, 24, 8 };
            track.Events.Add(Meta(0x58, tick, data));
            last = Math.Max(last, tick);
            prevBar = ts.BarIndex; prevNum = ts.Numerator; prevDenom = ts.Denominator;
        }

        track.Events.Add(Meta(0x2F, last, Array.Empty<byte>()));
        return track;
    }

    // ───────────────────────── voice（track1..N）─────────────────────────

    static MidiTrack BuildVoice(VocProject proj, int index)
    {
        var vtrack = proj.Tracks[index];
        var track = new MidiTrack();

        var name = string.IsNullOrEmpty(vtrack.Name) ? $"Voice{index + 1}" : vtrack.Name;
        track.Events.Add(Meta(0x03, 0, VSQText.Encode(name)));

        // 汇总该轨所有 part 的音符 / 控制器（part 相对 → 绝对 tick）。
        var notes = new List<(double Tick, VocNote Note)>();
        foreach (var part in vtrack.Parts)
            foreach (var n in part.Notes)
                notes.Add((part.Pos + n.Pos, n));
        notes.Sort((a, b) => a.Tick.CompareTo(b.Tick));

        var ctrlPoints = new Dictionary<string, List<(double Tick, int Value)>>(StringComparer.Ordinal);
        foreach (var part in vtrack.Parts)
            foreach (var c in part.Controllers)
            {
                if (!ctrlPoints.TryGetValue(c.Name, out var pts))
                {
                    pts = new List<(double, int)>();
                    ctrlPoints[c.Name] = pts;
                }
                foreach (var e in c.Events)
                    pts.Add((part.Pos + e.Pos, e.Value));
            }

        var ini = BuildIni(proj, index, name, notes, ctrlPoints);
        var body = VSQText.Encode(VSQIni.Build(ini));

        foreach (var payload in ChunkDm(body))
            track.Events.Add(Meta(0x01, 0, payload));

        track.Events.Add(Meta(0x2F, 0, Array.Empty<byte>()));
        return track;
    }

    static List<IniSection> BuildIni(
        VocProject proj, int index, string name,
        List<(double Tick, VocNote Note)> notes,
        Dictionary<string, List<(double Tick, int Value)>> ctrlPoints)
    {
        var sections = new List<IniSection>();

        // [Common]
        var common = new IniSection("Common");
        common.Add("Version", VSQCodec.FormatVersion);
        common.Add("Name", name);
        common.Add("Color", VSQCodec.DefaultColor);
        common.Add("DynamicsMode", "1");
        common.Add("PlayMode", "1");
        sections.Add(common);

        // [Master]
        var master = new IniSection("Master");
        master.Add("PreMeasure", VSQCodec.PreMeasure.ToString(CultureInfo.InvariantCulture));
        sections.Add(master);

        // [Mixer]（全局；每个 voice 轨都写一份，保持自洽）
        var mixer = new IniSection("Mixer");
        mixer.Add("MasterFeder", "0");
        mixer.Add("MasterPanpot", "0");
        mixer.Add("MasterMute", "0");
        mixer.Add("OutputMode", "0");
        mixer.Add("Tracks", proj.Tracks.Count.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < proj.Tracks.Count; i++)
        {
            var t = proj.Tracks[i];
            mixer.Add("Feder" + i, ((int)Math.Round(t.GainDb * 10)).ToString(CultureInfo.InvariantCulture));
            mixer.Add("Panpot" + i, ((int)Math.Round(Math.Clamp(t.Pan, -1, 1) * VSQCodec.PanpotScale)).ToString(CultureInfo.InvariantCulture));
            mixer.Add("Mute" + i, t.Mute ? "1" : "0");
            mixer.Add("Solo" + i, t.Solo ? "1" : "0");
        }
        sections.Add(mixer);

        // [EventList]：Singer(ID#0000) + 各 note（ID#0001..），末尾 EOS。
        double eos = 0;
        var events = new List<(long Tick, string Id)>();
        events.Add((0, "ID#0000"));
        for (int i = 0; i < notes.Count; i++)
        {
            long tick = (long)Math.Round(notes[i].Tick);
            events.Add((tick, $"ID#{i + 1:D4}"));
            eos = Math.Max(eos, tick + Math.Max(0, notes[i].Note.Dur));
        }
        events.Sort((a, b) => a.Tick.CompareTo(b.Tick));

        var eventList = new IniSection("EventList");
        int ei = 0;
        while (ei < events.Count)
        {
            long tick = events[ei].Tick;
            var ids = new StringBuilder();
            while (ei < events.Count && events[ei].Tick == tick)
            {
                if (ids.Length > 0)
                    ids.Append(',');
                ids.Append(events[ei].Id);
                ei++;
            }
            eventList.Add(tick.ToString(CultureInfo.InvariantCulture), ids.ToString());
        }
        eventList.Add(((long)Math.Round(eos)).ToString(CultureInfo.InvariantCulture), "EOS");
        sections.Add(eventList);

        // [ID#] + [h#]
        var idSections = new List<IniSection>();
        var hSections = new List<IniSection>();

        idSections.Add(SingerIdSection());
        hSections.Add(SingerHandleSection());

        int h = 1;
        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i].Note;
            int lyricHandle = h++;
            int vibHandle = -1;

            var id = new IniSection($"ID#{i + 1:D4}");
            id.Add("Type", "Anote");
            id.Add("Length", ((long)Math.Round(note.Dur)).ToString(CultureInfo.InvariantCulture));
            id.Add("Note#", note.Number.ToString(CultureInfo.InvariantCulture));
            id.Add("Dynamics", note.Velocity.ToString(CultureInfo.InvariantCulture));
            id.Add("PMBendDepth", VSQCodec.NotePmBendDepth.ToString(CultureInfo.InvariantCulture));
            id.Add("PMBendLength", VSQCodec.NotePmBendLength.ToString(CultureInfo.InvariantCulture));
            id.Add("PMbPortamentoUse", VSQCodec.NotePmPortamentoUse.ToString(CultureInfo.InvariantCulture));
            id.Add("DEMdecGainRate", VSQCodec.NoteDecGainRate.ToString(CultureInfo.InvariantCulture));
            id.Add("DEMaccent", VSQCodec.NoteAccent.ToString(CultureInfo.InvariantCulture));
            id.Add("LyricHandle", $"h#{lyricHandle:D4}");

            if (note.Vibrato is { Dur: > 0 } vib)
            {
                vibHandle = h++;
                long delay = (long)Math.Round(Math.Clamp(note.Dur - vib.Dur, 0, note.Dur));
                id.Add("VibratoHandle", $"h#{vibHandle:D4}");
                id.Add("VibratoDelay", delay.ToString(CultureInfo.InvariantCulture));
            }
            idSections.Add(id);

            hSections.Add(LyricHandleSection(lyricHandle, note));
            if (vibHandle >= 0)
                hSections.Add(VibratoHandleSection(vibHandle, note, note.Vibrato!));
        }

        sections.AddRange(idSections);
        sections.AddRange(hSections);

        // *BPList（Daisium 有对应者）。
        var bySection = new Dictionary<string, List<(double Tick, int Value)>>(StringComparer.Ordinal);
        foreach (var (ctrlName, pts) in ctrlPoints)
        {
            var section = VSQCodec.SectionForController(ctrlName);
            if (section == null)
                continue;                       // BRI/BRE/CLE/GWL/XSY/… VSQ 无对应 ⇒ 丢弃
            if (!bySection.TryGetValue(section, out var dst))
            {
                dst = new List<(double, int)>();
                bySection[section] = dst;
            }
            dst.AddRange(pts);
        }

        // 固定顺序输出，贴近真实文件。
        string[] order = ["PitchBendBPList", "PitchBendSensBPList", "DynamicsBPList", "GenderFactorBPList", "PortamentoTimingBPList"];
        foreach (var section in order)
        {
            if (!bySection.TryGetValue(section, out var pts) || pts.Count == 0)
                continue;
            pts.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            var sec = new IniSection(section);
            foreach (var (tick, value) in pts)
            {
                int v = value;
                if (section == "PitchBendSensBPList")
                    v = Math.Clamp(v, VSQCodec.PitchBendSensMin, VSQCodec.PitchBendSensMax);
                sec.Add(((long)Math.Round(tick)).ToString(CultureInfo.InvariantCulture), v.ToString(CultureInfo.InvariantCulture));
            }
            sections.Add(sec);
        }

        // [OpeningBPList]：恒写常量（用户约定）。
        var opening = new IniSection(VSQCodec.OpeningSection);
        opening.Add("0", VSQCodec.OpeningDefault.ToString(CultureInfo.InvariantCulture));
        sections.Add(opening);

        return sections;
    }

    static IniSection SingerIdSection()
    {
        var s = new IniSection("ID#0000");
        s.Add("Type", "Singer");
        s.Add("IconHandle", "h#0000");
        return s;
    }

    static IniSection SingerHandleSection()
    {
        var s = new IniSection("h#0000");
        s.Add("IconID", VSQCodec.DefaultSingerIconId);
        s.Add("IDS", VSQCodec.DefaultSingerIds);
        s.Add("Original", VSQCodec.DefaultSingerOriginal.ToString(CultureInfo.InvariantCulture));
        s.Add("Caption", string.Empty);
        s.Add("Length", "1");
        s.Add("Language", VSQCodec.DefaultSingerLanguage.ToString(CultureInfo.InvariantCulture));
        s.Add("Program", VSQCodec.DefaultSingerProgram.ToString(CultureInfo.InvariantCulture));
        return s;
    }

    static IniSection LyricHandleSection(int handle, VocNote note)
    {
        // L0="<lyric>","<phoneme>",<timing>,<每音素长度>,<protect>；未锁定音素写空串，长度列表不建模 ⇒ 全 0。
        var phoneme = note.PhonemeLocked ? note.Phoneme : string.Empty;
        int n = CountTokens(phoneme);
        var sb = new StringBuilder();
        sb.Append('"').Append(Escape(note.Lyric)).Append("\",");
        sb.Append('"').Append(Escape(phoneme)).Append("\",");
        sb.Append(VSQCodec.DefaultPhoneticTiming);
        for (int i = 0; i < n; i++)
            sb.Append(",0");
        sb.Append(',').Append(note.PhonemeLocked ? '1' : '0');

        var s = new IniSection($"h#{handle:D4}");
        s.Add("L0", sb.ToString());
        return s;
    }

    static IniSection VibratoHandleSection(int handle, VocNote note, VocVibrato vib)
    {
        var s = new IniSection($"h#{handle:D4}");
        s.Add("IconID", $"$040400{vib.Type & 0xFF:x2}");
        s.Add("IDS", "normal");
        s.Add("Original", "1");
        s.Add("Caption", string.Empty);
        s.Add("Length", ((long)Math.Round(vib.Dur)).ToString(CultureInfo.InvariantCulture));
        s.Add("StartDepth", ((int)Math.Round(vib.Depth)).ToString(CultureInfo.InvariantCulture));
        s.Add("DepthBPNum", "0");
        s.Add("StartRate", ((int)Math.Round(vib.Rate)).ToString(CultureInfo.InvariantCulture));
        s.Add("RateBPNum", "0");
        return s;
    }

    // ───────────────────────── 工具 ─────────────────────────

    /// <summary>音素串按空白分词计数（与 L0 的长度列表个数一致：每个音素一个长度）。</summary>
    static int CountTokens(string? phoneme)
    {
        if (string.IsNullOrWhiteSpace(phoneme))
            return 1;
        int n = 0;
        bool inTok = false;
        foreach (var c in phoneme)
        {
            if (char.IsWhiteSpace(c))
                inTok = false;
            else if (!inTok)
            {
                inTok = true;
                n++;
            }
        }
        return Math.Max(n, 1);
    }

    static string Escape(string? text) => (text ?? string.Empty).Replace("\"", "'");

    static MidiEvent Meta(byte type, long tick, byte[] data)
        => new() { Tick = tick, Status = 0xFF, MetaType = type, Data = data };

    /// <summary>把 INI 字节流切成 "DM:NNNN:" + ≤119B 的分片载荷（按字节切，读取端按字节拼接）。</summary>
    static List<byte[]> ChunkDm(byte[] body)
    {
        var list = new List<byte[]>();
        int pos = 0, index = 0;
        do
        {
            var prefix = Encoding.ASCII.GetBytes("DM:" + index.ToString("D4", CultureInfo.InvariantCulture) + ":");
            int take = Math.Min(VSQCodec.MaxTextPayload - prefix.Length, body.Length - pos);
            if (take < 0)
                take = 0;
            var payload = new byte[prefix.Length + take];
            Array.Copy(prefix, 0, payload, 0, prefix.Length);
            if (take > 0)
                Array.Copy(body, pos, payload, prefix.Length, take);
            list.Add(payload);
            pos += take;
            index++;
        } while (pos < body.Length);
        return list;
    }
}
