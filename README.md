# Game Pause

Game Pause 是一个面向 Windows 10/11 的游戏进程暂停工具，当前版本为 **0.9.1**。它可以批量暂停和恢复进程树，并提供游戏档案、自动规则、全局快捷键、托盘控制、异常恢复记录和独立守护进程。

本项目只面向单机游戏或明确可暂停的本地进程，不注入游戏、不修改游戏内存数据，也不尝试绕过反作弊。

## 使用说明

### 系统要求

- Windows 10 或 Windows 11，64 位。
- 主程序需要管理员权限，以操作同等或较低权限的目标进程。
- Windows 11 22H2 及以上可显示完整系统背景材质；Windows 10 自动使用浅色回退主题。

### 启动程序

1. 解压完整发布包，保持 `GamePause.exe`、`GamePause.Watchdog.exe` 和 `GamePause.Updater.exe` 位于同一目录。
2. 双击 `GamePause.exe`，在系统提示时允许管理员权限。
3. 程序只允许一个实例运行。重复启动时会提示检查系统托盘并退出。

关闭主窗口不会退出程序，而是转入系统托盘。第一次关闭时会提示一次，后续静默进入托盘。需要彻底退出时，请使用托盘菜单中的退出命令。

### 暂停与恢复程序

1. 在“运行中”列表里勾选一个或多个进程。可以按进程名、窗口标题或 PID 搜索，也可以只显示当前前台进程。
2. 点击普通暂停或深度暂停，程序会依次暂停所有已勾选目标的进程树。
3. 在“已暂停”列表勾选一个或多个目标，再点击恢复所选。
4. 遇到异常时，点击“紧急全部恢复”，恢复本程序记录的全部目标。

进程树暂停顺序为先子进程、后主进程；恢复顺序为先主进程、后子进程。长进程名和窗口标题会省略显示，悬停可查看完整内容。

### 前台进程与搜索

- 程序会记住最近一个非 Game Pause 的前台窗口，并在列表中置顶、高亮。
- “仅显示前台”可快速筛选当前游戏。
- 前台捕获提供 3 秒倒计时，便于先切回游戏，再自动将它加入勾选。

### 游戏档案与自动规则

- 可从一个或多个已勾选进程创建游戏档案。批量创建时使用默认设置，之后可分别编辑。
- 档案记录显示名称、EXE 路径、暂停模式和自动规则；路径失效时会回退到进程名匹配。
- 每个档案可单独启用“失去前台后延迟暂停”和“回到前台后恢复”，延迟范围为 3 到 300 秒，默认关闭。
- 托盘菜单会动态列出游戏收藏和已暂停目标，可直接暂停、恢复并查看暂停时长。

不要为联机服务器游戏启用自动暂停规则。客户端暂停时服务器世界仍会继续运行，恢复后可能已经断线。

### 快捷键、开机启动和设置

统一设置窗口提供：

- 自定义暂停/恢复快捷键和紧急恢复快捷键；保存前通过 Windows 全局热键注册结果检查冲突。
- 开机启动及静默进入托盘。启用后会创建最高权限的 Windows 登录计划任务 `GamePause.AutoStart`。
- 当前版本号、后台自动检查更新和手动检查更新入口。

启用开机启动后不要移动程序目录；如需移动，请先关闭开机启动，移动后再重新启用。

### 深度暂停与整机休眠

- 普通暂停：冻结目标进程树。
- 深度暂停：先冻结进程树，再请求 Windows 回收其物理工作集。它不是内存快照，不能跨 Windows 重启恢复。
- 整机休眠：调用 Windows 原生休眠，将整台电脑状态保存到 `hiberfil.sys`。此操作影响所有程序，并要求系统已启用休眠且磁盘空间充足。

深度暂停和整机休眠都需要二次确认。它们不能避免在线游戏超时或服务器断线。

### 异常退出保护

