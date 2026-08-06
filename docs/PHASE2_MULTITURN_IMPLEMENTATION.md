# QianYuan 多轮对话第二阶段实现说明（上下文管理与对话连贯性）

生成日期：2026-08-06

## 1. 快照与留痕

- 开工前快照：`artifacts/snapshots/pre-phase2-multiturn-20260806-113636`
- 快照索引：`artifacts/snapshots/pre-phase2-multiturn-latest.txt`
- 规划摘录：`artifacts/multiturn-phase2-plan-extracted.txt`
- 验证输出：`artifacts/multiturn-phase2-smoke/`

## 2. 本阶段目标对应关系

### 已完成

1. Token 计数基础设施
   - 新增轻量启发式 token 计数接口与实现，不引入外部 tokenizer 依赖。
   - 会话消息保存时写入估算 token，便于后续统计与上下文裁剪。

2. Token 感知上下文压缩
   - ReAct 上下文压缩从字符长度裁剪升级为 token 预算裁剪。
   - 默认保留最近若干轮对话，并把更早上下文折叠为系统摘要，降低长对话丢上下文的概率。

3. 会话状态原子性保护
   - EF 会话保存改为事务化删除旧消息再写入新消息、轮次信息，降低中途失败造成半写入的风险。
   - SSE 中断时使用非请求取消令牌保存已收集到的阶段性 transcript，尽量避免流式断开后历史完全丢失。

4. 历史会话读取修复
   - 后端统一 JSON 枚举字符串输出，避免 `role/kind` 返回数字导致前端无法识别。
   - 前端保留对旧数字枚举历史数据的兼容，旧数据也可正常渲染消息内容。

5. 新对话闪退修复
   - 新建会话前先中断当前流，再清空消息、专家态和输入种子，避免组件在 streaming 状态被卸载时异常。

6. 消息编辑与重新生成
   - 新增 `POST /api/sessions/{id}/regenerate`，可回滚到指定用户消息之前。
   - 前端用户消息增加“重新生成”“编辑重发”操作，编辑后会截断后续历史并重新走正常流式生成。

7. 历史会话 UI 可读性修复
   - 修复消息组件内核心中文文案乱码。
   - 历史消息携带源消息索引，支持从历史消息触发重生成。

### 暂未完全实现

1. SSE 事件级断线续传
   - 当前实现做到“中断后尽量保存已完成 transcript + 前端可重新加载/重生成”。
   - 尚未实现基于 `Last-Event-ID` 的事件缓冲和精确续传；该能力需要新增服务端事件缓存或持久化 streaming event 表。

2. LLM 语义摘要
   - 当前为本地摘要折叠与 token 预算裁剪。
   - 尚未额外调用模型生成高质量语义摘要，以避免本阶段引入额外费用和失败点。

## 3. 改动文件说明

### 后端 API

- `src/QianYuan.Api/Program.cs`
  - 为 Controller JSON 序列化注册 `JsonStringEnumConverter`，让 `ChatRole`、`ContentKind` 等枚举默认以字符串返回。

- `src/QianYuan.Api/Controllers/ChatController.cs`
  - SSE 流式请求增加会话中断保护，流中断后仍保存已积累 transcript。
  - 新增 `ReuseLastUserMessage` 请求字段，为后续不重复追加用户消息的重生成模式预留能力。

- `src/QianYuan.Api/Controllers/CatalogControllers.cs`
  - `SessionsController` 支持会话列表关键字过滤。
  - 新增会话重生成准备接口 `POST /api/sessions/{id}/regenerate`。
  - 新建会话默认标题改为中文 `新会话`。

### 数据层

- `src/QianYuan.Data/Services/EfSessionStore.cs`
  - 会话读写改为使用字符串枚举序列化消息 JSON。
  - 保存会话时写入 message token 估算值。
  - 保存流程加入事务，保证 Conversation、Messages、Turns 同步更新。

### Core / Kernel

- `src/QianYuan.Core/Abstractions/ITokenCounter.cs`
  - 新增 token 计数抽象。

- `src/QianYuan.Kernel/ReAct/HeuristicTokenCounter.cs`
  - 新增启发式 token 计数实现，兼容中文、英文、数字与常见符号。

