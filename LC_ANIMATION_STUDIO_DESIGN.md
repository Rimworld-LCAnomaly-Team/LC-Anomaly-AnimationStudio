# LC Animation Studio 设计与实现交接

## 1. 文档目的

本文面向后续负责 LC Anomaly 系列工具开发的 AI 或开发者，描述一套基于 RimWorld 1.6 原生 `PawnRenderTree` / `AnimationDef` 的游戏内实时动画编辑器。

目标工具暂定名为 **LC Animation Studio**。它应当让内容作者在 RimWorld 实际渲染环境中选择 Pawn、浏览渲染节点、拖动时间轴、修改关键帧并直接导出可被子 Mod 加载的标准 XML。

本文是实现交接，不是概念提案。后续 AI 应优先遵守本文的数据格式、模块边界、验收标准和分阶段计划。

## 2. 当前痛点

LC 系列实体使用拆分 PNG、`PawnRenderTreeDef`、`PawnRenderNodeTagDef` 和 `AnimationDef` 组合动画。目前的制作流程主要依赖手写 XML，再离线生成 GIF 检查，存在以下问题：

- 关键帧需要反复修改 XML、启动游戏和观察结果。
- 无法在 RimWorld 的真实 DrawSize、Pivot、父子矩阵和 Shader 环境中拖动预览。
- 难以观察指定 tick，命中 tick 与画面爆发点容易错位。
- 节点标签、持续时间、关键帧顺序和 1.5/1.6 字段差异容易写错。
- 离线预览器只能近似模拟 `PawnRenderTree`，不能作为最终事实来源。
- 每个子 Mod 都重复解决同一类预览、验证和导出问题。

LC Animation Studio 应把 RimWorld 本身变成动画编辑器，导出的 XML 仍然保持原生、可审查、可版本控制。

## 3. 已确认的 RimWorld 1.6 事实

以下类型和字段已经依据 RimWorld 1.6 程序集核对：

- `Verse.AnimationDef`
  - `durationTicks`
  - `startOnRandomTick`
  - `playWhenDowned`
  - `posRotScale`
  - `loopMode`
  - `keyframeParts`
  - `curveParts`
- `Verse.KeyframeAnimationPart`
  - `keyframes`
  - 默认 Worker 为 `AnimationWorker_Keyframes`
- `Verse.AnimationPart`
  - `pivot`
  - `rotationMode`
  - `workerType`
- `Verse.Keyframe`
  - `tick`
  - `offset`
  - `angle`
  - `scale`
  - 可选 `graphicState`
- `PawnRenderer.SetAnimation(AnimationDef)` 用于开始或清除动画。
- `PawnRenderer.CurAnimation` 返回当前动画。
- `PawnRenderTree.AnimationTick` 给出当前循环或钳制后的动画 tick。
- `PawnRenderTree.TryGetNodeByTag` 可按 `PawnRenderNodeTagDef` 查找节点。
- 原版 `Dialog_DebugRenderTree` 已能实时调整节点的 Offset、Pivot、Angle、Scale、Layer 和 Enabled 状态，但没有完整时间轴、关键帧管理和 XML 导出。

### 3.1 RimWorld 1.6 正确 XML 结构

```xml
<AnimationDef>
  <defName>Example_Idle</defName>
  <durationTicks>60</durationTicks>
  <keyframeParts>
    <li>
      <key>Example_Head</key>
      <value>
        <workerType>AnimationWorker_Keyframes</workerType>
        <pivot>(0.5,0.5)</pivot>
        <rotationMode>Relative</rotationMode>
        <keyframes>
          <li>
            <tick>0</tick>
            <offset>(0,0,0)</offset>
            <angle>0</angle>
            <scale>(1,1,1)</scale>
          </li>
          <li>
            <tick>30</tick>
            <offset>(0,0,0.05)</offset>
            <angle>-3</angle>
            <scale>(1,1,1)</scale>
          </li>
          <li>
            <tick>60</tick>
            <offset>(0,0,0)</offset>
            <angle>0</angle>
            <scale>(1,1,1)</scale>
          </li>
        </keyframes>
      </value>
    </li>
  </keyframeParts>
</AnimationDef>
```

