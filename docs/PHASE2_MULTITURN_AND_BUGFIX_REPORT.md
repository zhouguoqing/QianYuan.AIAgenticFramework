# QianYuan 多轮对话第二阶段与问题修复说明

生成日期：2026-08-06

## 1. 文档目的

本文档记录“多轮对话功能规划”第二阶段的当前实现情况，以及本轮围绕历史会话、新建会话、侧边栏历史列表等前端反馈问题所做的修复与回退记录。

重要说明：本文档按当前代码状态编写。此前针对“新建对话后空白无反馈”的占位卡方案已按要求回退，不再计入有效实现。

## 2. 快照与留痕

- 开工前快照仍保留：`artifacts/snapshots/pre-phase2-multiturn-20260806-113636`
- 快照索引仍保留：`artifacts/snapshots/pre-phase2-multiturn-latest.txt`
- 阶段规划摘录：`artifacts/multiturn-phase2-plan-extracted.txt`
- 接口验证输出：`artifacts/multiturn-phase2-smoke/`

## 3. 第二阶段已实现内容

### 3.1 Token 计数基础

- 新增 `ITokenCounter` 抽象，用于统一估算文本 token 数。
- 新增 `HeuristicTokenCounter`，采用轻量启发式算法估算中文、英文、数字和符号 token。
- 在 Kernel DI 中注册 token counter，供 ReAct 执行链路与会话保存链路共用。

涉及文件：

- `src/QianYuan.Core/Abstractions/ITokenCounter.cs`
- `src/QianYuan.Kernel/ReAct/HeuristicTokenCounter.cs`
- `src/QianYuan.Kernel/QianYuanKernelExtensions.cs`
- `src/QianYuan.Kernel/ReAct/ReActEngine.cs`
- `src/QianYuan.Kernel/Agents/ReActAgent.cs`

### 3.2 Token 感知上下文压缩

- `LoopEngineeringOptions` 增加 `MaxContextTokens` 与 `MinRecentTurnsToKeep`。
- 上下文裁剪从字符长度判断改为 token 预算判断。
- 优先保留最近若干轮对话，早期上下文折叠为 system summary，降低长对话后续回复丢上下文的概率。

涉及文件：

- `src/QianYuan.Kernel/ReAct/LoopEngineering.cs`

### 3.3 会话保存增强

- EF 会话保存加入事务保护。
- 保存会话时重新写入消息与轮次信息，降低 Conversation、Messages、Turns 不一致风险。
- 会话消息写入 token 估算值，为后续上下文统计与检索打基础。
- 会话消息 JSON 序列化统一使用字符串枚举，减少前后端枚举解释差异。

涉及文件：

- `src/QianYuan.Data/Services/EfSessionStore.cs`

### 3.4 API 枚举输出统一

- Controller JSON 序列化注册 `JsonStringEnumConverter`。
- 新接口返回的 `ChatRole`、`ContentKind` 等枚举默认以字符串形式输出，避免前端只识别字符串枚举时历史消息无法渲染。

涉及文件：

- `src/QianYuan.Api/Program.cs`

### 3.5 消息重新生成基础能力

- 新增 `POST /api/sessions/{id}/regenerate`，用于把会话回滚到指定用户消息位置。
- 前端新增 `prepareRegenerateSession` API 封装。
- `useChat` 新增 `regenerate` 方法，支持用户消息重新生成与编辑重发。
- `ChatMessageView` 中用户消息增加“重新生成”“编辑重发”入口。

涉及文件：

- `src/QianYuan.Api/Controllers/CatalogControllers.cs`
- `src/QianYuan.Web/src/services/api.ts`
- `src/QianYuan.Web/src/hooks/useChat.ts`
- `src/QianYuan.Web/src/components/ChatMessageView.tsx`
- `src/QianYuan.Web/src/App.tsx`
- `src/QianYuan.Web/src/types/api.ts`

## 4. Bug 修复与当前状态

### 4.1 历史会话点开无内容

问题表现：

- 历史会话列表可以看到记录，但点开后聊天窗口无内容。

原因：

- 后端历史接口返回的 `role` 和 `kind` 曾经是数字枚举。
- 前端历史消息转换逻辑只按字符串枚举识别，例如 `User`、`Text`。

修复：

- 后端新响应统一输出字符串枚举。
- 前端 `useChat` 增加数字枚举兼容转换函数，旧历史数据仍可渲染。

当前状态：

- 用户已反馈“历史记录确实可以查看了”。
- 该问题视为已解决。

涉及文件：

- `src/QianYuan.Api/Program.cs`
- `src/QianYuan.Data/Services/EfSessionStore.cs`
- `src/QianYuan.Web/src/hooks/useChat.ts`
- `src/QianYuan.Web/src/types/api.ts`

### 4.2 历史会话列表大量 `??/????`

问题表现：

- 左侧历史会话列表标题出现大量 `??`、`???? OK`、`??????:???????`。
- 影响用户判断具体会话。

原因：

- 部分旧数据标题已经以问号形式写入数据库，属于历史脏数据。
- 新写入的中文标题在接口验证中可正常显示，因此不宜直接修改数据库历史内容。

修复：

- 在前端侧边栏展示层增加 `sanitizeSessionTitle`。
- 对明显由历史编码污染导致的标题做前端兜底展示，例如 `历史会话 · 时间`。
- 重命名弹窗不再默认带入脏标题，方便用户重新命名。
- 顺手修复该小区域可见中文文案，如“搜索会话 / 重命名 / 删除 / 条”。

