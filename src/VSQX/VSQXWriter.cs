using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using VocaloidFormatSupport.Common;

namespace VocaloidFormatSupport.VSQX;

// VSQX 写出器：按设置写 VOCALOID3(vsq3) 或 VOCALOID4(vsq4)；CompID 归一化，VOCALOID 独有参数写默认值。
internal static class VSQXWriter
{
    const string StylePluginId = "ACA9C502-A04B-42b5-B2EB-5CEA36D16FCE";
    const string StylePluginName = "VOCALOID2 Compatible Style";
    const string StylePluginVersion = "3.0.0.1";
    const string AuxId = "AUX_VST_HOST_CHUNK_INFO";
    const string AuxContent = "VlNDSwAAAAADAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    /// <summary>带默认命名空间的元素工厂（E/C/I/V/A 一律注入该 ns）。</summary>
    sealed class Ns
    {
        readonly XNamespace mNs;

        public Ns(XNamespace ns) => mNs = ns;

        /// <summary>普通元素。</summary>
        public XElement E(string name, params object?[] content) => new(mNs + name, content);

        /// <summary>CDATA 文本元素。</summary>
        public XElement C(string name, string? text) => new(mNs + name, XmlExt.CData(text));

        /// <summary>整数文本元素。</summary>
        public XElement I(string name, int value) => new(mNs + name, value.ToString(CultureInfo.InvariantCulture));

        /// <summary>VSQX4 的 &lt;v id="…"&gt; 值元素。</summary>
        public XElement V(string id, int value) => new(mNs + "v", new XAttribute("id", id), value.ToString(CultureInfo.InvariantCulture));

        /// <summary>VSQX3 的 &lt;attr id="…"&gt; 值元素。</summary>
        public XElement A(string id, int value) => new(mNs + "attr", new XAttribute("id", id), value.ToString(CultureInfo.InvariantCulture));
    }

    public static void Write(Stream output, VocProject voc, bool v4)
        => XmlExt.WriteXml(output, v4 ? BuildV4(voc) : BuildV3(voc));

    static List<string> DistinctCompIds(VocProject voc)
    {
        var list = new List<string>();
        foreach (var tr in voc.Tracks)
            foreach (var pt in tr.Parts)
            {
                var id = DaisiumParams.NormalizeCompId(pt.CompId);
                if (!list.Contains(id))
                    list.Add(id);
            }
        if (list.Count == 0)
            list.Add(DaisiumParams.DefaultCompId);
        return list;
    }

    static int BsOf(List<string> compIds, string compId)
    {
        var i = compIds.IndexOf(DaisiumParams.NormalizeCompId(compId));
        return i < 0 ? 0 : i;
    }

    // ═════════════════════════════ VOCALOID4 ═════════════════════════════

    static XDocument BuildV4(VocProject voc)
    {
        XNamespace ns = "http://www.yamaha.co.jp/vocaloid/schema/vsq4/";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var x = new Ns(ns);

        var compIds = DistinctCompIds(voc);

        var root = x.E("vsq4",
            new XAttribute(XNamespace.Xmlns + "xsi", xsi.NamespaceName),
            new XAttribute(xsi + "schemaLocation", "http://www.yamaha.co.jp/vocaloid/schema/vsq4/ vsq4.xsd"),
            x.C("vender", "Yamaha corporation"),
            x.C("version", "4.0.0.3"),
            x.E("vVoiceTable",
                compIds.Select(cid => x.E("vVoice",
                    x.I("bs", compIds.IndexOf(cid)),
                    x.I("pc", 0),
                    x.C("id", cid),
                    x.C("name", cid),
                    x.E("vPrm", x.I("bre", 0), x.I("bri", 0), x.I("cle", 0), x.I("gen", 0), x.I("ope", 0))))),
            x.E("mixer",
                x.E("masterUnit", x.I("oDev", 0), x.I("rLvl", 0), x.I("vol", 0)),
                voc.Tracks.Select((t, i) => x.E("vsUnit",
                    x.I("tNo", i),
                    x.I("iGin", 0),
                    x.I("sLvl", -898),
                    x.I("sEnable", 0),
                    x.I("m", t.Mute ? 1 : 0),
                    x.I("s", t.Solo ? 1 : 0),
                    x.I("pan", (int)Math.Round(Math.Clamp(t.Pan, -1, 1) * 64) + 64),
                    x.I("vol", (int)Math.Round(t.GainDb * 10)))),
                x.E("monoUnit", x.I("iGin", 0), x.I("sLvl", -898), x.I("sEnable", 0), x.I("m", 0), x.I("s", 0), x.I("pan", 64), x.I("vol", 0)),
                x.E("stUnit", x.I("iGin", 0), x.I("m", 0), x.I("s", 0), x.I("vol", -129))),
            x.E("masterTrack",
                x.C("seqName", "Untitled"),
                x.C("comment", string.Empty),
                x.I("resolution", 480),
                x.I("preMeasure", 4),
                voc.TimeSignatures.Select(ts => x.E("timeSig",
                    x.I("m", ts.BarIndex), x.I("nu", ts.Numerator), x.I("de", ts.Denominator))),
                voc.Tempos.Select(t => x.E("tempo",
                    x.I("t", (int)Math.Round(t.Pos)), x.I("v", (int)Math.Round(t.Bpm * 100))))),
            voc.Tracks.Select((t, i) => x.E("vsTrack",
                x.I("tNo", i),
                x.C("name", string.IsNullOrEmpty(t.Name) ? "Track" : t.Name),
                x.C("comment", string.Empty),
                t.Parts.Select(p => BuildV4Part(x, p, compIds)))),
            x.E("monoTrack"),
            x.E("stTrack"),
            x.E("aux", x.C("id", AuxId), x.C("content", AuxContent)));

        return new XDocument(root);
    }

