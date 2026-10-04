using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VocaloidFormatSupport.VSQ;

// 极简 SMF（Standard MIDI File）读写（VSQ 专用）：大端头、可变长量、meta 事件；channel 事件（CC 层）只跳过。

internal sealed class MidiEvent
{
    public long Tick;

    /// <summary>0xFF = meta；其余为 channel/sysex 状态字节。</summary>
    public byte Status;

    /// <summary>meta 类型（<see cref="Status"/> == 0xFF 时有效）。</summary>
    public byte MetaType;

    public byte[] Data = Array.Empty<byte>();

    public bool IsMeta => Status == 0xFF;
}

internal sealed class MidiTrack
{
    public List<MidiEvent> Events { get; } = new();
}

internal sealed class MidiFile
{
    public int Format { get; set; } = 1;
    public int Division { get; set; } = VSQCodec.Ppq;
    public List<MidiTrack> Tracks { get; } = new();
}

internal static class VSQMidi
{
    // ───────────────────────── 读 ─────────────────────────

    public static MidiFile Parse(byte[] b)
    {
        if (b.Length < 14 || b[0] != (byte)'M' || b[1] != (byte)'T' || b[2] != (byte)'h' || b[3] != (byte)'d')
            throw new InvalidDataException("VSQ: 不是合法的 SMF 文件（缺少 MThd）。");

        int headerLen = ReadU32(b, 4);
        if (headerLen < 6 || 8 + headerLen > b.Length)
            throw new InvalidDataException("VSQ: MThd 头不完整。");

        var midi = new MidiFile
        {
            Format = ReadU16(b, 8),
            Division = ReadU16(b, 10),
        };
        int trackCount = ReadU16(b, 12);

        int p = 8 + headerLen;
        for (int t = 0; t < trackCount; t++)
        {
            if (p + 8 > b.Length)
                break;
            if (b[p] != (byte)'M' || b[p + 1] != (byte)'T' || b[p + 2] != (byte)'r' || b[p + 3] != (byte)'k')
                break;
            int len = ReadU32(b, p + 4);
            int body = p + 8;
            int end = Math.Min(b.Length, body + len);
            midi.Tracks.Add(ParseTrack(b, body, end));
            p = body + len;
        }

        return midi;
    }

    static MidiTrack ParseTrack(byte[] b, int pos, int end)
    {
        var track = new MidiTrack();
        long tick = 0;
        byte running = 0;

        while (pos < end)
        {
            tick += ReadVar(b, ref pos, end);
            if (pos >= end)
                break;

            byte status = b[pos++];
            if (status < 0x80)
            {
                if (running == 0)
                    break;
                pos--;                 // running status：该字节是数据
                status = running;
            }
            else if (status < 0xF0)
            {
                running = status;
            }

            if (status == 0xFF)
            {
                if (pos >= end)
                    break;
                byte meta = b[pos++];
                int len = (int)ReadVar(b, ref pos, end);
                if (pos + len > end)
                    len = end - pos;
                var data = new byte[len];
                Array.Copy(b, pos, data, 0, len);
                pos += len;
                track.Events.Add(new MidiEvent { Tick = tick, Status = 0xFF, MetaType = meta, Data = data });
            }
            else if (status == 0xF0 || status == 0xF7)
            {
                int len = (int)ReadVar(b, ref pos, end);
                pos += len;
            }
            else
            {
                int hi = status & 0xF0;
                pos += (hi == 0xC0 || hi == 0xD0) ? 1 : 2;
            }
        }

        return track;
    }

    static long ReadVar(byte[] b, ref int pos, int end)
    {
        long v = 0;
        while (pos < end)
        {
            byte c = b[pos++];
            v = (v << 7) | (uint)(c & 0x7F);
            if ((c & 0x80) == 0)
                break;
        }
        return v;
    }

    static int ReadU16(byte[] b, int p) => (b[p] << 8) | b[p + 1];

    static int ReadU32(byte[] b, int p)
        => (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];

    // ───────────────────────── 写 ─────────────────────────

    public static void Write(Stream s, MidiFile midi)
    {
        WriteAscii(s, "MThd");
        WriteU32(s, 6);
        WriteU16(s, midi.Format);
        WriteU16(s, midi.Tracks.Count);
        WriteU16(s, midi.Division);

        foreach (var track in midi.Tracks)
        {
            var body = BuildTrack(track);
            WriteAscii(s, "MTrk");
            WriteU32(s, body.Length);
            s.Write(body, 0, body.Length);
        }
    }

    static byte[] BuildTrack(MidiTrack track)
    {
        // 稳定排序（同 tick 保持插入顺序：trackName → tempo → INI 文本 → EOT）。
        var ordered = track.Events.OrderBy(e => e.Tick).ToList();

        using var ms = new MemoryStream();
        long last = 0;
        foreach (var e in ordered)
        {
            if (e.Tick < last)
                e.Tick = last;              // 防御：delta 不可为负
            WriteVar(ms, e.Tick - last);
            last = e.Tick;

            if (e.IsMeta)
            {
                ms.WriteByte(0xFF);
                ms.WriteByte(e.MetaType);
                WriteVar(ms, e.Data.Length);
                ms.Write(e.Data, 0, e.Data.Length);
            }
            else
            {
                ms.WriteByte(e.Status);
                ms.Write(e.Data, 0, e.Data.Length);
            }
        }
        return ms.ToArray();
    }

    static void WriteVar(Stream s, long value)
    {
        if (value < 0)
            value = 0;
        Span<byte> tmp = stackalloc byte[10];
        int n = 0;
        tmp[n++] = (byte)(value & 0x7F);
        value >>= 7;
        while (value > 0)
        {
            tmp[n++] = (byte)((value & 0x7F) | 0x80);
            value >>= 7;
        }
        for (int i = n - 1; i >= 0; i--)
            s.WriteByte(tmp[i]);
    }

    static void WriteAscii(Stream s, string text)
    {
        foreach (var c in text)
            s.WriteByte((byte)c);
    }

    static void WriteU16(Stream s, int v)
    {
        s.WriteByte((byte)((v >> 8) & 0xFF));
        s.WriteByte((byte)(v & 0xFF));
    }

    static void WriteU32(Stream s, int v)
    {
        s.WriteByte((byte)((v >> 24) & 0xFF));
        s.WriteByte((byte)((v >> 16) & 0xFF));
        s.WriteByte((byte)((v >> 8) & 0xFF));
        s.WriteByte((byte)(v & 0xFF));
    }
}