当前状态：

- 侧边栏不再直接展示明显脏标题。
- 原始数据库标题未被批量改写，避免误伤历史数据。

涉及文件：

- `src/QianYuan.Web/src/components/Sidebar.tsx`

### 4.3 新建对话闪退 / 空白问题

问题表现：

- 用户反馈“开启新对话时会闪退”。
- 后续尝试避开闪退后，又出现“直接空白无反馈”。

已做的有效保护：

- `startNewSession` 中先 `abort()` 当前流式请求，再 `reset()` 消息状态。
- 清理当前会话 ID、专家状态、输入种子、任务/积分面板状态。

已回退的治标方案：

- 曾尝试加入 `showLanding/showChatShell`，让新建会话后进入空白聊天输入态，避免落地页重挂载。
- 曾尝试新增 `EmptyChatState` 占位反馈卡，解决空白无反馈。
- 用户判断该方案“治标不治本”，已按要求回退。

当前状态：

- 回退后，渲染逻辑恢复为 `hasMessages ? 聊天界面 : HomeLanding`。
- 闪退根因尚未最终定位，建议后续用浏览器控制台错误栈或 Electron/前端运行日志继续定位。
- 该问题不应在当前文档中标记为完全修复。

涉及文件：

- `src/QianYuan.Web/src/App.tsx`

### 4.4 前端中文乱码导致的构建/样式风险

问题表现：

- 部分历史乱码文案在文件被 UTF-8 保存后暴露为不完整字符串。
- CSS 中部分伪元素 `content` 字符串损坏，导致 Vite 构建出现 CSS syntax warning，可能截断后续样式。

修复：

- 修复 `App.tsx` 中因历史乱码导致的损坏字符串，替换为合法中文文案。
- 修复 `styles.css` 中损坏的伪元素 `content` 字符串，例如光标、下拉箭头、选中勾选符号。

当前状态：

- 前端构建通过，CSS syntax warning 已消除。

涉及文件：

- `src/QianYuan.Web/src/App.tsx`
- `src/QianYuan.Web/src/styles.css`

## 5. 已验证内容

已执行：

```powershell
npm run build --prefix src/QianYuan.Web
```

结果：

- TypeScript 编译通过。
- Vite build 通过。
- 仍保留既有 chunk size warning，此 warning 与本轮功能修改无直接关系。

曾执行的后端验证：

```powershell
dotnet build QianYuan.AgenticFramework.sln --no-restore
```

结果：

- 后端构建通过。
- 仅保留项目既有 warning。

接口级验证记录：

- `GET /api/sessions?take=5` 可返回会话列表。
- `GET /api/sessions/{id}` 可返回会话详情。
- 验证样例中 `role = User`、`kind = Text`，中文正文可读。
- 结果文件：`artifacts/multiturn-phase2-smoke/session-detail.json`

## 6. 未完成 / 后续建议

1. 新建对话闪退根因定位
   - 当前只保留了 `abort + reset` 基础保护。
   - 后续应采集浏览器控制台错误栈，优先检查 `HomeLanding` 与 `Composer` 在清空消息后重挂载时的运行时异常。

2. SSE 精确断线续传
   - 当前实现是中断后尽量保存 transcript。
   - 尚未实现 `Last-Event-ID`、服务端事件缓冲或 streaming event 持久化。

3. LLM 语义摘要
   - 当前上下文压缩使用本地摘要折叠和 token 预算裁剪。
   - 尚未引入额外 LLM 调用生成高质量摘要，以避免本阶段增加成本与不稳定点。

4. 历史脏标题治理
   - 当前采用前端展示兜底。
   - 如后续需要彻底清理，可新增“只修复明显问号标题”的数据修复脚本，但应先备份数据库。

## 7. 相关文件汇总

后端：

- `src/QianYuan.Api/Program.cs`
- `src/QianYuan.Api/Controllers/CatalogControllers.cs`
- `src/QianYuan.Api/Controllers/ChatController.cs`
- `src/QianYuan.Data/Services/EfSessionStore.cs`
- `src/QianYuan.Core/Abstractions/ITokenCounter.cs`
- `src/QianYuan.Kernel/ReAct/HeuristicTokenCounter.cs`
- `src/QianYuan.Kernel/ReAct/LoopEngineering.cs`
- `src/QianYuan.Kernel/ReAct/ReActEngine.cs`
- `src/QianYuan.Kernel/Agents/ReActAgent.cs`
- `src/QianYuan.Kernel/QianYuanKernelExtensions.cs`

前端：

- `src/QianYuan.Web/src/App.tsx`
- `src/QianYuan.Web/src/components/ChatMessageView.tsx`
- `src/QianYuan.Web/src/components/Sidebar.tsx`
- `src/QianYuan.Web/src/hooks/useChat.ts`
- `src/QianYuan.Web/src/services/api.ts`
- `src/QianYuan.Web/src/types/api.ts`
- `src/QianYuan.Web/src/styles.css`

## 8. 回退说明

如果需要回到第二阶段开工前，可使用仍保留的快照：

```powershell
robocopy artifacts\snapshots\pre-phase2-multiturn-20260806-113636 . /MIR /XD .git node_modules bin obj dist
```

注意：执行回退前建议再保存当前工作区，避免覆盖后续已认可的历史会话修复与说明文档。