    static XElement BuildV4Part(Ns x, VocPart p, List<string> compIds)
    {
        var content = new List<object?>
        {
            x.I("t", (int)Math.Round(p.Pos)),
            x.I("playTime", (int)Math.Round(p.Duration)),
            x.C("name", string.IsNullOrEmpty(p.Name) ? "Part" : p.Name),
            x.C("comment", string.Empty),
            x.E("sPlug", x.C("id", StylePluginId), x.C("name", StylePluginName), x.C("version", StylePluginVersion)),
            x.E("pStyle",
                x.V("accent", VocNoteDefaults.Accent),
                x.V("bendDep", VocNoteDefaults.BendDepth),
                x.V("bendLen", VocNoteDefaults.BendLength),
                x.V("decay", VocNoteDefaults.Decay),
                x.V("fallPort", VocNoteDefaults.FallPort),
                x.V("opening", VocNoteDefaults.Opening),
                x.V("risePort", VocNoteDefaults.RisePort)),
            x.E("singer", x.I("t", 0), x.I("bs", BsOf(compIds, p.CompId)), x.I("pc", 0)),
        };

        // 控制器：映射轨 + VOCALOID 独有（POR）默认常量。
        foreach (var vc in p.Controllers)
        {
            var spec = AutomationMap.FindByVocName(vc.Name);
            if (spec != null)
            {
                foreach (var e in vc.Events)
                    content.Add(x.E("cc", x.I("t", (int)Math.Round(e.Pos)), x.V(spec.VSQXCcId, e.Value)));
                continue;
            }
            foreach (var (name, _, ccId, def) in AutomationMap.DiscardedPartControllers)
            {
                if (name != vc.Name || ccId.Length == 0)
                    continue;
                foreach (var e in vc.Events)
                    content.Add(x.E("cc", x.I("t", (int)Math.Round(e.Pos)), x.V(ccId, def)));
            }
        }

        foreach (var n in p.Notes)
            content.Add(BuildV4Note(x, n));

        content.Add(x.I("plane", 0));
        return x.E("vsPart", content.ToArray());
    }

