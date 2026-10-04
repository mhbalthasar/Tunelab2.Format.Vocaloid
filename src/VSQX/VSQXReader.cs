using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using VocaloidFormatSupport.Common;

namespace VocaloidFormatSupport.VSQX;

// VSQX 读取器：vsq3/vsq4 一套代码通吃（按 LocalName 匹配，忽略命名空间与两代字段名差异）。
internal static class VSQXReader
{
    public static VocProject Read(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidDataException("VSQX: empty document");
        var v4 = root.Name.LocalName == "vsq4";

        var voc = new VocProject();

        // ── 声库表：(bs,pc) → CompID ──
        var voiceByBspc = new Dictionary<(int Bs, int Pc), string>();
        foreach (var v in root.Elem("vVoiceTable").Elems("vVoice"))
        {
            var bs = v4 ? v.Int("bs") : v.Int("vBS");
            var pc = v4 ? v.Int("pc") : v.Int("vPC");
            var cid = v4 ? v.Str("id") : v.Str("compID");
            if (cid.Length > 0)
                voiceByBspc[(bs, pc)] = cid;
        }

        // ── 调音台：轨号 → (Gain dB, Pan, Mute, Solo) ──
        var mix = new Dictionary<int, (double GainDb, double Pan, bool Mute, bool Solo)>();
        foreach (var u in root.Elem("mixer").Elems("vsUnit"))
        {
            var tNo = v4 ? u.Int("tNo") : u.Int("vsTrackNo");
            var gainDb = u.Dbl("vol") / 10.0;              // VOCALOID 音量单位 = 0.1 dB
            var pan = (u.Dbl("pan", 64) - 64.0) / 64.0;    // 0..127（64 居中）→ -1..1
            var mute = (v4 ? u.Int("m") : u.Int("mute")) != 0;
            var solo = (v4 ? u.Int("s") : u.Int("solo")) != 0;
            mix[tNo] = (gainDb, Math.Clamp(pan, -1, 1), mute, solo);
        }

        // ── 主轨：拍号 / 速度 ──
        var mt = root.Elem("masterTrack");
        foreach (var ts in mt.Elems("timeSig"))
        {
            voc.TimeSignatures.Add(new VocTimeSignature
            {
                BarIndex = v4 ? ts.Int("m") : ts.Int("posMes"),
                Numerator = v4 ? ts.Int("nu", 4) : ts.Int("nume", 4),
                Denominator = v4 ? ts.Int("de", 4) : ts.Int("denomi", 4),
            });
        }
        foreach (var t in mt.Elems("tempo"))
        {
            voc.Tempos.Add(new VocTempo
            {
                Pos = v4 ? t.Dbl("t") : t.Dbl("posTick"),
                Bpm = (v4 ? t.Dbl("v") : t.Dbl("bpm")) / 100.0,
            });
        }

        // ── 轨 ──
        foreach (var tr in root.Elems("vsTrack"))
        {
            var tNo = v4 ? tr.Int("tNo") : tr.Int("vsTrackNo");
            var vt = new VocTrack
            {
                Name = v4 ? tr.Str("name") : tr.Str("trackName"),
            };
            if (mix.TryGetValue(tNo, out var m))
            {
                vt.GainDb = m.GainDb;
                vt.Pan = m.Pan;
                vt.Mute = m.Mute;
                vt.Solo = m.Solo;
            }
            foreach (var pt in tr.Elems(v4 ? "vsPart" : "musicalPart"))
                vt.Parts.Add(ReadPart(pt, v4, voiceByBspc));
            voc.Tracks.Add(vt);
        }

        return voc;
    }

    static VocPart ReadPart(XElement pt, bool v4, Dictionary<(int, int), string> voiceByBspc)
    {
        var vp = new VocPart
        {
            Pos = v4 ? pt.Dbl("t") : pt.Dbl("posTick"),
            Duration = pt.Dbl("playTime"),
            Name = v4 ? pt.Str("name") : pt.Str("partName"),
        };

        var singer = pt.Elem("singer");
        if (singer != null)
        {
            var bs = v4 ? singer.Int("bs") : singer.Int("vBS");
            var pc = v4 ? singer.Int("pc") : singer.Int("vPC");
            if (voiceByBspc.TryGetValue((bs, pc), out var cid))
                vp.CompId = cid;
        }

        if (v4)
        {
            foreach (var cc in pt.Elems("cc"))
            {
                var id = cc.Elem("v")?.Attribute("id")?.Value;
                AddController(vp, AutomationMap.FindByCcId(id), cc.Dbl("t"), (int)Math.Round(cc.Dbl("v")));
            }
        }
        else
        {
            foreach (var mc in pt.Elems("mCtrl"))
            {
                var pos = mc.Dbl("posTick");
                foreach (var attr in mc.Elems("attr"))
                {
                    var id = attr.Attribute("id")?.Value;
                    AddController(vp, AutomationMap.FindByV3Id(id), pos, ParseInt(attr.Value));
                }
            }
        }

        foreach (var n in pt.Elems("note"))
            vp.Notes.Add(ReadNote(n, v4));

        return vp;
    }

