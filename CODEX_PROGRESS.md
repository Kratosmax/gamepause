# Codex 项目接续记录

> 新会话从这里开始。先完整读取本文件，再执行“接手校准”；完成任务前按“维护规则”更新本文件。实时 Git、源码和测试结果高于本文件中的历史快照。

## 接手校准

在仓库根目录执行以下只读检查，不需要先询问用户：

```powershell
git status --short
git branch --show-current
git log -5 --oneline
git remote -v
git describe --tags --always --dirty
```

然后核对：

1. `src/GamePause.App/GamePause.App.csproj`、Updater/Watchdog 项目及 `scripts/publish.ps1` 中的版本是否一致。
2. README、`.github/workflows/release.yml`、最新发布说明和四种产物规则是否一致。
3. 本文件“当前快照”是否仍匹配 Git 与源码；不匹配时先按证据修正，不要沿用旧结论。
4. 与需求有关的源码、调用方和测试必须重新读取；本文件只提供导航，不替代代码证据。

## 当前快照

最后校准：2026-08-20（Asia/Shanghai）

| 项目 | 已核实状态 |
| --- | --- |
| 仓库 | `git@github.com:Kratosmax/gamepause.git` |
| 默认分支 | `main` |
| 当前版本 | `1.1.4` |
| 本地最新标签 | `v1.1.4` |
| 产品代码基线 | `16e967c659bbedf2f86e251ef761e0a8347ab3c0`（`v1.1.4`，本轮未改产品代码） |
| Git 身份 | 仓库级 `小火车 <kratosthemax@gmail.com>` |
| 技术栈 | .NET 8、WPF、Windows 10/11 x64 |
| 线上状态 | 本轮未联网复核 Actions 与 Release，不得仅凭本表宣称线上正常 |

本轮工作是建立跨会话接续机制。仓库内新增本文件并更新 README 的 AI 接手入口；用户已授权同步 README、提交并推送到 `origin/main`，不包含标签或 Release。跨设备是否已经可用应以远程分支和本地同步状态为准，不能只看本段文字。个人 skill 的同步不属于本仓库依赖。

### 本轮验证记录

- 2026-08-20：`git diff --check` 通过，12 个文档引用的仓库路径均存在。
- 2026-08-20：`desktop-tool-ui-release` skill 结构校验通过。
- 本轮只修改文档和个人 skill，没有修改产品代码，因此未运行产品编译、核心测试、更新器自测或 UI 视觉验收。

## 当前产品状态

- 主程序支持多选暂停/恢复、普通与深度暂停、紧急全部恢复、前台筛选和搜索。
- 支持游戏档案、按游戏自动规则、托盘控制、全局快捷键、开机静默启动和兼容性提示。
- 暂停状态写入 `%LocalAppData%\GamePause`，主程序重启和 Watchdog 会尝试恢复；PID 与启动时间用于防止 PID 复用误操作。
- 启动时要求管理员权限并限制单实例；关闭窗口默认进入托盘，退出时若仍有暂停目标会询问恢复。
- 自动更新支持签名清单、SHA-256、包内版本、通道和 ZIP 路径校验、失败回滚、下载上限与停滞超时。
- Full/Lite 使用独立更新通道。`1.1.4` 修复了旧版从 ZIP 条目读取程序集版本时要求流可 Seek 的问题。
- `0.9.1` 至 `1.1.3` 无法自动升级到 `1.1.4`，需要手动安装一次；README 和 `docs/releases/v1.1.4.md` 已记录该过渡方案。

## 当前待办

当前没有已授权但未完成的产品代码任务。已知后续工作：

- 在真实 Windows 10/11 设备上验证 UI、管理员启动、托盘、开机任务、安装/卸载和自动更新。
- 真机验证目标游戏：地府有点忙、多少兄弟？、千棋百计、黑神话：悟空、幻兽帕鲁。
- 根据真机日志补充兼容性规则和回归测试；幻兽帕鲁仅面向单人或本地主机场景。

下一位 Codex 应等待用户给出具体需求，不要把上述验证事项擅自标记为完成，也不要自行发布。

## 关键入口

