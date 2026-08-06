# 第三阶段技能市场与技能前端说明（未完全实现）

## 一、文档定位

本说明记录第三阶段“技能市场与选择增强”的当前实现状态。当前版本已经形成本地技能市场与技能前端页面的可运行基础，但第三阶段尚未完全闭环，仍有远程市场接入、完整审核流、端到端安装验收、更多内置技能补齐等工作需要继续推进。

本阶段实现遵循 QIANYUAN 本地化方向：前端与后端均以本地技能市场、现有技能注册器和本地 `SKILL.md` 文件为核心，不引入未验证的远程依赖。

## 二、当前已实现能力

### 1. 技能市场基础数据结构

已新增技能市场相关模型与数据表支撑：

- 技能包：用于按套件组织市场技能。
- 市场技能条目：保存技能名称、描述、分类、标签、触发词、来源和安装状态。
- 已安装技能记录：保存本地安装路径、启停状态、分类、标签和触发词。

相关文件：

- `src/QianYuan.Api/Models/SkillMarketModels.cs`
- `src/QianYuan.Data/Entities/SkillMarket.cs`
- `src/QianYuan.Data/QianYuanDbContext.cs`
- `src/QianYuan.Api/Services/SkillMarketplaceService.cs`

### 2. 技能市场 API

已补充技能市场相关接口，供前端浏览、安装、创建和管理技能使用：

- `GET /api/skills/market`：读取市场技能，支持分类和关键字查询。
- `GET /api/skills/categories`：读取技能分类统计。
- `GET /api/skills/installed`：读取已安装技能。
- `POST /api/skills/install`：安装市场技能。
- `POST /api/skills/create`：创建自定义技能。
- `GET /api/skills/search`：搜索市场技能。
- `DELETE /api/skills/installed/{skillId}`：卸载已安装技能。
- `POST /api/skills/{skillId}/enabled` / `PUT /api/skills/{skillId}/enabled`：启用或停用技能。

相关文件：

- `src/QianYuan.Api/Controllers/CatalogControllers.cs`
- `src/QianYuan.Api/Program.cs`
- `src/QianYuan.Web/src/services/api.ts`
- `src/QianYuan.Web/src/types/api.ts`

### 3. 技能注册与选择增强

已对技能核心模型做扩展，使技能可以参与更细粒度的选择：

- `SkillManifest` 增加分类和触发词字段。
- Markdown 技能加载器支持解析分类、标签和触发词。
- `SkillManager` 增强评分逻辑，触发词命中优先级高于标签和描述。
- 支持技能启停状态进入运行时选择逻辑。

相关文件：

- `src/QianYuan.Core/Abstractions/ISkill.cs`
- `src/QianYuan.Core/Abstractions/ISkillManager.cs`
- `src/QianYuan.Kernel/Skills/MarkdownSkill.cs`
- `src/QianYuan.Kernel/Skills/MarkdownSkillLoader.cs`
- `src/QianYuan.Kernel/Skills/SkillManager.cs`

### 4. 技能前端页面

已将“专家 / 技能 / 连接器”中的“技能”页改为当前目标中的技能前端页面形态，远程市场接入暂缓，优先保证本地可用：

- 顶部搜索框支持技能关键字过滤。
- 提供“我安装的”过滤入口。
- 提供“添加技能”弹窗，可创建本地自定义技能。
- 提供“精选技能”区域，展示可快速添加的本地模板。
- 提供“推荐技能套件”区域，合并后端市场技能与本地精选模板。
- 提供分类 Tab，支持按技能分类过滤。
- 技能卡片提供安装按钮，已安装后显示完成态。
- 页面样式补齐卡片布局、暗色模式和移动端响应式。

相关文件：

- `src/QianYuan.Web/src/components/ExpertMarketplace.tsx`
- `src/QianYuan.Web/src/styles.css`

### 5. 当前本地精选技能模板

当前前端内置了一组本地精选模板，用于在远程市场暂缓接入时保证页面有明确内容和可操作入口：

- `QIANYUAN 爆款封面设计`
- `QIANYUAN 云文件助手`
- `QIANYUAN 问卷设计`
- `NeoData 金融搜索服务`
- `金融数据库查询助手`
- `市场情报搜索`
- `Markdown 文档转换`
- `股票综合诊断`
- `音乐助手`
- `Excel 文件处理`
- `Web Access 浏览器助手`
- `技能创建指南`

这些模板通过前端调用 `POST /api/skills/create` 生成本地自定义技能，不依赖远程市场。

## 三、当前未完全实现内容

第三阶段目前仍未完全完成，主要缺口如下：

### 1. 远程市场接入暂缓

当前版本没有接入远程技能市场。前端展示内容来自后端本地市场数据和前端本地精选模板。后续需要在接口协议、鉴权、错误处理、缓存和降级方案明确后再接入。

### 2. 技能审核流暂未实现

当前创建技能后直接写入本地 `SKILL.md` 并注册，尚未实现审核、发布、下架、版本比对、来源可信校验等流程。

### 3. 安装链路仍需完整端到端验收

当前已完成接口和前端按钮接线，但仍建议继续补充以下验收：

- 前端点击安装市场技能后的真实安装路径检查。
- 前端点击精选模板后的本地技能创建检查。
- 重启后已安装技能恢复检查。
- 技能启停后选择算法是否实时生效检查。
- 卸载技能后文件、数据库记录、锁文件是否一致检查。

### 4. 技能管理页面仍可增强

当前页面完成了主要浏览与创建入口，但仍缺少更完整的管理体验：

- 已安装技能详情页。
- 已安装技能卸载按钮。
- 技能启用/停用开关。
- 创建表单字段校验增强。
- 安装失败后的可恢复提示。
- 批量管理和分类统计可视化。

### 5. 内置技能数量仍需补齐

当前已经具备本地市场种子和前端精选模板，但距离目标中“补充更多关键内置技能”的完整状态仍有差距。后续应继续补充文件搜索、定时任务、数据可视化、自动化编排等高频技能，并补充每个技能的真实 `SKILL.md` 工作流内容。

## 四、本轮前端修正说明

本轮主要修复此前技能前端页面与目标效果不一致的问题：

- 修复点击“技能”后跳转逻辑错误的问题，使其停留在“专家 / 技能 / 连接器”内部的技能页。
- 暂缓远程市场接入，避免页面依赖不稳定外部接口。
- 重建技能页结构，使其包含精选区、推荐区、分类过滤、已安装过滤和添加技能弹窗。
- 修复技能页中文乱码和空态文案。
- 补齐技能页卡片样式、暗色模式和响应式布局。

## 五、验证结果

已完成以下验证：

- 前端构建：`npm run build --prefix src/QianYuan.Web` 通过。
- 技能列表接口：`GET /api/skills` 返回 `200`。
- 技能市场接口：`GET /api/skills/market` 返回 `200`。
- 技能分类接口：`GET /api/skills/categories` 返回 `200`。
- 已安装技能接口：`GET /api/skills/installed` 返回 `200`。
- 代码扫描：未发现受限品牌词残留。
- 代码扫描：未发现暂缓接入的远程市场关键字残留。

## 六、后续建议

建议后续按以下顺序继续推进第三阶段：

1. 先补齐“安装 / 创建 / 卸载 / 启停”的端到端自动化测试。
2. 再完善已安装技能管理 UI，支持卸载和启停。
3. 补充更多真实可用的本地 `SKILL.md` 技能内容。
4. 完善技能版本、来源可信校验和失败回滚机制。
5. 最后再接入远程市场，并保留本地市场作为降级方案。
