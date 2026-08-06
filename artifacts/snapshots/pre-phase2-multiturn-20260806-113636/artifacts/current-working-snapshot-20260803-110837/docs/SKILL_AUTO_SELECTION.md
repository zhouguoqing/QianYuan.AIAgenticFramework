# 自动技能选择实现指南

## ✅ 已完成的配置

### 1. 创建的技能
- ✅ `brainstorming` - 规划和设计用
- ✅ `find-skills` - 查找和安装技能用  
- ✅ `using-superpowers` - 技能引导工具
- ✅ `summarize` - 文本总结用
- ✅ `pdf` - PDF处理用
- ✅ `skill-creator` - 新增，技能创建用

### 2. 配置的位置

#### appsettings.json
```json
"SkillDirectories": [
  {
    "Path": "./.agents/skills",
    "Recursive": true,
    "Enabled": true,
    "IdPrefix": "agent"
  }
]
```

#### Program.cs - ReActAgentDefinition
- ✅ `UseProgressiveSkillLoading = true` - 启用渐进式选择
- ✅ `ProgressiveTopK = 8` - 每轮最多选8个技能
- ✅ `SystemPrompt` - 明确指导何时使用各技能

---

## 🎯 工作原理

### 技能ID映射关系
| 文件夹名 | 技能ID | 触发关键词 |
|---------|--------|-----------|
| `brainstorming` | `agent.brainstorming` | plan, design, 规划, 设计等 |
| `find-skills` | `agent.find-skills` | skill, findskill, 技能等 |
| `using-superpowers` | `agent.using-superpowers` | （任何时候 +1 分） |
| `summarize` | `agent.summarize` | summary, summarize, 总结等 |
| `pdf` | `agent.pdf` | pdf, 阅读pdf等 |
| `skill-creator` | `agent.skill-creator` | createskill, 新建技能等 |

### 推理流程

```
用户输入
    ↓
SkillManager.SelectRelevantAsync()
    ↓
Score(技能, 用户输入)
    ↓
根据关键词匹配计算得分：
  ├─ 基础得分（ID/Name/Description匹配）
  ├─ 高权重技能得分（ScoreWellKnownPromptSkill）
  │  ├─ using-superpowers: +1
  │  ├─ brainstorming: +16（如果提到plan/design等）
  │  ├─ find-skills: +16（如果提到skill/技能等）
  │  ├─ skill-creator: +16（如果提到createskill等）
  │  ├─ summarize: +16（如果提到summary/总结等）
  │  └─ pdf: +16（如果提到pdf等）
    ↓
返回 Top 8 个最高分技能
    ↓
注入到 System Prompt
    ↓
LLM 可见这些工具并调用
```

---

## 🧪 测试方案

### 测试用例 1: 规划与设计
**输入：** "帮我设计一个电商系统，需要规划一下"

**预期结果：**
- ✅ `agent.brainstorming` 被选中（匹配 design, 规划 → +16）
- ✅ `agent.using-superpowers` 被选中（基础 +1）
- ✅ 模型应该调用 brainstorming 技能进行深度分析

### 测试用例 2: 查找技能
**输入：** "我想找一个技能来处理 PDF 文件"

**预期结果：**
- ✅ `agent.find-skills` 被选中（匹配 skill, 技能 → +16）
- ✅ `agent.pdf` 被选中（匹配 PDF）
- ✅ 模型应该先调用 find-skills 搜索，然后使用 pdf 技能

### 测试用例 3: 创建技能
**输入：** "我想创建一个新的技能来统计代码"

**预期结果：**
- ✅ `agent.skill-creator` 被选中（匹配 createskill, 创建, 技能 → +16）
- ✅ 模型应该调用 skill-creator 技能帮助设计和创建

### 测试用例 4: 总结文档
**输入：** "帮我总结这篇文章的要点"

**预期结果：**
- ✅ `agent.summarize` 被选中（匹配 summarize, 总结 → +16）
- ✅ 模型应该调用 summarize 技能

### 测试用例 5: PDF 处理
**输入：** "读取这个 PDF 文件并提取表格"

**预期结果：**
- ✅ `agent.pdf` 被选中（匹配 pdf, 读取 → +16）
- ✅ 模型应该调用 pdf 技能处理

### 测试用例 6: 混合场景
**输入：** "帮我制定一个项目计划，并创建对应的技能"

**预期结果：**
- ✅ `agent.brainstorming` 被选中（匹配 plan, 计划 → +16）
- ✅ `agent.skill-creator` 被选中（匹配 createskill, 创建技能 → +16）
- ✅ 两个技能都被加载，模型可灵活选择

---

## 🔧 故障排查

### 如果技能未被选中

1. **检查技能是否已加载**
   ```bash
   # 访问 API 获取技能列表
   curl http://localhost:5050/api/skills
   ```
   查看响应中是否包含这些技能

2. **检查 ID 是否正确**
   - 文件位置：`./.agents/skills/brainstorming/SKILL.md`
   - 生成的 ID 应为：`agent.brainstorming`
   - 可通过日志查看

3. **检查关键词是否匹配**
   - 在 [SkillManager.cs](../../src/QianYuan.Kernel/Skills/SkillManager.cs#L207-L222) 中查看 `ScoreWellKnownPromptSkill` 的关键词列表
   - 确保您的输入包含列表中的关键词

4. **检查得分函数**
   ```csharp
   // SkillManager.cs 中的 IsSkill 函数
   // 匹配规则：ID 以候选项结尾，或 Name 等于候选项
   if (id.EndsWith(candidate) || name == candidate)
       return true;
   ```

### 常见问题

**Q: 为什么我说"帮助"但技能没被选中？**
A: "帮助"不在关键词列表中。使用系统提示中明确列出的关键词，如"设计"、"规划"、"技能"等。

**Q: 是否需要手动触发技能？**
A: 不需要。当用户提到相关关键词时，技能会自动被选中并注入到工具列表中。模型可选择调用。

**Q: 能否强制总是加载某些技能？**
A: 可以，在 ReActAgentDefinition 中设置 `PreloadSkills`：
```csharp
PreloadSkills = new[] { 
    "agent.brainstorming",
    "agent.find-skills",
    "agent.using-superpowers"
}
```

---

## 📊 性能考虑

- **初始化时**：只加载技能 manifest（轻量），不实例化
- **推理时**：每轮根据意图选择最相关的 top-8 技能
- **延迟实例化**：只当模型调用时才真正加载技能（GetAsync）

这样设计既保证了功能完整性，又不会因为加载过多技能导致性能下降。

---

## 📝 维护建议

### 添加新的 prompt 类技能时

1. 在 `./.agents/skills/` 下创建新目录
2. 添加 `SKILL.md` 文件（参考 `brainstorming/SKILL.md`）
3. 在 [SkillManager.cs](../../src/QianYuan.Kernel/Skills/SkillManager.cs#L217) 的 `ScoreWellKnownPromptSkill` 中添加匹配规则：
   ```csharp
   if (HasAny(normalizedIntent, "your-keyword-1", "your-keyword-2", ...))
   {
       if (IsSkill(manifest, "your-skill-id")) score += 16;
   }
   ```

### 调整关键词时

编辑 [SkillManager.cs](../../src/QianYuan.Kernel/Skills/SkillManager.cs#L207-L222)，修改 `HasAny` 调用中的关键词列表

