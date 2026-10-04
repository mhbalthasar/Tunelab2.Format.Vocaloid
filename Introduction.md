# VOCALOID Format Support

为 TuneLab 提供 VOCALOID 工程格式的**导入**与**导出**，并把数据映射到 DaisiumForTuneLab 的参数体系。
纯 C# 实现，无原生依赖（AnyCPU）。

## 支持的格式

| 后缀 | 名称 | 方向 |
| --- | --- | --- |
| `.vsqx` | VOCALOID 3 / 4 工程 | 导入 + 导出（导出可选 vsq3 / vsq4） |
| `.vsq` | VOCALOID 2 工程 | 导入 + 导出 |
| `.vpr` | VOCALOID 5 / 6 工程 | 导入 + 导出（导出可选 V5 / V6） |

导入时按文件内容自动判别版本（VSQX 看根元素 `vsq3`/`vsq4`；VPR 看 ZIP 内的 `version`）；导出时由扩展设置选择目标版本。

## 映射概要

- **音符 / 速度 / 拍号 / 轨道**：同名直通（时基同为 tick，PPQ 480）。
- **控制器**：同名直映并做值域换算（如 character / exciter 的有符号偏移、音量 0.1 dB、pan 0..127 → -1..1）；Daisium 有对应者保留，VOCALOID 独有且无对应者按格式能力丢弃或写中性默认。
- **音高**：V6 VPR 的 `directPitches` 换算坐标后合并为**单条**绝对音高曲线写入 `PitchInfo.Segments`；导出时再按音符范围切回 `directPitches`。
- **口开度**：VPR `exp.opening` / VSQX `opening` → 音符属性 Mouth。
- **颤音**：深度/速率包络取代表值 → `VibratoInfo`。

## 已知限制

- **VSQ**：singer 恒写默认 Miku；BRI / BRE / CLE / GWL / XSY 等 VSQ 无对应，导出丢弃；口开度恒写常量。
- **VPR V5**：无 `directPitches` 字段，导出为 V5 时音高曲线不被保留（V6 才有）。
- **V6 VPR 音高曲线**：真实文件里各音符的 `directPitches` 会相互重叠、且可能超出音符自身范围；导入时按「重叠区间由更靠后的音符整体覆盖」合并为一条曲线。
