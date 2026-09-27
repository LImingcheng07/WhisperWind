# 变更日志

所有「风声未止」的变更记录在此。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### 计划

- M2: WPF 三页骨架 + Fluent 2 主题
- M3: 卷帘窗编辑器 + 3D 全息口琴
- M4: 知音 AI
- M5: 语音控制 + NPC 数字码
- M6: 打包发布 + 4 首内置曲

## [M1] - 2026-09-26

### 新增

- 🍃 Core 纯逻辑库 `WhisperWind.Core`（.NET 8，跨平台）
- 🎵 MIDI 文件解析（基于 Melanchall DryWetMidi 7.2.0）
- 🎹 8 主键 + 2 鼠标修饰的按键映射（默认 C 大调 8 音 + 5 升半 + 高八度 13 音）
- 🔄 MIDI 音轨 → 按键计划（八度折回 / 半音就近 / 冲突检测 / 密距检测 / 极短补足）
- 🧪 30 单元测试（100% 通过）
- 📖 14KB 设计文档 `docs/DESIGN.md`
- 🎨 6 版 HTML 概念图（v1-v6 迭代至「诗与远方」国风最终版）

### 技术决策

- Core 层零 WPF / Win32 依赖，可跨平台编译
- `INotePlayer` 抽象接口，App 层实现 Windows SendInput
- 依赖仅 1 个外部包（DryWetMidi，MIT）
- 命名空间 `WhisperWind.Core`（库） / `WhisperWind.App`（UI）

[Unreleased]: https://github.com/LImingcheng07/WhisperWind/compare/M1...HEAD
[M1]: https://github.com/LImingcheng07/WhisperWind/releases/tag/M1