    static XElement BuildV4Note(Ns x, VocNote n)
    {
        var style = new List<object?>
        {
            x.V("accent", VocNoteDefaults.Accent),
            x.V("bendDep", VocNoteDefaults.BendDepth),
            x.V("bendLen", VocNoteDefaults.BendLength),
            x.V("decay", VocNoteDefaults.Decay),
            x.V("fallPort", VocNoteDefaults.FallPort),
            x.V("opening", n.Opening),
            x.V("risePort", VocNoteDefaults.RisePort),
        };

        if (n.Vibrato is { Dur: > 0 } vib)
        {
            style.Add(x.V("vibLen", (int)Math.Round(Math.Clamp(vib.Dur / Math.Max(1, n.Dur), 0, 1) * 100)));
            style.Add(x.V("vibType", vib.Type));
            style.Add(x.E("seq", new XAttribute("id", "vibDep"),
                x.E("cc", x.E("p", "0"), x.E("v", ((int)Math.Round(vib.Depth)).ToString(CultureInfo.InvariantCulture)))));
            style.Add(x.E("seq", new XAttribute("id", "vibRate"),
                x.E("cc", x.E("p", "0"), x.E("v", ((int)Math.Round(vib.Rate)).ToString(CultureInfo.InvariantCulture)))));
        }
        else
        {
            style.Add(x.V("vibLen", 0));
            style.Add(x.V("vibType", 0));
        }

        var p = n.PhonemeLocked
            ? x.E("p", new XAttribute("lock", "1"), XmlExt.CData(n.Phoneme))
            : x.E("p", XmlExt.CData(n.Phoneme));

        return x.E("note",
            x.I("t", (int)Math.Round(n.Pos)),
            x.I("dur", (int)Math.Round(n.Dur)),
            x.I("n", n.Number),
            x.I("v", DaisiumParams.VocVelocityDefault),
            x.C("y", n.Lyric),
            p,
            x.E("nStyle", style.ToArray()));
    }

    // ═════════════════════════════ VOCALOID3 ═════════════════════════════

    static XDocument BuildV3(VocProject voc)
    {
        XNamespace ns = "http://www.yamaha.co.jp/vocaloid/schema/vsq3/";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var x = new Ns(ns);

        var compIds = DistinctCompIds(voc);

        var root = x.E("vsq3",
            new XAttribute(XNamespace.Xmlns + "xsi", xsi.NamespaceName),
            new XAttribute(xsi + "schemaLocation", "http://www.yamaha.co.jp/vocaloid/schema/vsq3/ vsq3.xsd"),
            x.C("vender", "Yamaha corporation"),
            x.C("version", "3.0.0.11"),
            x.E("vVoiceTable",
                compIds.Select(cid => x.E("vVoice",
                    x.I("vBS", compIds.IndexOf(cid)),
                    x.I("vPC", 0),
                    x.C("compID", cid),
                    x.C("vVoiceName", cid),
                    x.E("vVoiceParam", x.I("bre", 0), x.I("bri", 0), x.I("cle", 0), x.I("gen", 0), x.I("ope", 0))))),
            x.E("mixer",
                x.E("masterUnit", x.I("outDev", 0), x.I("retLevel", 0), x.I("vol", 0)),
                voc.Tracks.Select((t, i) => x.E("vsUnit",
                    x.I("vsTrackNo", i),
                    x.I("inGain", 0),
                    x.I("sendLevel", -898),
                    x.I("sendEnable", 0),
                    x.I("mute", t.Mute ? 1 : 0),
                    x.I("solo", t.Solo ? 1 : 0),
                    x.I("pan", (int)Math.Round(Math.Clamp(t.Pan, -1, 1) * 64) + 64),
                    x.I("vol", (int)Math.Round(t.GainDb * 10)))),
                x.E("seUnit", x.I("inGain", 0), x.I("sendLevel", -898), x.I("sendEnable", 0), x.I("mute", 0), x.I("solo", 0), x.I("pan", 64), x.I("vol", 0)),
                x.E("karaokeUnit", x.I("inGain", 0), x.I("mute", 0), x.I("solo", 0), x.I("vol", -129))),
            x.E("masterTrack",
                x.C("seqName", "Untitled"),
                x.C("comment", string.Empty),
                x.I("resolution", 480),
                x.I("preMeasure", 4),
                voc.TimeSignatures.Select(ts => x.E("timeSig",
                    x.I("posMes", ts.BarIndex), x.I("nume", ts.Numerator), x.I("denomi", ts.Denominator))),
                voc.Tempos.Select(t => x.E("tempo",
                    x.I("posTick", (int)Math.Round(t.Pos)), x.I("bpm", (int)Math.Round(t.Bpm * 100))))),
            voc.Tracks.Select((t, i) => x.E("vsTrack",
                x.I("vsTrackNo", i),
                x.C("trackName", string.IsNullOrEmpty(t.Name) ? "Track" : t.Name),
                x.C("comment", string.Empty),
                t.Parts.Select(p => BuildV3Part(x, p, compIds)))),
            x.E("seTrack"),
            x.E("karaokeTrack"),
            x.E("aux", x.C("auxID", AuxId), x.C("content", AuxContent)));

        return new XDocument(root);
    }

