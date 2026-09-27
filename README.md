# 🍃 风声未止 (WhisperWind)

> 一个开源的、纯 Windows 标准的、零注入的「三角洲行动」自动口琴工具。
> 整合市面同类项目优点，叠加 8 项原创能力：AI 智能编配 / 3D 全息口琴 / 钢琴卷帘窗 / NPC 数字码 / 语音控制 / 曲谱评分 / 多主题 / 智能移调。

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/tests-30%2F30-brightgreen)](tests/)

![v6 国风最终图](preview/concept-v6-icons.png)

> 🍃 命名出处：三角洲行动 NPC 台词「风声未止」。
> 本项目与「Delta Force / 三角洲行动」游戏厂商无任何关联，是独立社区工具。

---

## ✨ 特色

- **🎵 任意 MIDI 演奏**：拖入任意 `.mid` 即可，不依赖特定曲库
- **🛡️ 零注入安全**：仅通过 Windows 标准 `SendInput` API 模拟键鼠，不读内存、不注入 DLL、不修改游戏文件
- **🤖 AI 智能编配**（M4）：Claude/GPT 自动选轨、移调、和声评级
- **🎨 诗与远方国风 UI**（M2）：思源宋体 / 楷体 / 水墨淡彩 / 篆体印章
- **🎤 语音控制**（M5）：本地 Whisper + 唤醒词「小风」
- **🔢 NPC 数字码**：佐拉 7676354 / 老乔 5123543123 / 唐吉 6712171 一键输入
- **📊 曲谱评分**：内置打分系统，越像 NPC 演奏越能解锁完整旋律

---

## 📥 下载

> ✅ **M5 完整版本** — WPF UI / SendInput 引擎 / 在线曲库 (BitMidi) / AI 知音 (Claude + OpenAI)

### Release 下载（推荐）

去 [Releases 页](https://github.com/LImingcheng07/WhisperWind/releases) 下载最新 zip：

- `WhisperWind-v*.*.*-win-x64.zip` —— 自包含单 exe (~150 MB)，解压即用，**无需装 .NET**

### 从源码编译

```bash
git clone https://github.com/LImingcheng07/WhisperWind.git
cd WhisperWind
dotnet test                         # Core 库 30 单元测试
dotnet build src/WhisperWind.App    # WPF App (需 Windows)
```

---

## 🗓️ 进度

| 里程碑 | 内容 | 状态 |
|---|---|---|
| **M0** | 设计稿（HTML 概念图 v1-v6 + 14KB 设计文档） | ✅ 完成 |
| **M1** | Core 纯逻辑层 + 30 单元测试 | ✅ 完成 |
| M2 | WPF 三页骨架 + Fluent 2 主题 | ⏳ |
| M3 | 卷帘窗编辑器 + 3D 全息口琴 | ⏳ |
| M4 | 知音 AI（规则版 + 可选 LLM） | ⏳ |
| M5 | 语音控制 + NPC 数字码 | ⏳ |
| M6 | 打包发布 + 4 首内置曲 | ⏳ |

详细设计：[docs/DESIGN.md](docs/DESIGN.md)

---

## 🏗️ 架构

```
WhisperWind.sln
├── src/WhisperWind.Core/          ← M1 纯逻辑（.NET 8 跨平台）
│   ├── HarmonicaMapping.cs          MIDI 音号 → 游戏按键映射
│   ├── MidiSong.cs                  DryWetMIDI 解析 → 绝对毫秒音符
│   ├── HarmonicaConverter.cs        音轨 → 按键计划（折回/冲突/密距）
│   └── INotePlayer.cs               抽象接口 + Fake 测试替身
├── src/WhisperWind.App/           ← M2 WPF UI（仅 Windows 编译）
└── tests/WhisperWind.Core.Tests/  ← 30 单元测试
```

**关键设计**：
- Core 层零 WPF / Win32 依赖，可跨平台编译（Linux CI 跑测试，Windows 用户编译 UI）
- `INotePlayer` 接口让 Core 与平台实现解耦，未来易移植到 macOS / Linux

---

## 🔑 核心算法

1. **音域折回**：`pitch > MaxPitch (84)` → `pitch -= 12` 循环；`pitch < MinPitch (60)` → `pitch += 12` 循环
2. **半音就近**：在 `safety < 12` 步内 `+1` 找最近映射音
3. **同 ms 多音冲突**：单音口琴 → 记录 `error: "Conflict at {ms}ms"`，跳过冲突音
4. **密距检测**：相邻按键 `< minGapMs`（默认 20ms）→ 记 `error: "Key too close"`
5. **极短音补足**：`< 30ms` 的音拉到 30ms（游戏识别阈值）

---

## ⚖️ 法律声明

> **免责声明**：
> 1. 「风声未止」是独立开源项目，与腾讯 / Morefun Studios / 「三角洲行动」游戏厂商无任何关联
> 2. 「Delta Force」「三角洲行动」是各自所有者的商标
> 3. 本项目仅使用 Windows 标准公开 API（`SendInput` / `RegisterHotKey` / `FindWindow`），不修改游戏文件、不注入 DLL、不读写游戏内存
> 4. 使用本工具可能违反游戏 EULA，**使用风险由用户自行承担**
> 5. 项目维护者不对因使用本工具导致的任何账号封禁负责

---

## 🛡️ 安全设计

- **白盒测试**：`tests/` 下 30 单元测试覆盖核心算法
- **零依赖核心**：Core 层不引任何 SendInput / Win32 / WPF 库，杜绝误用
- **依赖审计**：仅 `Melanchall.DryWetMidi` (MIT) 一个外部包
- **GitHub Actions**：每次 push 自动跑测试矩阵

---

## 🧑‍💻 开发

### 前置

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- （M2+）Windows + Visual Studio 2022 + WPF workload

### 跑测试

```bash
dotnet test
```

**Linux 提示**：如果遇到 `ICU` 错误：
```bash
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
```

### 项目结构

详见 [docs/DESIGN.md §9 目录](docs/DESIGN.md)

---

## 🤝 贡献

欢迎 PR！但请先开 Issue 讨论。

**代码风格**：
- 4 空格缩进 / 120 字符行宽
- 一个公共类一个文件
- 公开 API 必须有 XML 注释
- 新功能必须配单元测试

**License**：本项目使用 MIT，所有贡献默认采用 MIT License。

---

## 📜 致谢

- 灵感来源：[Cuirx 博客](https://tc.cuirx.me/archives/pW9KYW8k) / [B 站 BV1KMYy6HEun](https://www.bilibili.com/video/BV1KMYy6HEun/)
- MIDI 解析：[Melanchall DryWetMidi](https://github.com/melanchall/DryWetMidi)（MIT）
- UI 框架（待 M2）：[WPF-UI](https://github.com/lepoco/wpfui)（MIT）
- 设计参考：3Blue1Brown / Linear / Stripe / Vercel 风格