不得重新引入 RimWorld 1.5 旧字段：

```xml
<animationParts>  <!-- 错误：1.5 旧格式 -->
<workerClass>     <!-- 错误：1.5 旧格式 -->
```

对应的 1.6 字段必须是：

```xml
<keyframeParts>
<workerType>
```

## 4. 设计原则

### 4.1 游戏内渲染是最终事实来源

预览必须调用真实 `PawnRenderer` / `PawnRenderTree`。不要在编辑器里重新实现一套近似矩阵作为主预览器。

### 4.2 编辑器只在开发环境启用

Studio 应作为开发工具存在，不应成为 LC 子 Mod 的运行时硬依赖。子 Mod 最终只提交原生 XML、节点标签和必要的动画切换代码。

### 4.3 内存编辑与磁盘导出分离

- 拖动滑块时只修改当前编辑会话和内存中的预览对象。
- 用户明确保存时才写 XML。
- 导出必须使用临时文件加原子替换，避免中途产生半截 XML。
- 不应在每一帧或每次滑块变化时写磁盘。

### 4.4 不直接污染原始 Def

加载动画后先复制为 `AnimationEditSession`。只有点击“应用到预览”时才更新内存预览 Def；点击“导出”时才序列化到目标文件。

### 4.5 标准格式优先

导出物必须是 RimWorld 1.6 原生 `AnimationDef`。不要发明只有 LC Core 才能读取的动画格式。

## 5. 推荐部署方式

建议建立独立开发工具项目：

```text
LC-Anomaly-AnimationStudio/
├─ About/
├─ 1.6/
│  ├─ Assemblies/
│  └─ Defs/
└─ Source/1.6/LCAnimationStudio/
```

推荐依赖：

- RimWorld 1.6
- Harmony（仅在确有必要打开原版入口或冻结动画 tick 时使用）
- LC Anomaly Core 1.6（用于识别 LC 收容平台和 HeldPawn；核心编辑能力不得依赖 LC 专有实体）

不建议直接把编辑器程序集塞进每个异常实体子 Mod。独立工具可以服务 Laetitia、Ordeals、Story 及后续实体项目。

## 6. 总体架构

```text
Window_LCAnimationStudio
    |
    +-- PreviewTargetResolver
    |     +-- SelectedPawnTarget
    |     +-- HoldingPlatformTarget
    |     +-- SpawnedPreviewPawnTarget
    |
    +-- AnimationEditSession
    |     +-- EditableAnimation
    |     +-- EditableNodeTrack[]
    |     +-- UndoRedoStack
    |
    +-- RuntimePreviewController
    |     +-- Play / Pause / Scrub
    |     +-- ApplySessionToPreviewDef
    |     +-- ForceRenderTreeDirty
    |
    +-- AnimationXmlSerializer
    |     +-- Import
    |     +-- Validate
    |     +-- ExportAtomic
    |
    +-- ProjectPathResolver
          +-- ExplicitProjectRoot
          +-- DefNameToFileMapping
```

## 7. 核心模块

### 7.1 `Window_LCAnimationStudio`

主窗口建议分为四区：

1. 左侧：PawnRenderTree 节点树。
2. 中央：真实游戏预览和节点 Gizmo。
3. 右侧：当前节点与关键帧属性。
4. 底部：时间轴、轨道和播放控制。

窗口至少提供：

- 目标 Pawn 名称、ThingDef、RenderTreeDef。
- 当前 AnimationDef 和持续 tick。
- Play、Pause、Stop、Loop、Previous Frame、Next Frame。
- 当前 tick 数字输入和时间轴拖动。
- 节点搜索、只显示带 tagDef 的节点、显示所有节点。
- 新建、复制、删除关键帧。
- 一键在当前 tick 写入当前节点变换。
- 撤销、重做。
- 导入 XML、验证、导出 XML。

### 7.2 `PreviewTargetResolver`

必须支持三种目标：

#### 已选择 Pawn

直接读取 `Find.Selector.SelectedPawns`。

#### LC 收容平台 HeldPawn

Laetitia 等实体被放在 `ThingOwner` 内，地图选择器通常只能选中收容平台。Resolver 应检测选中建筑是否为 LC 收容平台，并返回其 `HeldPawn`。

