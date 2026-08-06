#!/usr/bin/env bash

# QianYuan 自动技能选择配置验证脚本

echo "🔍 QianYuan 自动技能选择配置验证"
echo "=================================="
echo ""

# 检查 1: 技能文件是否存在
echo "✓ 检查 1: 技能文件是否存在"
echo "---"

SKILLS=(
  "./.agents/skills/brainstorming/SKILL.md"
  "./.agents/skills/find-skills/SKILL.md"
  "./.agents/skills/using-superpowers/SKILL.md"
  "./.agents/skills/summarize/SKILL.md"
  "./.agents/skills/pdf/SKILL.md"
  "./.agents/skills/skill-creator/SKILL.md"
)

for skill in "${SKILLS[@]}"; do
  if [ -f "$skill" ]; then
    echo "  ✅ $skill"
  else
    echo "  ❌ $skill (缺失)"
  fi
done

echo ""

# 检查 2: appsettings.json 配置
echo "✓ 检查 2: appsettings.json 配置"
echo "---"

APPSETTINGS="src/QianYuan.Api/appsettings.json"

if [ -f "$APPSETTINGS" ]; then
  if grep -q '"./.agents/skills"' "$APPSETTINGS"; then
    echo "  ✅ SkillDirectories 中配置了 ./.agents/skills"
  else
    echo "  ❌ SkillDirectories 中未配置 ./.agents/skills"
  fi
else
  echo "  ⚠️  $APPSETTINGS 文件不存在"
fi

echo ""

# 检查 3: Program.cs 中的 Agent 配置
echo "✓ 检查 3: Program.cs 中的 Agent 配置"
echo "---"

PROGRAM_CS="src/QianYuan.Api/Program.cs"

if [ -f "$PROGRAM_CS" ]; then
  if grep -q 'UseProgressiveSkillLoading' "$PROGRAM_CS"; then
    echo "  ✅ 配置了 UseProgressiveSkillLoading"
  else
    echo "  ⚠️  未显式配置 UseProgressiveSkillLoading（默认启用）"
  fi
  
  if grep -q 'brainstorming' "$PROGRAM_CS"; then
    echo "  ✅ SystemPrompt 中提及 brainstorming"
  else
    echo "  ⚠️  SystemPrompt 中未明确提及技能用途"
  fi
  
  if grep -q 'find-skills' "$PROGRAM_CS"; then
    echo "  ✅ SystemPrompt 中提及 find-skills"
  else
    echo "  ⚠️  SystemPrompt 中未明确提及 find-skills"
  fi
else
  echo "  ⚠️  $PROGRAM_CS 文件不存在"
fi

echo ""

# 检查 4: SkillManager 中的得分函数
echo "✓ 检查 4: SkillManager 中的关键词配置"
echo "---"

SKILL_MANAGER="src/QianYuan.Kernel/Skills/SkillManager.cs"

if [ -f "$SKILL_MANAGER" ]; then
  if grep -q 'brainstorm' "$SKILL_MANAGER"; then
    echo "  ✅ 配置了 brainstorm 关键词匹配"
  else
    echo "  ❌ 未找到 brainstorm 关键词匹配"
  fi
  
  if grep -q 'find-skills' "$SKILL_MANAGER"; then
    echo "  ✅ 配置了 find-skills 关键词匹配"
  else
    echo "  ❌ 未找到 find-skills 关键词匹配"
  fi
  
  if grep -q 'skill-creator' "$SKILL_MANAGER"; then
    echo "  ✅ 配置了 skill-creator 关键词匹配"
  else
    echo "  ⚠️  未找到 skill-creator 关键词匹配（需要手动添加）"
  fi
else
  echo "  ⚠️  $SKILL_MANAGER 文件不存在"
fi

echo ""
echo "=================================="
echo "✨ 验证完成！"
echo ""
echo "后续步骤："
echo "1. 如果有任何 ❌ 项，请查看 docs/SKILL_AUTO_SELECTION.md 中的故障排查部分"
echo "2. 如果所有检查都通过，可以启动应用并测试技能自动选择"
echo "3. 运行测试用例来验证功能（见 docs/SKILL_AUTO_SELECTION.md）"
echo ""