    static void AddController(VocPart vp, AutomationSpec? spec, double pos, int value)
    {
        if (spec == null)
            return;   // 未映射控制器 ⇒ 丢弃
        var name = spec.VocNames[0];
        var vc = vp.Controllers.FirstOrDefault(c => c.Name == name);
        if (vc == null)
        {
            vc = new VocController { Name = name };
            vp.Controllers.Add(vc);
        }
        vc.Events.Add(new VocControllerEvent(pos, value));
    }

    static VocNote ReadNote(XElement n, bool v4)
    {
        var vn = new VocNote();
        XElement? style;
        if (v4)
        {
            vn.Pos = n.Dbl("t");
            vn.Dur = n.Dbl("dur");
            vn.Number = n.Int("n", 60);
            vn.Velocity = n.Int("v", DaisiumParams.VocVelocityDefault);
            vn.Lyric = n.Str("y");
            var p = n.Elem("p");
            if (p != null)
            {
                vn.Phoneme = p.Value;
                vn.PhonemeLocked = p.Attribute("lock")?.Value == "1";
            }
            style = n.Elem("nStyle");
        }
        else
        {
            vn.Pos = n.Dbl("posTick");
            vn.Dur = n.Dbl("durTick");
            vn.Number = n.Int("noteNum", 60);
            vn.Velocity = n.Int("velocity", DaisiumParams.VocVelocityDefault);
            vn.Lyric = n.Str("lyric");
            var ph = n.Elem("phnms");
            if (ph != null)
            {
                vn.Phoneme = ph.Value;
                vn.PhonemeLocked = ph.Attribute("lock")?.Value == "1";
            }
            style = n.Elem("noteStyle");
        }

        ReadStyle(style, v4, vn);
        return vn;
    }

    static void ReadStyle(XElement? style, bool v4, VocNote vn)
    {
        if (style == null)
            return;

        var opening = DaisiumParams.VocOpeningDefault;
        var vibLen = 0;
        var vibType = 0;
        double? depth = null;
        double? rate = null;

        if (v4)
        {
            foreach (var v in style.Elems("v"))
            {
                switch (v.Attribute("id")?.Value)
                {
                    case "opening": opening = ParseInt(v.Value); break;
                    case "vibLen": vibLen = ParseInt(v.Value); break;
                    case "vibType": vibType = ParseInt(v.Value); break;
                }
            }
            depth = MaxEnvelope(style, "seq", "vibDep", "v");
            rate = MaxEnvelope(style, "seq", "vibRate", "v");
        }
        else
        {
            foreach (var a in style.Elems("attr"))
            {
                switch (a.Attribute("id")?.Value)
                {
                    case "opening": opening = ParseInt(a.Value); break;
                    case "vibLen": vibLen = ParseInt(a.Value); break;
                    case "vibType": vibType = ParseInt(a.Value); break;
                }
            }
            depth = MaxEnvelope(style, "seqAttr", "vibDep", "elv");
            rate = MaxEnvelope(style, "seqAttr", "vibRate", "elv");
        }

        vn.Opening = Math.Clamp(opening, 0, 127);

        if (vibLen > 0)
        {
            // vibLen 是**音符时长的百分比**（0..100，实测全库上限恰为 100）⇒ 换算为 tick。
            vn.Vibrato = new VocVibrato
            {
                Type = vibType,
                Dur = Math.Round(vibLen / 100.0 * vn.Dur),
                Depth = depth ?? 64,
                Rate = rate ?? 50,
            };
        }
    }

    // 取包络峰值：seq/seqAttr（按 id 定位）内所有点值的最大值。
    static double? MaxEnvelope(XElement style, string seqName, string seqId, string pointName)
    {
        var seq = style.Elems(seqName).FirstOrDefault(e => e.Attribute("id")?.Value == seqId);
        if (seq == null)
            return null;
        double? max = null;
        foreach (var e in seq.Descendants())
        {
            if (e.Name.LocalName != pointName)
                continue;
            var v = ParseDouble(e.Value);
            if (max == null || v > max)
                max = v;
        }
        return max;
    }

    static int ParseInt(string? s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    static double ParseDouble(string? s)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