不要为了编辑而把不可逃逸实体释放到地图；部分实体在脱离平台后会立即执行逃逸或死亡逻辑。

#### 独立预览 Pawn

高级阶段可建立不加入地图逻辑的预览 Pawn。该对象只能用于渲染，不能运行 AI、工作、逃逸和伤害逻辑。

MVP 阶段优先完成“已选择 Pawn + 收容平台 HeldPawn”。

### 7.3 `AnimationEditSession`

建议使用与游戏 Def 解耦的可编辑 DTO：

```csharp
public sealed class AnimationEditSession
{
    public string DefName;
    public int DurationTicks;
    public bool StartOnRandomTick;
    public bool PlayWhenDowned;
    public LoopMode LoopMode;
    public float PosRotScale;
    public List<EditableNodeTrack> Tracks;
    public bool IsDirty;
}

public sealed class EditableNodeTrack
{
    public string TagDefName;
    public Vector2 Pivot;
    public RotationMode RotationMode;
    public string WorkerTypeName;
    public List<EditableKeyframe> Keyframes;
}

public sealed class EditableKeyframe
{
    public int Tick;
    public Vector3 Offset;
    public float Angle;
    public Vector3 Scale;
    public string GraphicStateDefName;
}
```

要求：

- 关键帧始终按 tick 升序保存。
- 同一轨道不允许重复 tick。
- tick 必须在 `0..durationTicks` 内。
- Scale 默认值为 `(1,1,1)`，不能错误地默认成 `(0,0,0)`。
- Offset 默认值为 `(0,0,0)`。
- Angle 默认值为 `0`。

### 7.4 `RuntimePreviewController`

编辑器需要能够控制预览 tick，而不能完全依赖 `Find.TickManager.TicksGame` 自动推进。

建议实现顺序：

#### MVP 方案

- 暂停游戏。
- 用户拖动时间轴时，把预览 Def 设置为当前动画。
- 临时调整 `renderTree.animationStartTick = GenTicks.TicksGame - requestedTick`。
- 调用 `renderTree.SetDirty()`。
- 每次拖动都重新绘制。

#### 后续方案

通过最小 Harmony 补丁为 Studio 目标 Pawn 注入“编辑器指定 tick”，其他 Pawn 仍使用原版 `AnimationTick`。

必须限定补丁范围：

- 仅当 Studio 窗口打开。
- 仅对当前 PreviewTarget 生效。
- 窗口关闭时立即恢复原版行为。

### 7.5 `AnimationXmlSerializer`

职责：

- 从标准 1.6 XML 读取 `AnimationDef`。
- 把 tagDef 名称解析为编辑轨道。
- 保留未被编辑器理解但合法的字段，或在导入时明确警告。
- 以稳定顺序输出 XML，减少 Git 噪音。
- 输出前执行完整验证。

稳定排序建议：

1. 动画按文件中原有顺序。
2. 轨道按 RenderTree DFS 顺序；无法匹配的轨道按 tagDef 名称排序。
3. 关键帧按 tick 排序。
4. 字段顺序固定为 tick、offset、angle、scale、graphicState。

若值等于默认值，可以配置“精简导出”或“完整导出”。LC 项目默认推荐完整导出，便于 AI 和人工审查。

### 7.6 `AnimationSchemaValidator`

必须在导入、应用和导出三个阶段运行。

至少检查：

- 禁止 `animationParts`。
- 禁止 `workerClass`。
- 必须使用 `keyframeParts` 或合法的 `curveParts`。
- Worker 字段为 `workerType`。
- `defName` 非空且在目标文件内唯一。
- `durationTicks > 0`。
- tagDef 存在于 `DefDatabase<PawnRenderNodeTagDef>`。
- 目标 RenderTree 中能够找到对应 tagDef；找不到时显示警告而不是静默忽略。
- 轨道包含至少一个关键帧。
- tick 严格递增并处于合法范围。
- Vector 格式和数值有限，不允许 NaN/Infinity。
- Scale 不允许任一可见轴为 0，除非用户明确启用“允许隐藏节点”。
- `loopMode=End` / `Clamp` 时给出播放语义提示。