- `src/QianYuan.Kernel/QianYuanKernelExtensions.cs`
  - 注册 `ITokenCounter` 默认实现。

- `src/QianYuan.Kernel/ReAct/ReActEngine.cs`
  - ReAct 引擎接收 token counter 并传入 loop runtime。

- `src/QianYuan.Kernel/Agents/ReActAgent.cs`
  - 从 DI 获取 token counter，保证 agent 执行链路使用统一计数策略。

- `src/QianYuan.Kernel/ReAct/LoopEngineering.cs`
  - 新增 `MaxContextTokens` 与 `MinRecentTurnsToKeep` 配置。
  - 上下文压缩改为 token 预算驱动，优先保留最近对话轮次。

### 前端

- `src/QianYuan.Web/src/types/api.ts`
  - `ChatMessageDto.role`、`ContentPartDto.kind` 兼容字符串枚举与历史数字枚举。
  - 新增 `SessionRegenerateRequest`。
  - `StreamRequest` 增加 `reuseLastUserMessage` 字段。

- `src/QianYuan.Web/src/services/api.ts`
  - 新增 `prepareRegenerateSession`，封装重生成准备接口。

- `src/QianYuan.Web/src/hooks/useChat.ts`
  - 历史会话加载时兼容数字枚举，修复“点开历史无内容”。
  - DisplayMessage 增加源消息索引，用于定位被编辑/重生成的用户消息。
  - 新增 `regenerate` 方法：先回滚服务端会话，再重新发送用户消息生成回复。
  - 修复部分图片生成相关中文文案乱码。

- `src/QianYuan.Web/src/components/ChatMessageView.tsx`
  - 修复消息头、工具调用、复制、token 使用量等中文文案。
  - 用户消息新增“重新生成”“编辑重发”按钮。

- `src/QianYuan.Web/src/App.tsx`
  - 新建会话时先 abort 再 reset，避免 streaming 状态导致闪退。
  - 加载历史会话时先 abort 并切回聊天视图。
  - 接入用户消息重生成回调。

## 4. Bug 修复说明

### Bug 1：开启新对话闪退

原因：原有流程在当前流式请求仍运行时切换/清空会话状态，可能触发组件状态竞争。

修复：`startNewSession` 先执行 `abort()`，再执行 `reset()` 与状态清理，保证正在 streaming 的消息先停止。

### Bug 2：历史会话大量 `???`、点开无内容

原因分两类：

1. 历史无内容：后端返回数字枚举，前端只按字符串枚举判断，导致用户/助手消息被过滤或识别失败。
2. 标题 `???`：部分旧数据已经在数据库中被写成问号，属于历史脏数据；新写入数据已验证中文标题可正常保存。

修复：

- 后端新响应统一返回字符串枚举。
- 前端兼容旧数字枚举。
- 已通过接口验证最新中文历史详情可读。

## 5. 验证结果

已执行：

```powershell
dotnet build QianYuan.AgenticFramework.sln --no-restore
npm run build --prefix src/QianYuan.Web
```

结果：

- 后端构建成功：0 error，保留既有 warning。
- 前端构建成功：TypeScript 与 Vite build 均通过，仅保留既有 chunk size warning。

接口级验证：

- 启动 API 后调用 `GET http://127.0.0.1:5050/api/sessions?take=5` 成功返回会话列表。
- 调用 `GET http://127.0.0.1:5050/api/sessions/{id}` 成功返回详情。
- 验证样例中 `FirstRole = User`、`FirstKind = Text`，中文正文可读。
- 验证结果保存于 `artifacts/multiturn-phase2-smoke/session-detail.json`。

## 6. 回退方式

如需回退到本阶段开工前状态，可使用快照：

```powershell
robocopy artifacts\snapshots\pre-phase2-multiturn-20260806-113636 . /MIR /XD .git node_modules bin obj dist
```

执行回退前建议再次保存当前工作区，避免覆盖后续新增成果。

---

更新说明（2026-08-06）：后续又追加了历史会话列表标题修复、前端乱码字符串修复，并按要求回退了新建会话空白占位的治标方案。当前准确状态以 `docs/PHASE2_MULTITURN_AND_BUGFIX_REPORT.md` 为准。
