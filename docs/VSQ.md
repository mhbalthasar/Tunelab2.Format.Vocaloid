# VSQ（VOCALOID 2）

容器 = **SMF（Standard MIDI File）Format 1**，大端序，division = 480。

## 结构

- **Track 0** = conductor：`0x51` 速度、`0x58` 拍号。
- **Track 1..N** = 每个 voice 轨。轨内以 `0x01`（text）meta 事件承载内嵌的 **VOCALOID INI**，分片格式为 `DM:NNNN:` + ≤119 字节正文；读取端按序号拼接后按 Shift-JIS 解码。
- **音符全部在 INI 里**（无 note-on/off 事件）。

## INI 小节

| 小节 | 内容 |
| --- | --- |
| `[Common]` | `Version`(DSB301) / `Name` / `Color` / `DynamicsMode` / `PlayMode` |
| `[Master]` | `PreMeasure`(4) |
| `[Mixer]` | `MasterFeder` / `MasterPanpot` / `Feder{i}` / `Panpot{i}` / `Mute{i}` / `Solo{i}` |
| `[EventList]` | tick → `ID#XXXX`（逗号分隔），末条 `EOS` |
| `[ID#0000]` | Singer |
| `[ID#0001..]` | 各 note（`Type=Anote`、`Length`、`Note#`、`Dynamics`、`LyricHandle`、`VibratoHandle`…） |
| `[h#0000]` | Singer 句柄（`IDS` / `IconID` / `Language` / `Program`…） |
| `[h#NNNN]` | 歌词句柄 `L0` / 颤音句柄 |
| `*BPList` | 控制器曲线（`PitchBendBPList` / `PitchBendSensBPList` / `DynamicsBPList` / `GenderFactorBPList` / `PortamentoTimingBPList`） |
| `[OpeningBPList]` | 口开度曲线 |

## 映射约定

- **只写 INI**（不写冗余 NRPN CC 层，也不写 note-on/off）。
- **singer 恒写默认 Miku**（不用 CompID）。
- **口开度恒写常量** `[OpeningBPList]`。
- 控制器：`dynamics` / `pitchbend` / `pitchbendsens` / `character` / `portamento` 有对应；**BRI / BRE / CLE / GWL / XSY 无对应 ⇒ 丢弃**。
- 音符级 VOCALOID2 独有样式（`PMBendDepth` / `PMBendLength` / `PMbPortamentoUse` / `DEMdecGainRate` / `DEMaccent`）写中性默认。
- 歌词句柄 `L0` = `"<lyric>","<phoneme>",<timing>,<每音素一个长度>,<protect>`；未锁定的音素按约定写空串。
- `PitchBendSens` 值域裁剪到 1..12。

## 常量

`Ppq=480`、`FormatVersion="DSB301"`、`PreMeasure=4`、`OpeningDefault=127`、`DefaultSingerIds="Miku"`、`PanpotScale=64`。