### 7.7 `ProjectPathResolver`

编辑器不能猜测任意磁盘路径并直接写入。

推荐方式：

- 在 Mod Settings 中明确配置“目标项目根目录”。
- 用户首次导出时选择具体 XML。
- 保存 `defName -> XML absolute path` 映射。
- 每次写入前确认路径仍位于已授权项目根目录内。
- 默认只允许写入 `1.6/Defs/AnimationDefs/`。
- 禁止写入 `1.5/` 和 `Source/1.5/`。

## 8. 编辑交互细节

### 8.1 时间轴

时间轴以 RimWorld tick 为基本单位，不以秒或帧作为存储单位。

- 显示 `tick / durationTicks`。
- 可选显示秒：`tick / 60f`。
- 支持缩放和平移。
- 每个节点一条轨道。
- 关键帧用可拖动菱形显示。
- 命中、声音、代码事件 tick 可用 Marker 显示，但 Marker 不写入原生 AnimationDef；它们来自项目侧元数据或手工注释。

### 8.2 机械停顿的制作

RimWorld `AnimationWorker_Keyframes` 在两个关键帧之间线性插值。若需要原版脑叶公司式的“保持后瞬间跳变”，应创建相邻 tick：

```text
tick 10: 旧姿态
tick 11: 新姿态
tick 20: 新姿态
tick 21: 下一姿态
```

Studio 应提供按钮：

- `Insert Hold Before`
- `Insert Hold After`
- `Make One-Tick Snap`
- `Duplicate Pose To Tick`

### 8.3 坐标约定

- Offset X：屏幕/地图平面左右方向。
- Offset Y：高度层相关分量，通常不作为平面位移编辑。
- Offset Z：屏幕/地图平面前后方向，在南向预览中通常表现为上下移动。
- Angle：节点旋转角度。
- Scale：使用 `Vector3`，平面动画通常修改 X/Z，Y 保持 1。
- Pivot：0..1 归一化纹理坐标。
- RotationMode：必须可编辑并显示其对不同朝向的影响。

不要在序列化层把 Unity 屏幕 Y 坐标直接写成 RimWorld Offset Z；转换只能发生在 Gizmo 输入层。

## 9. 导入、编辑、导出流程

### 9.1 导入

1. 选择 Pawn 或 LC 收容平台。
2. 打开 Studio。
3. 从 `DefDatabase<AnimationDef>` 选择已加载动画，或从 XML 导入。
4. Validator 检查字段、tagDef 和关键帧。
5. 创建独立 `AnimationEditSession`。
6. 生成临时 Preview AnimationDef。

### 9.2 编辑

1. 暂停预览。
2. 拖动到目标 tick。
3. 选择 RenderNode。
4. 用滑块、数字框或场景 Gizmo 调整 Offset/Angle/Scale/Pivot。
5. 点击“写入关键帧”。
6. 播放局部区间确认节奏。

### 9.3 导出

1. 运行 Validator。
2. 显示目标文件与 Def 列表。
3. 创建 `.tmp` 文件。
4. 写入并重新解析 `.tmp`。
5. 解析成功后原子替换目标 XML。
6. 重新读取目标文件，确认关键字段与帧数一致。
7. 在 UI 中显示 Git 友好的变更摘要。

## 10. 热重载策略

### 10.1 首选：内存会话即时预览

编辑期间不依赖 RimWorld Def 热重载。Studio 创建临时 `AnimationDef`，更新其 `keyframeParts`，然后对 Preview Pawn 调用：

```csharp
pawn.Drawer.renderer.SetAnimation(previewAnimationDef);
pawn.Drawer.renderer.renderTree.SetDirty();
```

这是最可靠的实时反馈路径。

### 10.2 文件变化重导入

可选使用 `FileSystemWatcher` 观察目标 XML：

- 防抖 300~500 ms。
- 文件稳定且可完整解析后才重导入。
- 当前会话有未保存修改时，不自动覆盖；显示冲突提示。
- 不要尝试重新加载整个 Mod 或重建所有 DefDatabase。

### 10.3 游戏 Def 替换注意事项

