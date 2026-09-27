# 变更日志

所有「风声未止」的变更记录在此。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [Unreleased]

## [M2~M5] - 2026-09-27

### 新增

- 🖼 **WPF 4 页 + 国风 UI**（NavigationView v3 / 宣纸+朱砂+古铜金色 / Microsoft YaHei）
- ⌨ **目标窗口监测** —— FindWindow('Delta Force'/'三角洲行动') + GetForegroundWindow
- 🔥 **全局热键 F8/F9/F10** —— RegisterHotKey（演奏/暂停/急停）
- 🎹 **SendInput P/Invoke 引擎** —— 原生 user32.dll 鼠标 + 键盘
- 🎵 **4 首内置 MIDI**（自动生成到 %AppData%）
- 📂 **用户导入 MIDI**（OpenFileDialog + 复制到 user library）
- 🌐 **在线曲库 BitMidi.com** —— 15000+ 流行/动漫/影视/古典，搜索 + 翻页 + 下载
- 🤖 **AI 知音** —— Anthropic Claude / OpenAI / OpenAI 兼容（含 OpenRouter / DeepSeek / Ollama / 中转）
- ⚙ **Settings 持久化** —— %AppData%/WhisperWind/settings.json（API Key 只存本地）
- 📦 **自包含发布 workflow** —— `git tag v*` 自动出 Release zip

### CI

- 3 job 拆分：Core 测试 (Linux) / NuGet 漏洞扫描 / WPF 编译 (Windows)
- 6 个 workflow：build / codeql / release-drafter / dependabot / publish
- 全部 3 job 绿色 + CodeQL 绿色 + Release Drafter 自动出 note

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