| 范围 | 文件 |
| --- | --- |
| 程序启动、提权、单实例 | `src/GamePause.App/Program.cs`、`ElevationService.cs` |
| 主窗口和主要交互 | `src/GamePause.App/MainWindow.xaml`、`MainWindow.xaml.cs` |
| 进程暂停与恢复 | `src/GamePause.Core/ProcessSuspensionService.cs`、`NativeProcessApi.cs` |
| 恢复记录与日志 | `src/GamePause.Core/SessionStore.cs`、`DiagnosticLog.cs` |
| 安全与兼容性 | `src/GamePause.Core/SafetyPolicy.cs`、`GameProfiles.cs` |
| 设置、快捷键和代理 | `HotkeySettingsWindow.*`、`HotkeySettings.cs`、`UiSettings.cs` |
| 游戏档案 | `GameProfileWindow.*`、`GameProfileStore.cs` |
| 更新检查与安装 | `UpdateService.cs`、`src/GamePause.Updater/Program.cs` |
| 异常恢复守护 | `src/GamePause.Watchdog/Program.cs`、`WatchdogLauncher.cs` |
| 核心回归测试 | `tests/GamePause.CoreTests/Program.cs` |
| 打包、清单和发布 | `scripts/publish.ps1`、`scripts/New-UpdateManifest.ps1`、`.github/workflows/release.yml` |
| 安装器 | `installer/GamePause.iss` |

用户数据文件与用途详见 README 的“本地数据”。不要把真实用户数据、日志、代理凭据或暂停记录复制进仓库。

## 不得破坏的约束

- 不移除系统关键进程和反作弊保护，不支持绕过反作弊，不对在线竞技游戏做真实暂停测试。
- 暂停路径必须有恢复路径，并处理崩溃、PID 复用、重复启动和磁盘写入中断。
- 更新功能必须保留清单签名、下载后二次哈希、包内版本、通道、包结构、路径穿越防护和失败回滚。
- 正式私钥不得进入源码、日志、进度文件、构建产物或 Release；仓库只保存公钥。
- 保持 Windows 10 清晰回退和 Windows 11 背景材质；UI 改动必须检查真实截图、长文本、缩放和选中态。
- 默认发行四种版本：Full Setup、Lite Setup、Full ZIP、Lite ZIP。条件不满足时先给证据并询问，不得静默少发。
- 不自行提交、推送、打标签或发布。用户说“更新”时，本项目约定为同步 README、提交并推送；用户说“发布”才包含标签与 Release，不能把二者混用。

## 验证矩阵

普通代码改动至少执行：

```powershell
dotnet restore GamePause.sln --configfile NuGet.Config
dotnet build GamePause.sln --configuration Release --no-restore
dotnet run --project tests\GamePause.CoreTests\GamePause.CoreTests.csproj --configuration Release --no-build
dotnet src\GamePause.Updater\bin\Release\net8.0-windows\GamePause.Updater.dll --self-test
```

UI 或设置改动再执行：

```powershell
dotnet run --project temp\visual-qa\VisualQa.csproj --configuration Release -- --settings-test
dotnet run --project temp\visual-qa\VisualQa.csproj --configuration Release
```

发布、安装器或更新改动再执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
dotnet .\temp\release\GamePause-<版本>-Lite\GamePause.Updater.dll --self-test
```

发布前确认四个包、三个更新清单和 `SHA256SUMS.txt` 均存在。不要把以前会话的测试结果写成当前任务已通过；每条结果记录命令、日期和成功/失败。

## 协作与发布约定

- 默认 GitHub 所有者：`Kratosmax`；个人主页：<https://github.com/Kratosmax>。
- 只使用仓库级 Git 身份 `小火车 <kratosthemax@gmail.com>`，禁止修改全局 Git 身份。
- 新仓库默认公开，但创建仓库、首次推送和发布仍是独立授权。
- 本项目每次发布默认提供四种版本。本地日常验证优先 Lite，正式发布必须验证全部资产。
- README 必须持续包含使用教程、自行编译和 AI 接续开发引导，并随版本、命令、下载矩阵和已知限制同步更新。

## 维护规则

每次任务结束前更新本文件，但只记录能帮助下一会话继续工作的事实：

1. 更新“最后校准”、版本、分支、产品代码基线、线上核验状态和当前工作；不要在同一提交内记录会被该提交自身改变的“当前提交哈希”。
2. 把完成事项从“当前待办”移除，新增可执行的下一步、阻塞原因和所需授权。
3. 记录本次修改文件、已运行的测试及结果；未运行就明确写“未运行”。仓库外的个人 skill 只记录是否影响项目流程，不写设备专属路径。
4. 架构、关键入口、安全约束、发布命令或用户约定改变时同步更新对应稳定章节和 README。
5. 只保留当前有效状态，不堆叠聊天流水账；历史发布变化写入 `docs/releases/` 和 Git。
6. 不写密钥、令牌、用户数据、临时文件、设备专属绝对路径或无法由仓库复现的环境细节。
7. 提交前运行 `git diff --check` 和 `git status --short`，确认本文件与真实差异一致。

当本文件与 Git、源码、测试或用户当前指示冲突时，以较新的可验证证据和用户当前指示为准，并立即修正文档。