    static XElement BuildV3Part(Ns x, VocPart p, List<string> compIds)
    {
        var content = new List<object?>
        {
            x.I("posTick", (int)Math.Round(p.Pos)),
            x.I("playTime", (int)Math.Round(p.Duration)),
            x.C("partName", string.IsNullOrEmpty(p.Name) ? "Part" : p.Name),
            x.C("comment", string.Empty),
            x.E("stylePlugin", x.C("stylePluginID", StylePluginId), x.C("stylePluginName", StylePluginName), x.C("version", StylePluginVersion)),
            x.E("partStyle",
                x.A("accent", VocNoteDefaults.Accent),
                x.A("bendDep", VocNoteDefaults.BendDepth),
                x.A("bendLen", VocNoteDefaults.BendLength),
                x.A("decay", VocNoteDefaults.Decay),
                x.A("fallPort", VocNoteDefaults.FallPort),
                x.A("opening", VocNoteDefaults.Opening),
                x.A("risePort", VocNoteDefaults.RisePort)),
            x.E("singer", x.I("posTick", 0), x.I("vBS", BsOf(compIds, p.CompId)), x.I("vPC", 0)),
        };

        foreach (var vc in p.Controllers)
        {
            var spec = AutomationMap.FindByVocName(vc.Name);
            if (spec != null)
            {
                if (spec.VSQX3Id.Length == 0)
                    continue;   // VSQX3 无此控制器（如 GWL/XSY）⇒ 不发射
                foreach (var e in vc.Events)
                    content.Add(x.E("mCtrl", x.I("posTick", (int)Math.Round(e.Pos)), x.A(spec.VSQX3Id, e.Value)));
                continue;
            }
            foreach (var (name, v3Id, _, def) in AutomationMap.DiscardedPartControllers)
            {
                if (name != vc.Name || v3Id.Length == 0)
                    continue;
                foreach (var e in vc.Events)
                    content.Add(x.E("mCtrl", x.I("posTick", (int)Math.Round(e.Pos)), x.A(v3Id, def)));
            }
        }

        foreach (var n in p.Notes)
            content.Add(BuildV3Note(x, n));

        return x.E("musicalPart", content.ToArray());
    }

    static XElement BuildV3Note(Ns x, VocNote n)
    {
        var style = new List<object?>
        {
            x.A("accent", VocNoteDefaults.Accent),
            x.A("bendDep", VocNoteDefaults.BendDepth),
            x.A("bendLen", VocNoteDefaults.BendLength),
            x.A("decay", VocNoteDefaults.Decay),
            x.A("fallPort", VocNoteDefaults.FallPort),
            x.A("opening", n.Opening),
            x.A("risePort", VocNoteDefaults.RisePort),
        };

        if (n.Vibrato is { Dur: > 0 } vib)
        {
            style.Add(x.A("vibLen", (int)Math.Round(Math.Clamp(vib.Dur / Math.Max(1, n.Dur), 0, 1) * 100)));
            style.Add(x.A("vibType", vib.Type));
            style.Add(x.E("seqAttr", new XAttribute("id", "vibDep"),
                x.E("elem", x.E("posNrm", "0"), x.E("elv", ((int)Math.Round(vib.Depth)).ToString(CultureInfo.InvariantCulture)))));
            style.Add(x.E("seqAttr", new XAttribute("id", "vibRate"),
                x.E("elem", x.E("posNrm", "0"), x.E("elv", ((int)Math.Round(vib.Rate)).ToString(CultureInfo.InvariantCulture)))));
        }
        else
        {
            style.Add(x.A("vibLen", 0));
            style.Add(x.A("vibType", 0));
        }

        var ph = n.PhonemeLocked
            ? x.E("phnms", new XAttribute("lock", "1"), XmlExt.CData(n.Phoneme))
            : x.E("phnms", XmlExt.CData(n.Phoneme));

        return x.E("note",
            x.I("posTick", (int)Math.Round(n.Pos)),
            x.I("durTick", (int)Math.Round(n.Dur)),
            x.I("noteNum", n.Number),
            x.I("velocity", DaisiumParams.VocVelocityDefault),
            x.C("lyric", n.Lyric),
            ph,
            x.E("noteStyle", style.ToArray()));
    }
}
