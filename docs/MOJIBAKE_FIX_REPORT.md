# 乱码修复报告：默认 Agent SystemPrompt

- **修复日期**：2026-08-09
- **修复文件**：`src/QianYuan.Api/Program.cs`
- **提交类型**：bugfix（编码还原，非功能改动）

## 一、问题描述

`src/QianYuan.Api/Program.cs` 中默认 Agent（`qianyuan.default`）的 `SystemPrompt`
存在乱码：UTF-8 编码的中文被误按 GBK 解码，形成不可读文本（如 `浣犳槸 QianYuan...`）。

该提示词是 Agent 的核心行为约束，乱码会导致运行时系统提示词不可读，
影响模型遵循 ReAct 框架与技能使用指南（brainstorming / find-skills /
skill-creator / summarize / pdf），属于直接影响对话质量的问题。

## 二、根因分析

- 文本经历了「UTF-8 字节 → GBK 解码」的 mojibake 错误链路。
- 该乱码自 git 最早提交（`19f9a31 Init workpartner`）起即存在，后续提交均继承，
  git 历史中**不存在干净原文**。
- 部分字符（如 `•`、`→`、`【】`）在早期转换中被 `?` 吞掉，无法逐字节还原，
  需结合上下文重建。

## 三、修复过程

1. 读取文件真实字节，确认编码链路（文件为 UTF-8 存储 + 单级 GBK 误解码）。
2. 对乱码文本做「GBK 编码 → UTF-8 解码」逆向，还原出约 90% 原文；
   被 `?` 吞掉的字符依据 README 技能清单与 `PreloadSkills` 配置补齐。
3. 精确替换为干净中文，保持原有 `\n` 换行结构与 C# 字符串拼接风格。
4. 校验 BOM：保持文件为单 BOM（`EF BB BF`），未引入双 BOM。

## 四、修复后文本

> 你是 QianYuan（乾元）智能助手。遵循 ReAct 框架：先思考再行动，需要外部信息时调用工具，得到观察后继续推理。
>
> 关键技术使用指南：
>
> - 当用户提及【规则/设计/需求拆解/推理/评估】等关键词时 → 调用 brainstorming 技能进行深度分析与设计
> - 当用户提及【查找技能/安装能力/扩展功能】等关键词时 → 调用 find-skills 技能查找合适的技能
> - 当用户提及【创建/新建/制作技能】等关键词时 → 调用 skill-creator 技能帮助创建
> - 当用户提及【总结/摘要/提炼】等关键词时 → 调用 summarize 技能
> - 当用户提及【PDF/阅读文档】等关键词时 → 调用 pdf 技能处理PDF
>
> 工具会根据用户意图渐进式加载——只暴露当前可能用到的技能。

## 五、验证结果

| 检查项 | 结果 |
|--------|------|
| `dotnet build src/QianYuan.Api/QianYuan.Api.csproj` | ✅ 0 错误 |
| 编译诊断（Problems 面板） | ✅ 无错误 |
| git diff | ✅ 仅 7 行乱码 → 7 行中文，其余内容不变 |
| 文件编码 / BOM | ✅ 保持单 BOM（`EF BB BF`） |

## 六、变更文件

| 文件 | 说明 |
|------|------|
| `src/QianYuan.Api/Program.cs` | 还原默认 Agent SystemPrompt 为干净中文 |
| `docs/MOJIBAKE_FIX_REPORT.md` | 本文档 |

> **说明**：工作区另有未提交改动（`appsettings.json` 含明文 API Key，
> 建议改用环境变量 / user-secrets；以及 Phase3 相关未跟踪文件），
> 均不属于本次修复，未纳入本次提交。
