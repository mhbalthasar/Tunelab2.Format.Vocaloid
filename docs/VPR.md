# VPR（VOCALOID 5 / 6）

容器 = **ZIP**，唯一必需成员 `Project/sequence.json`（实测恒 STORED）。

> ⚠️ 部分真实文件（如 `LNGB.vpr`）的 ZIP 中央目录条目名用**反斜杠** `Project\sequence.json`；`ZipArchive.GetEntry` 不归一化 ⇒ 必须容错查找（遍历 `Entries`，把 `\` 归一为 `/` 比较）。

## 版本

| | V5 | V6 |
| --- | --- | --- |
| version | 5.0.0 | 6.5.1 |
| `masterTrack.mainTuning` | 无 | 有（440.0） |
| `masterTrack.tempo.ara` | 无 | 有 |
| note `directPitches` | 无 | 有 |
| note `aiExp` | 无 | 有（12 维，默认 0.5） |
| note `isAiVibratoEnabled` / `langID` | 无 | 有 |
| note `exp` | 仅 `opening` | 完整（`accent` / `decay` / `bendDepth` / `bendLength` / `opening`） |
| `singingSkill.duration` | 158 | 160 |
| 轨 `lastScrollPositionNoteNumber` | 无 | 有 |
| 控制器 `xSynth`（= V6 版 xsy） | 无 | 有 |

## directPitches（V6 音高曲线）

- 结构：`[{pos, value}, …]`，`pos` = **相对该 note 的 tick**，末项恒为字符串哨兵 `"ZeroPitch"`。
- 值编码（源自 `VSMScore.GetRawNoteNumberFromPitch`）：`noteNumber = (value + 6900) / 100`；反算 `value = noteNumber * 100 - 6900`。
- ⚠️ 真实文件里各 note 的曲线会**相互重叠、且可能超出该 note 自身范围**。TuneLab 的 `PitchInfo.Segments` 要求段互不重叠且有序，故：
  - **导入**：把所有 note 的 `directPitches` 换算到绝对 tick 后**合并为单条曲线**；重叠区间由**更靠后的 note 整体覆盖**。
  - **导出**：绝对曲线按 tick 归属音符（包含优先，落在所有音符外则归属最近音符以保住 portamento 余量），再转回 note 相对 tick。
- **V5 无此字段 ⇒ 导出为 V5 时音高曲线不被保留。**

## 映射约定

- 有符号控制器（`character` / `exciter`）：`canonical = raw + 64`。
- 音量：`volume` 单位 = 0.1 dB；`panpot` ∈ [-64, 64] → [-1, 1]。
- part 外事件（`pos < 0`）丢弃。
- 只处理含 `notes` 的 vocal part；wav / region part 跳过。
- 版本由导出设置选择；`voices` 由工程 CompID 去重生成。

## 常量

`Ppq=480`、`SamplingRate=44100`、`SignedOffset=64`、`MainTuning=440.0`。
