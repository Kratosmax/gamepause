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

最后校准：2026-08-21（Asia/Shanghai）

| 项目 | 已核实状态 |
| --- | --- |
| 仓库 | `git@github.com:Kratosmax/gamepause.git` |
| 默认分支 | `main` |
| 当前版本 | `1.2.3`（准备发布） |
| 本地最新标签 | `v1.2.2`；待创建 `v1.2.3` |
| 产品代码基线 | `v1.2.2` 提交 `909bddc3bee7af94992f056e43ce6bf3cc8b717f`；当前工作树含 1.2.3 休眠检测改动 |
| Git 身份 | 仓库级 `小火车 <kratosthemax@gmail.com>` |
| 技术栈 | .NET 8、WPF、Windows 10/11 x64 |
| 线上状态 | Actions `32440356615` 成功；`v1.2.2` 为 latest 正式版且 8 个资产、签名、哈希、公开路由和 `1.2.1 -> 1.2.2` 更新链已核验；`v1.2.0` 保留为预发布审计记录 |

实机日志确认旧版冻结 `douyin.exe` 时会连带暂停 `douyin_tray.exe` 和 Windows App Runtime 的 `DynamicDependencyLifetimeManagerShadow.exe`。已发布后撤回的 `1.2.0` 排除了这两个节点及其整个后代分支，并增加任务栏连续无响应自动恢复、跨进程暂停/恢复互斥、可开关 Debug 日志和按需管理员权限。真实旧客户端更新链随后发现旧更新器会锁住自身已加载的 DLL，导致安装和回滚失败；`1.2.1` 改用“原子移走旧文件，再写入新文件”的替换方式，并由新版主程序在更新器退出后完成 apphost 交接和备份清理。没有加入抖音阻止规则或红色 UI 状态。

### 本轮验证记录

- 2026-08-21：`dotnet restore GamePause.sln --configfile NuGet.Config` 通过。
- 2026-08-21：`1.2.1` Release 构建通过，0 个警告、0 个错误；核心测试 29/29 通过；源码与打包后更新器安装、锁文件回滚和路径穿越自测通过。
- 2026-08-21：设置持久化测试通过；WPF 主窗口、设置页、更新提示和提权弹窗截图通过人工检查，纯黑截图门禁通过。
- 2026-08-21：`scripts/publish.ps1` 成功生成 `1.2.1` 的 Full Setup、Lite Setup、Full ZIP、Lite ZIP；版本、运行时包含关系和 `full`/`lite` 通道标记已核对。
- 2026-08-21：新 `1.2.1` 更新器使用线上已签名的 `1.2.0` Lite 包完成真实锁文件替换，退出码为 0，`GamePause.Updater.next` 完成交接；独立启动收尾测试确认 `1.2.1` 会校验替换 updater apphost 并清理备份目录。
- 2026-08-21：从 GitHub Release 下载原始 `1.1.4` Full/Lite 包，旧客户端自身的包预检逻辑均接受对应的本地 `1.2.0` 候选 ZIP，但旧更新器在实际替换自身 DLL 时失败；因此不能宣称这些版本可自动过渡到 `1.2.1`。
- 2026-08-21：GitHub Actions `32400575499` 成功发布 `v1.2.1`。线上 4 个发行包、3 个清单和 `SHA256SUMS.txt` 均存在；4 个包哈希、3 个 RSA 签名、Full/Lite 通道、ZIP 结构、包内版本和 latest 路由全部通过独立核验。
- 2026-08-21：线上正式 `1.2.1` Lite 包使用包内更新器完成自身更新整链，退出码为 0；重启后主程序与更新器版本均为 `1.2.1+32c53d4`，`.next` 和备份目录均已清理。
- 2026-08-21：`1.2.2` 候选版更新窗口改为版本公告与下载共用的可调整窗口，显示真实字节百分比、大小、当前 URL、线路序号及哈希/版本/通道/结构校验阶段，并支持取消和清理临时文件；校验完成前下载进度最多显示 99%。Release 构建 0 警告、核心测试 29/29、源码与打包后更新器自测、设置/进度/取消测试和 560x460、500x410 WPF 截图通过。
- 2026-08-21：GitHub Actions `32440356615` 成功发布 `v1.2.2`。线上 4 个发行包、3 个清单和 `SHA256SUMS.txt` 共 8 个资产；4 个包哈希、3 个 RSA 签名、Full/Lite 通道、ZIP 结构、包内版本、latest API 和三个公开清单路由均通过独立核验。线上原始 `1.2.1` Lite 更新器完成正式 `1.2.2` 更新链，退出码为 0；主程序与更新器均为 `1.2.2+909bddc`，`.next` 和备份目录均已清理。
- 尚未对修复后的正式包再次执行真实抖音暂停测试；不同抖音版本的进程结构仍需观察。