不要轻易从 `DefDatabase` 删除和重新添加已有 Def。其他代码、DefOf 和对象可能已经持有旧引用。

预览阶段应使用临时 Def；正式内容仍在下次正常启动时由 RimWorld 加载。

## 11. 与 LC Core 的集成点

建议 LC Core 只提供可选适配接口，不把编辑器 UI 编进运行时 Core：

```csharp
public interface IAnimationStudioTargetProvider
{
    bool TryGetPreviewPawn(Thing selectedThing, out Pawn pawn);
    string GetSuggestedAnimationDirectory(Pawn pawn);
}
```

LC Core 适配器负责：

- 从 `Building_AbnormalityHoldingPlatform` 获取 `HeldPawn`。
- 暂时抑制 Studio Preview Pawn 的逃逸、死亡、AI 和伤害逻辑。
- 暴露研究开始、命中、生成、死亡等事件 Marker。

Studio 主体仍应能编辑普通 RimWorld Pawn。

## 12. 文件与类布局建议

```text
Source/1.6/LCAnimationStudio/
├─ Bootstrap/
│  └─ LCAnimationStudioMod.cs
├─ UI/
│  ├─ Window_LCAnimationStudio.cs
│  ├─ TimelineView.cs
│  ├─ RenderTreeView.cs
│  ├─ KeyframeInspector.cs
│  └─ PreviewToolbar.cs
├─ Model/
│  ├─ AnimationEditSession.cs
│  ├─ EditableNodeTrack.cs
│  ├─ EditableKeyframe.cs
│  └─ UndoRedoStack.cs
├─ Runtime/
│  ├─ PreviewTargetResolver.cs
│  ├─ RuntimePreviewController.cs
│  └─ PreviewAnimationFactory.cs
├─ Serialization/
│  ├─ AnimationXmlSerializer.cs
│  ├─ AnimationSchemaValidator.cs
│  └─ AtomicFileWriter.cs
├─ Integration/
│  ├─ VanillaTargetProvider.cs
│  └─ LCAnomalyTargetProvider.cs
└─ Harmony/
   └─ AnimationTickOverridePatch.cs
```

## 13. 分阶段实现计划

> 实现状态（2026-07-16）：Phase 0 至 Phase 4 均已落地到 `Source/1.6/LCAnimationStudio/`。其中事件 Marker 使用 `.lcas.xml` 项目旁车文件，不写入原生 `AnimationDef`；GIF 与 PNG Contact Sheet 输出到授权项目的 `1.6/Preview/`。

### Phase 0：格式验证器（已实现）

先实现命令行或游戏内验证按钮：

- 扫描 `1.6/Defs/AnimationDefs/*.xml`。
- 阻止 1.5 字段。
- 校验节点标签和关键帧。
- 输出精确到 Def、节点和 tick 的错误。

这是后续所有阶段的基础。

### Phase 1：游戏内预览 MVP（已实现）

- 从选中 Pawn 或 LC 平台取得目标。
- 展示 RenderTree。
- 选择已加载 AnimationDef。
- Play、Pause、Scrub。
- 显示当前 tick。
- 修改当前节点 Debug Offset/Angle/Scale/Pivot。

此阶段可以复用或参考原版 `Dialog_DebugRenderTree` 的节点树与属性面板设计。

### Phase 2：关键帧编辑与 XML 导出（已实现）

- `AnimationEditSession`。
- 新建、修改、删除关键帧。
- Undo/Redo。
- 标准 1.6 XML 导入导出。
- 原子写入和重新解析验证。

### Phase 3：场景 Gizmo 与事件 Marker（已实现）

- 画面内平移、旋转、缩放手柄。
- 命中 tick、声音 tick、代码事件 Marker。
- 区间循环播放。
- 多节点姿态复制。

### Phase 4：跨项目工作流（已实现）

- 项目根目录设置。
- Def 到文件映射。
- 文件监视与冲突处理。
- 批量验证所有 LC 子 Mod。
- 导出 GIF/PNG Contact Sheet 作为 PR 附件，但不把离线图当作运行时验证替代品。

## 14. MVP 验收标准

MVP 完成必须同时满足：

