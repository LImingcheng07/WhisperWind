#!/usr/bin/env bash
# 推送到 GitHub 一键脚本
# 用法:
#   1. 去 https://github.com/settings/tokens/new 生成 token (勾 repo)
#   2. export GITHUB_TOKEN=ghp_xxxx
#   3. export GITHUB_USER=你的用户名
#   4. ./scripts/push-to-github.sh

set -euo pipefail

: "${GITHUB_TOKEN:?需要先 export GITHUB_TOKEN=ghp_xxx}"
: "${GITHUB_USER:?需要先 export GITHUB_USER=你的用户名}"

REPO="WhisperWind"
DESCRIPTION="🍃 风声未止 — 三角洲行动自动口琴 · 诗与远方国风 · MIT 开源"
VISIBILITY="${VISIBILITY:-public}"

echo ">>> 创建 GitHub 仓库: $GITHUB_USER/$REPO"
HTTP_CODE=$(curl -s -o /tmp/gh-create.json -w "%{http_code}" \
  -H "Authorization: token $GITHUB_TOKEN" \
  -H "Accept: application/vnd.github+json" \
  -X POST https://api.github.com/user/repos \
  -d "{\"name\":\"$REPO\",\"description\":\"$DESCRIPTION\",\"private\":$( [ "$VISIBILITY" = "private" ] && echo true || echo false ),\"has_issues\":true,\"has_projects\":true,\"has_wiki\":false}")

if [ "$HTTP_CODE" = "201" ]; then
  echo "✅ 仓库创建成功"
elif [ "$HTTP_CODE" = "422" ]; then
  echo "⚠️  仓库已存在，跳过创建"
else
  echo "❌ 创建失败 (HTTP $HTTP_CODE)"
  cat /tmp/gh-create.json
  exit 1
fi

echo ">>> 设置 remote 并推送"
cd "$(dirname "$0")/.."
git remote remove origin 2>/dev/null || true
git remote add origin "https://$GITHUB_TOKEN@github.com/$GITHUB_USER/$REPO.git"
git push -u origin main

echo ""
echo "🎉 完成! 仓库地址: https://github.com/$GITHUB_USER/$REPO"
echo ""
echo "下一步建议:"
echo "  1. 访问仓库 Settings → General → 开启 Discussions (社区)"
echo "  2. 编辑 .github/CODEOWNERS 填入你的 GitHub 用户名"
echo "  3. 编辑 README/CHANGELOG/SECURITY 替换占位符"
echo "  4. 创建 v0.1.0 release (M1 tag)"