当前暂停状态会写入恢复记录。程序再次启动时会核验 PID、进程启动时间和线程挂起计数，避免因 PID 复用而恢复错误进程。主程序异常退出后，独立守护进程也会读取记录并尝试恢复。

有目标仍处于暂停状态时退出程序，会询问是否全部恢复；拒绝恢复会取消退出。

### 本地数据

用户数据位于 `%LocalAppData%\GamePause`：

| 文件或目录 | 用途 |
| --- | --- |
| `profiles.json` | 游戏收藏、路径、暂停模式和自动规则 |
| `settings.json` | 自定义快捷键 |
| `ui-settings.json` | 关闭到托盘提醒、跳过的更新版本等界面偏好 |
| `active-session.json` | 当前暂停目标的恢复记录，全部恢复后删除 |
| `active-session.json.tmp` | 恢复记录写入过程中的临时文件 |
| `active-session.json.bak` | 恢复记录的冗余备份 |
| `game-pause.log` | 主程序运行与恢复日志 |
| `update.log` | 独立更新器日志 |
| `updates\` | 已下载并校验的更新临时文件 |

开机启动配置不在 JSON 文件中，而在 Windows 任务计划程序的 `GamePause.AutoStart` 任务中。

### 安全边界与已知限制

- 已知系统关键进程和反作弊组件会被阻止，但兼容性检测不是绝对保证。
- 不支持绕过反作弊；不要尝试暂停在线竞技游戏或反作弊服务。
- 当前只恢复由本程序暂停并记录的进程，不扫描其他工具暂停的目标。
- 深度暂停不保证全部内存立即写入页面文件，也不是 Steam Deck/整机休眠式的跨重启快照。
- 首批目标游戏仍需真机验证：地府有点忙、多少兄弟？、千棋百计、黑神话：悟空、幻兽帕鲁。幻兽帕鲁仅计划支持单人或本地主机场景。

## 自行编译

### 环境要求

- Windows 10/11。
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。
- PowerShell 5.1 或更高版本。
- Git（仅获取和提交源码时需要）。

项目不依赖外部 NuGet 包，`NuGet.Config` 已清空外部包源。

### 获取源码

```powershell
git clone git@github.com:Kratosmax/gamepause.git
cd gamepause
```

也可以使用 HTTPS：

```powershell
git clone https://github.com/Kratosmax/gamepause.git
cd gamepause
```

### 构建与测试

在仓库根目录执行：

```powershell
dotnet restore GamePause.sln --configfile NuGet.Config
dotnet build GamePause.sln --configuration Release --no-restore
dotnet run --project tests\GamePause.CoreTests\GamePause.CoreTests.csproj --configuration Release --no-build
dotnet src\GamePause.Updater\bin\Release\net8.0-windows\GamePause.Updater.dll --self-test
```

核心测试程序成功时应全部通过；更新器自测还会验证安装、失败回滚和 ZIP 路径穿越防护。

### WPF 视觉验收

涉及界面、布局、字体、主题或交互状态的改动，还必须运行视觉验收：

```powershell
dotnet run --project temp\visual-qa\VisualQa.csproj --configuration Release
```

它会在 `temp\` 目录生成主窗口、设置窗口、游戏档案窗口和更新窗口截图。需要人工检查 Windows 10/11 下的文字可读性、列宽、滚动、按钮边框、抗锯齿和控件重叠；截图属于临时产物，不提交到 Git。

### 生成发布包

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

输出目录为 `temp\release\GamePause-0.9.1\`，压缩包为 `temp\release\GamePause-0.9.1.zip`。发布目录中的三个可执行程序必须一起分发。

### 项目结构

```text
src/GamePause.App        WPF 主程序、托盘、快捷键、设置和更新检查
src/GamePause.Core       进程枚举、暂停恢复、安全策略和状态存储
src/GamePause.Watchdog   主程序异常退出后的恢复守护进程
src/GamePause.Updater    独立更新、校验、安装和回滚程序
tests/GamePause.CoreTests 核心及故障场景测试
temp/visual-qa           WPF 视觉验收程序（其余 temp 内容不提交）
scripts/publish.ps1      本地发布脚本
```

## 如何让 AI 继续开发本项目

### 接手前必须读取

让 AI 先读取以下内容，再提出或实施改动：

1. 本 README，了解产品边界、构建方式和未完成事项。
2. 与需求直接相关的 `src/` 代码及调用方，不根据类名猜测行为。
3. `tests/GamePause.CoreTests/Program.cs`，了解已有安全和故障测试。
4. `scripts/publish.ps1` 与三个项目文件，确认版本号和发布组成。
5. 仓库内的 `AGENTS.md`（如果后续添加），遵守项目级协作规则。

### 可直接交给 AI 的提示词

```text
请在当前 Game Pause 仓库继续开发：<写清具体需求>。

