using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace VocaloidFormatSupport.Common;

// VocProject ⇄ TuneLab ProjectInfo 的唯一映射真相源（用户确认：引擎身份=Type/Id、控制器同名直映+域换算、音高=PIT/PBS 轨 + V6 directPitches→PitchInfo、opening→Mouth、vibrato→VibratoInfo）。
public static class VocMapper
{
    // VOCALOID 控制曲线是零阶保持（阶梯），TuneLab 是 Hermite 插值且无 step 模式 ⇒ 导入插保持点阶梯化、导出按签名合拢；ε 必须为非整数（0.5 tick）以免误删真控制点。
    const double HoldEpsilonTicks = 0.5;

    // ───────────────────────── VOCALOID → TuneLab ─────────────────────────

    public static ProjectInfo ToTuneLab(VocProject voc)
    {
        var project = new ProjectInfo();

        foreach (var t in voc.Tempos)
            project.Tempos.Add(new TempoInfo { Pos = t.Pos, Bpm = t.Bpm });
        if (project.Tempos.Count == 0)
            project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });

        foreach (var ts in voc.TimeSignatures)
            project.TimeSignatures.Add(new TimeSignatureInfo { BarIndex = ts.BarIndex, Numerator = ts.Numerator, Denominator = ts.Denominator });
        if (project.TimeSignatures.Count == 0)
            project.TimeSignatures.Add(new TimeSignatureInfo { BarIndex = 0, Numerator = 4, Denominator = 4 });

        foreach (var vt in voc.Tracks)
        {
            var track = new TrackInfo
            {
                Name = vt.Name,
                Gain = vt.GainDb,
                Pan = Math.Clamp(vt.Pan, -1, 1),
                Mute = vt.Mute,
                Solo = vt.Solo,
            };
            foreach (var vp in vt.Parts)
                track.Parts.Add(ToTuneLabPart(vp));
            project.Tracks.Add(track);
        }

        return project;
    }

    static MidiPartInfo ToTuneLabPart(VocPart vp)
    {
        var part = new MidiPartInfo
        {
            Name = vp.Name,
            Pos = vp.Pos,
            StartOffset = 0,
            SoundSource = new SoundSourceInfo
            {
                Kind = SourceKind.Voice,
                Type = DaisiumParams.EngineType,
                Id = vp.CompId,
            },
        };

        double maxEnd = 0;
        foreach (var vn in vp.Notes)
        {
            part.Notes.Add(ToTuneLabNote(vn));
            maxEnd = Math.Max(maxEnd, vn.Pos + vn.Dur);

            if (vn.Vibrato is { Dur: > 0 } vib)
            {
                var vibPos = vn.Pos + vn.Dur - vib.Dur;
                if (vibPos < vn.Pos)
                    vibPos = vn.Pos;
                part.Vibratos.Add(new VibratoInfo
                {
                    Pos = vibPos,
                    Dur = vib.Dur,
                    Frequency = VibratoMap.RateToHz(vib.Rate),
                    Phase = 0,
                    Amplitude = VibratoMap.DepthToSemitones(vib.Depth),
                    Attack = 0,
                    Release = 0,
                });
            }
        }

        // V6 绝对音高线 → PitchInfo.Segments 单条曲线（绝对 tick = note.Pos + dp.Pos，重叠由后写覆盖）。
        var dpNotes = vp.Notes.Where(n => n.DirectPitches.Count > 0).OrderBy(n => n.Pos).ToList();
        if (dpNotes.Count > 0)
        {
            var merged = new List<(double Tick, double Value)>();
            foreach (var vn in dpNotes)
            {
                double lo = vn.Pos + vn.DirectPitches.Min(p => p.Pos);
                double hi = vn.Pos + vn.DirectPitches.Max(p => p.Pos);
                merged.RemoveAll(q => q.Tick >= lo && q.Tick <= hi);
                foreach (var p in vn.DirectPitches)
                    merged.Add((vn.Pos + p.Pos, p.Number));
            }
            merged.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            part.Pitch.Segments.Add(merged.Select(q => new Point(q.Tick, q.Value)).ToList());
        }

        part.EndOffset = Math.Max(vp.Duration, maxEnd);

        // 控制器 → 自动化轨（同名直映 + 域换算；多个控制器可能落同一轨 ⇒ 按轨聚合；未映射者丢弃）。
        Dictionary<string, List<Point>>? byTrack = null;
        foreach (var vc in vp.Controllers)
        {
            var spec = AutomationMap.FindByVocName(vc.Name);
            if (spec == null)
                continue;   // 丢弃
            byTrack ??= new Dictionary<string, List<Point>>(StringComparer.Ordinal);
            if (!byTrack.TryGetValue(spec.Key, out var pts))
            {
                pts = new List<Point>();
                byTrack[spec.Key] = pts;
            }
            foreach (var e in vc.Events)
                pts.Add(new Point(e.Pos, spec.ToDaisium(e.Value)));
        }

        if (byTrack != null)
        {
            foreach (var (key, pts) in byTrack)
            {
                var spec = AutomationMap.FindByKey(key)!;
                pts.Sort((a, b) => a.X.CompareTo(b.X));
                var auto = new AutomationInfo { DefaultValue = spec.Default };
                auto.Points.AddRange(StepifyZeroOrderHold(pts));
                part.Automations[key] = auto;
            }
        }

        return part;
    }

    static NoteInfo ToTuneLabNote(VocNote vn)
    {
        var note = new NoteInfo
        {
            Pos = vn.Pos,
            Dur = vn.Dur,
            Pitch = Math.Clamp(vn.Number, 0, 127),
            Lyric = vn.Lyric,
        };

        var props = new Map<string, PropertyValue>();
        if (vn.PhonemeLocked && !string.IsNullOrEmpty(vn.Phoneme))
            props[DaisiumParams.KeyPhoneme] = PropertyValue.Create(vn.Phoneme);
        if (vn.Opening != DaisiumParams.VocOpeningDefault)
            props[DaisiumParams.KeyMouth] = PropertyValue.Create(Math.Clamp(vn.Opening / 127.0, 0, 1));
        if (props.Count > 0)
            note.Properties = new PropertyObject(props);

        return note;
    }

    // ───────────────────────── TuneLab → VOCALOID ─────────────────────────

    public static VocProject FromTuneLab(ProjectInfo project)
    {
        var voc = new VocProject();

        foreach (var t in project.Tempos)
            voc.Tempos.Add(new VocTempo { Pos = t.Pos, Bpm = t.Bpm });
        if (voc.Tempos.Count == 0)
            voc.Tempos.Add(new VocTempo { Pos = 0, Bpm = 120 });

        foreach (var ts in project.TimeSignatures)
            voc.TimeSignatures.Add(new VocTimeSignature { BarIndex = ts.BarIndex, Numerator = ts.Numerator, Denominator = ts.Denominator });
        if (voc.TimeSignatures.Count == 0)
            voc.TimeSignatures.Add(new VocTimeSignature { BarIndex = 0, Numerator = 4, Denominator = 4 });

        foreach (var track in project.Tracks)
        {
            var vt = new VocTrack
            {
                Name = track.Name,
                GainDb = track.Gain,
                Pan = Math.Clamp(track.Pan, -1, 1),
                Mute = track.Mute,
                Solo = track.Solo,
            };
            foreach (var part in track.Parts)
            {
                if (part is MidiPartInfo midi)
                    vt.Parts.Add(FromTuneLabPart(midi));
            }
            voc.Tracks.Add(vt);
        }

        return voc;
    }

    static VocPart FromTuneLabPart(MidiPartInfo part)
    {
        var vp = new VocPart
        {
            Name = part.Name,
            Pos = part.Pos,
            Duration = Math.Max(0, part.EndOffset - part.StartOffset),
            CompId = DaisiumParams.NormalizeCompId(part.SoundSource.Id),
        };

        foreach (var note in part.Notes)
            vp.Notes.Add(FromTuneLabNote(note));

        // PitchInfo.Segments → 各 note 的 directPitches（绝对→note 相对 tick；优先归属包含它的 note，否则最近的）。
        if (part.Pitch is { Segments.Count: > 0 } pitch)
        {
            foreach (var seg in pitch.Segments)
            {
                foreach (var pt in seg)
                {
                    var host = FindNoteAt(vp.Notes, pt.X) ?? FindNearestNote(vp.Notes, pt.X);
                    host?.DirectPitches.Add(new VocPitchPoint(pt.X - host.Pos, pt.Y));
                }
            }
            foreach (var vn in vp.Notes)
                vn.DirectPitches.Sort((a, b) => a.Pos.CompareTo(b.Pos));
        }

        // 自动化轨 → 控制器（同名直映 + 域换算；Daisium 独有轨丢弃）。
        foreach (var kvp in part.Automations)
        {
            var spec = AutomationMap.FindByKey(kvp.Key);
            if (spec == null)
                continue;   // Daisium 独有轨（VOI/共振峰…）VOCALOID 无对应 ⇒ 丢弃

            var info = kvp.Value;
            var vc = new VocController { Name = spec.VocNames[0] };

            var pts = CollapseZeroOrderHold(info.Points);

            // 首点前若存在基线（DefaultValue），补一个 X=0 锚点以保真。
            if (pts.Count == 0 || pts[0].X > 0)
                vc.Events.Add(new VocControllerEvent(0, spec.ToVoc(info.DefaultValue)));
            foreach (var p in pts)
                vc.Events.Add(new VocControllerEvent(p.X, spec.ToVoc(p.Y)));

            vp.Controllers.Add(vc);
        }

        // VOCALOID 独有 part 控制器：写默认常量（用户约定）。格式支持性由 Writer 决定（空 id ⇒ 不发射）。
        foreach (var (name, _, _, def) in AutomationMap.DiscardedPartControllers)
        {
            var vc = new VocController { Name = name };
            vc.Events.Add(new VocControllerEvent(0, def));
            vp.Controllers.Add(vc);
        }

        // VibratoInfo → 归属包含其起点的 note。
        foreach (var vib in part.Vibratos)
        {
            if (vib.Dur <= 0)
                continue;
            VocNote? host = null;
            foreach (var vn in vp.Notes)
            {
                if (vib.Pos >= vn.Pos && vib.Pos < vn.Pos + vn.Dur)
                {
                    host = vn;
                    break;
                }
            }
            host ??= vp.Notes.Count > 0 ? vp.Notes[^1] : null;
            if (host == null)
                continue;
            host.Vibrato = new VocVibrato
            {
                Type = 0,
                Dur = vib.Dur,
                Depth = VibratoMap.SemitonesToDepth(vib.Amplitude),
                Rate = VibratoMap.HzToRate(vib.Frequency),
            };
        }

        return vp;
    }

    static VocNote FromTuneLabNote(NoteInfo note)
    {
        var vn = new VocNote
        {
            Pos = note.Pos,
            Dur = note.Dur,
            Number = Math.Clamp(note.Pitch, 0, 127),
            Velocity = DaisiumParams.VocVelocityDefault,
            Lyric = note.Lyric,
        };

        var phoneme = note.Properties.GetString(DaisiumParams.KeyPhoneme, string.Empty).Trim();
        if (phoneme.Length > 0)
        {
            vn.Phoneme = phoneme;
            vn.PhonemeLocked = true;
        }

        var mouth = note.Properties.GetDouble(DaisiumParams.KeyMouth, DaisiumParams.MouthDefault);
        vn.Opening = (int)Math.Round(Math.Clamp(mouth, 0, 1) * 127);

        return vn;
    }

    /// <summary>找到包含给定 part 相对 tick 的 note（半开区间 [Pos, Pos+Dur)）。</summary>
    static VocNote? FindNoteAt(List<VocNote> notes, double pos)
    {
        foreach (var n in notes)
            if (pos >= n.Pos && pos < n.Pos + n.Dur)
                return n;
        return null;
    }

    /// <summary>找到离给定 tick 最近的 note（含落在所有 note 之外时，用于保住 portamento 余量）。</summary>
    static VocNote? FindNearestNote(List<VocNote> notes, double pos)
    {
        VocNote? best = null;
        double bestDist = double.MaxValue;
        foreach (var n in notes)
        {
            double d = pos < n.Pos ? n.Pos - pos : (pos > n.Pos + n.Dur ? pos - (n.Pos + n.Dur) : 0);
            if (d < bestDist)
            {
                bestDist = d;
                best = n;
            }
        }
        return best;
    }

    // ───────────────────────── 零阶保持 ↔ Hermite 同步 ─────────────────────────

    /// <summary>零阶保持点列 → TuneLab 阶梯：每个相邻点对间插保持点 (next.X−ε, cur.Y)；间距≤ε 或等值的对跳过。</summary>
    static List<Point> StepifyZeroOrderHold(List<Point> pts)
    {
        if (pts.Count < 2)
            return pts;
        var outp = new List<Point>(pts.Count * 2);
        for (int i = 0; i < pts.Count; i++)
        {
            outp.Add(pts[i]);
            if (i + 1 < pts.Count
                && pts[i + 1].X - pts[i].X > HoldEpsilonTicks
                && pts[i + 1].Y != pts[i].Y)
                outp.Add(new Point(pts[i + 1].X - HoldEpsilonTicks, pts[i].Y));
        }
        return outp;
    }

    /// <summary>StepifyZeroOrderHold 的逆：合拢保持点还原原始控制点列；签名 = 距后点≈ε 且值同前点且后点值不同。</summary>
    static List<Point> CollapseZeroOrderHold(List<Point> pts)
    {
        int n = pts.Count;
        if (n < 3)
            return pts;
        var keep = new List<Point>(n);
        for (int i = 0; i < n; i++)
        {
            bool isHold = i >= 1 && i + 1 < n
                && Math.Abs((pts[i + 1].X - pts[i].X) - HoldEpsilonTicks) < 1e-6
                && pts[i].Y == pts[i - 1].Y
                && pts[i + 1].Y != pts[i].Y;
            if (!isHold)
                keep.Add(pts[i]);
        }
        return keep;
    }
}
