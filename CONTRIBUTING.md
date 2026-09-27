# 贡献指南

感谢你考虑为「风声未止」做贡献！🌿

## 开发流程

1. Fork 仓库
2. 创建 feature 分支：`git checkout -b feature/your-feature`
3. 写代码 + 写测试
4. 跑测试：`dotnet test`（必须 100% 通过）
5. 提交：`git commit -m "feat: 你的功能描述"`
6. Push：`git push origin feature/your-feature`
7. 开 PR，填写 PR 模板

## Commit 规范

我们用 [Conventional Commits](https://www.conventionalcommits.org/)：

```
feat: 新增 AI 智能编配模块
fix: 修复 MIDI 解析时跨拍错误
docs: 更新 README 的安装说明
test: 给 HarmonicaConverter 加边界测试
refactor: 抽出 MidiNote 验证逻辑
chore: 升级 NUnit 到 4.0
```

## 代码风格

- 4 空格缩进（C# 默认）
- 120 字符行宽
- 一个公共类一个文件
- 公开 API 必须有 XML 注释
- 命名：类 PascalCase、方法 PascalCase、字段 _camelCase、参数 camelCase

## 测试规范

- 新功能必须配单元测试
- 边界条件优先（空/单元素/极大/极小）
- 用 NUnit 不用 xUnit
- 一个测试一个断言为主，多个相关断言可接受

## PR Review

- 至少 1 位维护者 approve 才合并
- CI 必须全绿（build + test + CodeQL + Dependabot）
- 不接受"先合并后改"PR

## 行为准则

- 友善、尊重、不骚扰
- 关注事不针对人
- 接受建设性批评

## 提 Issue

**Bug**：用 `.github/ISSUE_TEMPLATE/bug.yml` 模板
**Feature**：用 `.github/ISSUE_TEMPLATE/feature.yml` 模板
**问题**：直接开 Issue 但先搜是否已有

## License

贡献的代码默认采用 MIT License（与项目一致）。