开始前先完整阅读 README.md、与需求有关的源码及现有测试，用代码证据说明当前行为和拟修改位置；查不到就明确说不知道，不要猜调用链。

约束：
1. 只做满足需求的最小修改，不顺手重构无关代码。
2. 不得移除系统关键进程黑名单、PID/启动时间校验、异常恢复记录、守护进程或退出恢复确认。
3. 不要真实暂停系统关键进程、反作弊组件或在线游戏。测试必须使用模拟、纯逻辑测试或明确安全的自建测试进程。
4. 更新功能必须保留清单签名、下载后二次哈希、程序集版本校验、ZIP 路径穿越防护、失败回滚和 Program Files 安装限制。
5. 不要生成、读取或提交正式发布私钥及其他密钥。
6. UI 改动必须运行 temp/visual-qa/VisualQa.csproj，检查生成截图；核心逻辑改动必须运行完整核心测试和更新器自测。
7. 完成后列出修改文件、关键原因、实际运行的命令与结果、截图位置和尚存风险。不能验证的部分必须明确说明。
8. 不要自行提交或推送，除非我明确授权。
```

### 开发与验证原则

- 证据优先：所有行为、调用链和缺陷结论都要能指向源码、日志或测试结果。
- 手术式修改：保持现有 .NET 8、WPF 和本地 JSON 存储方案，除非需求确实要求改变架构。
- 恢复优先：任何暂停路径都必须有对应恢复路径；新增状态必须考虑崩溃、重复启动、PID 复用和磁盘写入中断。
- 兼容优先：UI 同时考虑 Windows 10 回退主题和 Windows 11 系统背景材质。
- 密钥隔离：仓库只能保存公钥；发布私钥应离线保存，并通过受控发布流程使用。

判断“保持现有架构”在需求需要跨平台、驱动级冻结或真正的单进程跨重启内存快照时会失效；这些需求应先单独做技术方案和风险评估，不能直接塞进现有 WPF/用户态暂停实现。

### 每次改动后的最低验证

```powershell
dotnet restore GamePause.sln --configfile NuGet.Config
dotnet build GamePause.sln --configuration Release --no-restore
dotnet run --project tests\GamePause.CoreTests\GamePause.CoreTests.csproj --configuration Release --no-build
dotnet src\GamePause.Updater\bin\Release\net8.0-windows\GamePause.Updater.dll --self-test
```

若涉及设置持久化或 UI，再执行：

```powershell
dotnet run --project temp\visual-qa\VisualQa.csproj --configuration Release -- --settings-test
dotnet run --project temp\visual-qa\VisualQa.csproj --configuration Release
```

最后检查 `git diff`、`git status --short` 和生成截图，确认没有把 `bin/`、`obj/`、`artifacts/`、发布包、截图、日志、用户数据或密钥加入提交。

### 当前待办

- 在目标游戏和不同 Windows 10/11 环境完成真机兼容性验证。
- 根据真机日志补充兼容性规则和回归测试。

以上事项涉及发布安全或真实游戏环境，不能仅凭静态代码检查宣称完成。
