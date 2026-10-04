# VSQX（VOCALOID 3 / 4）

纯 XML。导入按根元素 `vsq3` / `vsq4` 自动判别；导出由扩展设置选择版本。

## 版本差异（同一套代码按 LocalName 通吃）

| 概念 | vsq3 | vsq4 |
| --- | --- | --- |
| 声库表项 | `vVoice`（`vBS` / `vPC` / `compID`） | `vVoice`（`bs` / `pc` / `id`） |
| 调音台 | `vsUnit`（`vsTrackNo` / `mute` / `solo` / `pan` / `vol`） | `vsUnit`（`tNo` / `m` / `s` / `pan` / `vol`） |
| 主轨拍号 | `timeSig`（`posMes` / `nume` / `denomi`） | `timeSig`（`m` / `nu` / `de`） |
| 主轨速度 | `tempo`（`posTick` / `bpm`） | `tempo`（`t` / `v`） |
| 轨 | `vsTrack`（`vsTrackNo` / `trackName`） | `vsTrack`（`tNo` / `name`） |
| part | `musicalPart`（`posTick` / `playTime` / `partName`） | `vsPart`（`t` / `playTime` / `name`） |
| 控制器 | `mCtrl` + `<attr id>` | `cc` + `<v id>` |
| 音符 | `note`（`posTick` / `durTick` / `noteNum` / `velocity` / `lyric` / `phnms`） | `note`（`t` / `dur` / `n` / `v` / `y` / `p`） |
| noteStyle | `noteStyle` + `<attr>` + `seqAttr/elem/elv` | `nStyle` + `<v>` + `seq/cc/p/v` |
| part 样式 | `partStyle` / `stylePlugin` | `pStyle` / `sPlug` |
| 命名空间 | `.../vsq3/`，version `3.0.0.11` | `.../vsq4/`，version `4.0.0.3` |

## 映射约定

- **CompID 归一化**：导出时把工程的 CompID 去重成 `vVoiceTable`，part 用 `bs`/`vBS` 索引引用。
- **VOCALOID 独有参数写默认**：`accent` / `bendDep` / `bendLen` / `decay` / `fallPort` / `risePort` / `opening` 等。
- **显式音素**：`p` / `phnms` 元素承载音素串；`lock="1"` 表示钉死。
- **颤音**：`vibLen` 是**音符时长的百分比**（0..100）⇒ 换算为 tick；深度/速率包络取峰值。
- `aux` 固定写 `AUX_VST_HOST_CHUNK_INFO`。

## 常量

`resolution=480`、`preMeasure=4`、StylePlugin `ACA9C502-A04B-42b5-B2EB-5CEA36D16FCE` / `VOCALOID2 Compatible Style` / `3.0.0.1`。