## 当前产品状态

- 主程序支持多选暂停/恢复、普通与深度暂停、紧急全部恢复、前台筛选和搜索。
- 支持游戏档案、按游戏自动规则、托盘控制、全局快捷键、开机静默启动和兼容性提示。
- 暂停状态写入 `%LocalAppData%\GamePause`，主程序重启和 Watchdog 会尝试恢复；PID 与启动时间用于防止 PID 复用误操作。
- 默认普通权限启动并限制单实例；用户主动暂停遇到拒绝访问时才询问提权，自动暂停不会弹 UAC。关闭窗口默认进入托盘，退出时若仍有暂停目标会询问恢复。
- 安全策略排除抖音托盘、Windows App Runtime DDLM 及其后代分支；稳定暂停期间 Watchdog 连续检测到任务栏无响应会自动恢复。
- 设置可开关 Debug 模式；详细进程树、父子 PID、路径和安全排除详情仅在开启后记录。
- 自动更新支持签名清单、SHA-256、包内版本、通道和 ZIP 路径校验、失败回滚、下载上限与停滞超时；`1.2.1` 起可替换更新器自身已加载的 DLL，并在新版启动后清理备份。
- Full/Lite 使用独立更新通道。`1.1.4` 修复了旧版从 ZIP 条目读取程序集版本时要求流可 Seek 的问题。
- `0.9.1` 至 `1.2.0` 均必须手动安装一次 `1.2.1`：前者部分版本卡在包预检，后者卡在旧更新器锁文件。README 和 `docs/releases/v1.2.1.md` 已记录该过渡方案。

## 当前待办

当前已获用户授权发布 `1.2.3`，包含启动时检测并隐藏未启用的系统休眠入口。待完成本地四产物验证、提交、推送标签、GitHub Actions 和线上 Release 核验。

其他已知后续工作：

- 在真实 Windows 10/11 设备上验证 UI、管理员启动、托盘、开机任务、安装/卸载和自动更新。
- 真机验证目标游戏：地府有点忙、多少兄弟？、千棋百计、黑神话：悟空、幻兽帕鲁。
- 根据真机日志补充兼容性规则和回归测试；幻兽帕鲁仅面向单人或本地主机场景。

下一位 Codex 应等待用户给出具体需求，不要把上述验证事项擅自标记为完成，也不要自行发布。

## 关键入口

| 范围 | 文件 |
| --- | --- |
| 程序启动、按需提权、单实例 | `src/GamePause.App/Program.cs`、`ElevationService.cs` |
| 主窗口和主要交互 | `src/GamePause.App/MainWindow.xaml`、`MainWindow.xaml.cs` |
| 进程暂停与恢复 | `src/GamePause.Core/ProcessSuspensionService.cs`、`NativeProcessApi.cs` |
| 恢复记录、跨进程互斥与日志 | `src/GamePause.Core/SessionStore.cs`、`DiagnosticLog.cs` |
| 安全与兼容性 | `src/GamePause.Core/SafetyPolicy.cs`、`GameProfiles.cs` |
| 设置、快捷键和代理 | `HotkeySettingsWindow.*`、`HotkeySettings.cs`、`UiSettings.cs` |
| 游戏档案 | `GameProfileWindow.*`、`GameProfileStore.cs` |
| 更新检查与安装 | `UpdateService.cs`、`src/GamePause.Updater/Program.cs` |
| 异常与 Shell 卡顿恢复守护 | `src/GamePause.Watchdog/Program.cs`、`ShellHealthProbe.cs`、`WatchdogLauncher.cs` |
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