- 能从 LC 收容平台选中 HeldPawn。
- 能选择 `Laetitia_Idle` 并实时播放。
- 能暂停到指定 tick。
- 能选择 `Laetitia_Head`，修改 Angle，并立即看到真实游戏渲染变化。
- 能在当前 tick 新增关键帧。
- 能导出使用 `keyframeParts` / `workerType` 的 RimWorld 1.6 XML。
- 导出的 XML 重启游戏后能够正常加载。
- 导出前后所有轨道数、关键帧数和关键帧值一致。
- 不修改任何 `1.5/` 或 `Source/1.5/` 文件。

## 15. 自动化验证建议

在 LC Core 或 Studio 仓库提供 PowerShell 验证脚本：

```text
Tools/ValidateAnimations.ps1
```

脚本至少执行：

1. XML 语法解析。
2. 搜索旧字段 `animationParts`、`workerClass`。
3. 验证每个动画的 `durationTicks`。
4. 验证 keyframe tick 严格递增。
5. 验证 tick 不超过持续时间。
6. 验证 tagDef 已定义。
7. 验证 tagDef 出现在目标 RenderTree 中。
8. 验证 C# `AnimationDefOf` 引用的 Def 存在。
9. 输出可供 CI 解析的非零退出码。

## 16. 风险与规避

### 风险：编辑器与真实渲染矩阵不一致

规避：只使用真实 PawnRenderer 绘制；Gizmo 负责输入，不自行重建最终画面。

### 风险：直接修改 DefDatabase 导致旧引用失效

规避：使用临时 Preview AnimationDef；正式 Def 在正常加载流程中更新。

### 风险：被收容实体无法直接选中

规避：实现 HoldingPlatformTarget，从选中平台解析 HeldPawn。

### 风险：编辑状态触发实体逃逸或死亡

规避：优先编辑平台内 HeldPawn；独立预览 Pawn 必须禁用游戏逻辑。

### 风险：文件监视覆盖未保存内容

规避：会话 Dirty 时只提示冲突，不自动重载。

### 风险：导出产生大量无意义 Git diff

规避：固定字段顺序、轨道顺序、数值格式和缩进。

### 风险：工具成为子 Mod 的强制依赖

规避：工具独立发布；子 Mod 只提交原生 XML。

## 17. 推荐的第一批开发任务

后续 AI 可以按以下顺序直接开始：

1. 新建独立 `LC-Anomaly-AnimationStudio` 仓库或开发工具目录。
2. 实现 `AnimationSchemaValidator` 和 `ValidateAnimations.ps1`。
3. 复制原版 `Dialog_DebugRenderTree` 的交互思路，避免复制受版权限制的大段源码。
4. 实现 `PreviewTargetResolver`，首先支持普通 Pawn 和 LC 平台 HeldPawn。
5. 实现暂停和 Scrub：调整当前 Preview Pawn 的 `animationStartTick`。
6. 实现 `AnimationEditSession` 和临时 Preview AnimationDef。
7. 实现关键帧 CRUD 和 Undo/Redo。
8. 实现标准 1.6 XML 导入导出。
9. 使用 Laetitia 的 `Idle`、`Work`、`GiveGift` 三套动画作为首个端到端验收样例。
10. 再用 LaetitiaMonster 的攻击命中 tick 验证事件 Marker 和单 tick 停顿功能。

## 18. 当前 Laetitia 项目基线

本项目可作为 Studio 的首个测试素材：

- `1.6/Defs/AnimationDefs/Laetitia.xml`
  - `Laetitia_Idle`
  - `Laetitia_Work`
  - `Laetitia_GiveGift`
- `1.6/Defs/AnimationDefs/LaetitiaMonster.xml`
  - Walk、Idle、AttackR、AttackL、Spawn、Dead
- `1.6/Defs/PawnRenderTreeDefs/LaetitiaRenderTreeDefs.xml`
- `1.6/Defs/PawnRenderTreeDefs/LaetitiaMonsterRenderTreeDefs.xml`
- `1.6/Defs/PawnRenderTreeDefs/PawnRenderNodeTagDefs.xml`

这些动画应全部保持 RimWorld 1.6 原生 `keyframeParts` / `workerType` 格式，并纳入 Studio Validator 的回归样例。

