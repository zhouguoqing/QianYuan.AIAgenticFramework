# QIANYUAN 第二阶段专家组功能实现说明

## 一、实现目标

本次改动按照短期目标规划继续推进第二阶段“专家组”能力建设，在第一阶段专家市场和自定义专家能力的基础上，补齐专家组模板、专家组创建、团队与成员维护、任务编排、顺序/并行执行、SSE 流式执行进度，以及前端专家组工作台。所有原品牌相关专家组和专家文案均统一调整为 `QIANYUAN` / `qianyuan`，源码和文档中不保留禁用品牌词。

## 二、整体功能概览

- 从专家清单读取 `team` 类型数据，生成可复用的专家组模板。
- 支持用户从模板一键创建专家组。
- 支持专家组基础信息编辑、软删除。
- 支持专家组成员新增、编辑、删除、启停，以及执行模式配置。
- 支持任务按专家组成员自动编排为工作步骤。
- 支持 `Sequential` 顺序步骤和连续 `Parallel` 并行步骤混合执行。
- 支持任务执行过程通过 `text/event-stream` 实时返回到前端。
- 支持前端查看执行日志、步骤状态、产物内容和最终报告。
- 默认专家组命名和执行提示统一使用 `QIANYUAN`。

## 三、后端改动文件

### `src/QianYuan.Api/Models/ExpertTeamModels.cs`

新增和扩展专家组模型：

- `UpdateExpertTeamRequest`：用于更新专家组名称、描述、场景和启用状态。
- `UpdateExpertTeamMemberRequest`：用于更新成员顺序、角色、展示名、绑定 Agent、职责、执行模式和启用状态。
- `ExpertTeamTemplateMemberDto`：描述模板中的成员角色、展示名、专业方向、职责和默认执行模式。
- `ExpertTeamTemplateDto`：描述专家组模板的完整信息，包括模板 ID、名称、描述、场景、分类、标签、初始化提示和成员列表。
- `ExpertTeamExecutionEventDto`：描述执行流事件，包含事件类型、任务 ID、团队 ID、步骤 ID、步骤名、执行模式、状态、消息和时间。

### `src/QianYuan.Api/Services/ExpertTeamTemplateService.cs`

新增专家组模板服务：

- 启动时读取 `src/QianYuan.Api/Data/experts/expert-manifest.json`。
- 筛选 `expertType` 为 `team` 的条目作为专家组模板。
- 提取模板名称、描述、场景、分类、标签、默认初始化提示和成员列表。
- 将首位成员或 `lead` 成员默认设为 `Sequential`。
- 将其他成员默认设为 `Parallel`，用于并行专家分析。
- 根据成员专业方向自动生成职责说明，供任务编排时写入步骤摘要。

### `src/QianYuan.Api/Services/ExpertTeamService.cs`

重构专家组核心服务，实现第二阶段主要业务逻辑：

- `CreateFromTemplateAsync`：根据模板创建用户自己的专家组。
- `UpdateAsync`：更新专家组名称、描述、场景和启用状态。
- `DeleteAsync`：软删除专家组，避免直接破坏历史任务引用。
- `AddMemberAsync`：向专家组添加成员。
- `UpdateMemberAsync`：更新专家组成员配置。
- `DeleteMemberAsync`：删除专家组成员。
- `OrchestrateTaskAsync`：根据专家组成员生成任务步骤，并创建 `expert-team-plan.md` 编排产物。
- `ExecuteTaskAsync`：执行专家组任务，按 `Sequential` 与 `Parallel` 分组处理。
- `BuildExecutionGroups`：将连续并行步骤聚合为一组，并通过 `Task.WhenAll` 并行执行。
- `RunPreparedStepAsync`：调用成员绑定的 Agent 执行当前步骤。
- `LoadCompletedOutputsAsync`：读取历史专家输出，作为后续步骤上下文。
- `BuildExecutionReport`：任务完成后生成 `expert-team-execution-report.md`。
- `EmitAsync`：向控制器回调执行事件，用于 SSE 流式输出。
- 默认团队改为 `QIANYUAN Strategy Expert Team` 和 `QIANYUAN Product Expert Team`。

### `src/QianYuan.Api/Controllers/ExpertTeamsController.cs`

扩展专家组控制器接口：

- `GET /api/expert-teams/templates`：获取专家组模板列表。
- `POST /api/expert-teams/from-template/{templateId}`：从模板创建专家组。
- `PUT /api/expert-teams/{teamId}`：更新专家组。
- `DELETE /api/expert-teams/{teamId}`：软删除专家组。
- `POST /api/expert-teams/{teamId}/members`：新增专家组成员。
- `PUT /api/expert-teams/{teamId}/members/{memberId}`：更新专家组成员。
- `DELETE /api/expert-teams/{teamId}/members/{memberId}`：删除专家组成员。
- `POST /api/work-tasks/{taskId}/orchestrate`：按专家组编排任务步骤。
- `POST /api/work-tasks/{taskId}/execute`：普通 HTTP 方式执行任务。
- `GET /api/work-tasks/{taskId}/execute-stream`：SSE 方式流式执行任务。

SSE 事件类型包括：

- `task_started`：任务开始。
- `step_started`：步骤开始。
- `step_completed`：步骤完成。
- `step_failed`：步骤失败。
- `task_failed`：任务失败。
- `task_completed`：任务完成。

### `src/QianYuan.Api/Program.cs`

新增依赖注入注册：

- `IExpertTeamTemplateService` 注册为 `ExpertTeamTemplateService`。

### `src/QianYuan.Api/Services/WorkTaskExecutionHarness.cs`

适配专家组服务的新执行签名：

