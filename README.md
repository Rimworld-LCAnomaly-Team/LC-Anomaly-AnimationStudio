# LC Animation Studio

面向 RimWorld 1.7 的游戏内动画编辑工具。仅维护 1.7，不要求 Anomaly DLC。

从游戏主界面的 Studio 入口选择 Pawn 和动画，编辑关键帧并预览。XML 导出到设置中项目根目录的 `1.7/Defs/AnimationDefs/`，GIF 和联系表输出到 `1.7/Preview/`。已有指向旧版目录的文件映射需要重新选择 1.7 文件。

导出使用原生 `AnimationDef` 的 `keyframeParts` / `workerType` 结构。零缩放关键帧可用于隐藏节点；非有限数值和旧版动画字段仍会被拒绝。

## 构建与验证

安装 .NET 10 SDK。模组位于游戏的 `Mods` 目录时可运行：

```powershell
dotnet build './Source/1.7/LCAnimationStudio/LCAnimationStudio.csproj' -c Release
./Tests/Test-ValidateAnimations.ps1
./Tests/Test-EditSession.ps1
./Tests/Test-ProjectWorkflow.ps1
./Tests/Test-GifEncoder.ps1
```

加载游戏程序集的回归脚本需要运行在 .NET 10 上的 PowerShell（如 PowerShell 7.6）。这些脚本检查编辑撤销/重做、XML 导出读回、项目路径、文件监听冲突及 GIF 编码。

验证系列子模组动画：

```powershell
./Tools/ValidateAnimations.ps1 -ProjectRoot '../LC Anomaly Laetitia' -WarningsAsErrors
```

命令行校验器读取项目 1.7 定义及基础游戏渲染节点标签。非默认安装可传入 `-RimWorldRoot`。完整系列的构建和游戏运行验证见 Core 的 `RIMWORLD_1.7_MIGRATION.md`。