- 后台执行任务时传入空事件回调。
- 保留原有任务运行时状态管理、取消和错误记录能力。

### `src/QianYuan.Api/Models/WorkTaskModels.cs`

扩展工作步骤 DTO：

- `WorkStepDto` 新增 `ExecutionMode` 字段，前端可展示步骤是顺序执行还是并行执行。

### `src/QianYuan.Api/Services/WorkTaskService.cs`

更新工作任务映射逻辑：

- 将实体 `WorkStep.ExecutionMode` 映射到前端 `WorkStepDto.executionMode`。

## 四、数据层改动文件

### `src/QianYuan.Data/Entities/WorkTasks.cs`

为 `WorkStep` 实体新增字段：

- `ExecutionMode`：默认值为 `Sequential`。

该字段用于描述步骤执行方式：

- `Sequential`：按顺序执行，等待前序步骤完成。
- `Parallel`：与相邻连续并行步骤一起并发执行。

### `src/QianYuan.Data/QianYuanDbContext.cs`

新增字段配置：

- `WorkStep.ExecutionMode` 最大长度为 40。

### `src/QianYuan.Data/DataServiceCollectionExtensions.cs`

更新数据库初始化和兼容逻辑：

- 新建 `WorkSteps` 表时包含 `ExecutionMode` 字段。
- SQLite 自动检测并补充缺失列。
- PostgreSQL 使用 `ADD COLUMN IF NOT EXISTS` 兼容旧库。
- SQL Server 使用 `COL_LENGTH` 检查并补列。
- 已有数据默认填充为 `Sequential`。

## 五、前端改动文件

### `src/QianYuan.Web/src/types/api.ts`

新增前端类型：

- `CreateExpertTeamRequest`
- `UpdateExpertTeamRequest`
- `CreateExpertTeamMemberRequest`
- `UpdateExpertTeamMemberRequest`
- `ExpertTeamTemplateDto`
- `ExpertTeamTemplateMemberDto`
- `ExpertTeamExecutionEventDto`

同时为 `WorkStepDto` 增加：

- `executionMode`

### `src/QianYuan.Web/src/services/api.ts`

新增专家组 API 封装：

- `listExpertTeamTemplates`：获取模板列表。
- `createExpertTeam`：创建专家组。
- `createExpertTeamFromTemplate`：从模板创建专家组。
- `updateExpertTeam`：更新专家组。
- `deleteExpertTeam`：删除专家组。
- `addExpertTeamMember`：新增成员。
- `updateExpertTeamMember`：更新成员。
- `deleteExpertTeamMember`：删除成员。
- `executeWorkTaskStream`：使用认证 `fetch` 读取 SSE 执行流。

`executeWorkTaskStream` 没有使用原生 `EventSource`，原因是当前系统需要携带本地认证头，使用 `fetch` 流解析可以保留 JWT 认证能力。

### `src/QianYuan.Web/src/components/WorkTasksPanel.tsx`

重写工作任务面板，升级为 `QIANYUAN Expert Team Workbench`：

- 支持创建绑定专家组的工作任务。
- 支持查看专家组模板并从模板创建专家组。
- 支持编辑专家组名称、场景和描述。
- 支持删除专家组。
- 支持新增、编辑、删除专家组成员。
- 支持配置成员执行模式：`Sequential` 或 `Parallel`。
- 支持配置成员顺序、角色 ID、展示名、绑定 Agent、职责和启用状态。
- 支持一键编排任务，将成员转换为工作步骤。
- 支持流式运行任务，实时显示任务和步骤事件。
- 支持查看步骤状态、执行模式、摘要和产物。
- 支持取消正在运行的任务。

### `src/QianYuan.Web/src/App.tsx`

做了轻量适配：

- 打开 `WorkTasksPanel` 时，将 `provider` / `model` 的 `null` 转为 `undefined`，满足组件类型定义。
- 保留专家召唤时绑定 Agent 的选择逻辑，使自定义专家和专家组能力保持一致。

### `src/QianYuan.Web/src/styles.css`

新增工作台样式：

- 专家组模板区域样式。
- 团队编辑卡片样式。
- 成员列表样式。
- 成员编辑表单样式。
- 执行日志和执行事件样式。
- 小屏幕响应式布局。

## 六、品牌与文案清理

本次改动继续执行品牌清理要求：

- 默认专家组名称使用 `QIANYUAN`。
- 专家组执行提示使用 `QIANYUAN expert team member`。
- 专家清单中的原品牌相关内容已统一替换。
- 源码、文档、README 和技能目录中未发现禁用品牌词残留。

## 七、验证结果

已执行以下验证命令：

```powershell
dotnet build QianYuan.AgenticFramework.sln --no-restore
npm run build --prefix src/QianYuan.Web
rg -n "<forbidden-brand-pattern>" src docs README.md skills -S -g '!**/bin/**' -g '!**/obj/**'
```

验证结果：

- 后端解决方案构建通过。
- 前端 TypeScript 编译和 Vite 构建通过。
- 禁用品牌词扫描无匹配结果。

当前仍存在项目既有非阻塞警告，包括 SQLite 依赖安全提示、包裁剪建议和 Vite 大 chunk 提示；这些不是本次专家组功能改动引入的阻塞问题。

## 八、本次新增能力总结

第二阶段完成后，系统已经具备完整专家组闭环：

1. 从专家组模板创建 QIANYUAN 专家组。
2. 在前端维护团队成员和执行模式。
3. 将工作任务编排为专家步骤。
4. 按顺序和并行策略执行专家步骤。
5. 通过 SSE 实时反馈执行过程。
6. 保存专家输出、失败产物和最终执行报告。
7. 前端可查看任务、步骤、日志和产物。
