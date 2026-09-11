<!-- markdownlint-disable -->

<div align="center">

<img alt="LOGO" src="./assets/resource/logo/MATR.png" width="256" />

# MATR — 刀剑乱舞自动化助手

<br>
<p align="center">
    <img src="https://img.shields.io/badge/Platform-Windows-0078D7?style=flat-square&logo=Windows" alt="Platform" />
    <img src="https://img.shields.io/badge/Language-C%23%20%2F%20Pipeline-%23239120?style=flat-square&logo=csharp" alt="Language" />
    <img alt="license" src="https://img.shields.io/github/license/NotZoruak/MATR?style=flat-square" />
    <a href="https://github.com/MaaXYZ/MaaFramework" target="_blank"><img alt="MaaFramework" src="https://raw.githubusercontent.com/MaaXYZ/MaaFramework/refs/heads/main/docs/static/maafw.svg" /></a>
    <br/>
    <img alt="stars" src="https://img.shields.io/github/stars/NotZoruak/MATR?style=flat-square&logo=github&color=darkgreen" />
    <img alt="downloads" src="https://img.shields.io/github/downloads/NotZoruak/MATR/total?style=flat-square&logo=github&color=darkgreen" />
</p>
<br>

<!-- markdownlint-restore -->

MATR 是一个基于 MaaFramework 的《刀剑乱舞》PC 端长期自动化系统，通过 ADB 连接模拟器运行游戏。项目以状态识别、任务编排、异常恢复、同步后勤和运行结果分析为核心，面向长时间、低干预的自动化运行。推荐使用 MuMu 模拟器 12，分辨率 1280×720。其它模拟器与分辨率缺少测试与适配，可能存在部分功能不生效的问题。

</div>

> [!TIP]
>
> 本项目目前处于快速迭代更新阶段，欢迎提交 PR 和 Issue。无论是使用中遇到的问题、功能建议，还是其他想法，都欢迎提出。也感谢每一位愿意使用、测试 MATR 并提出问题和建议的用户，项目的持续完善和发展离不开你们的支持与反馈。

## ⚠️ 免责声明与风险提示

> [!NOTE]
>
> 本项目基于 [MaaFramework](https://github.com/MaaXYZ/MaaFramework)（LGPL v3）构建，采用 [GNU General Public License v3.0](LICENSE) 协议，**永久免费且开源**。
> - 本项目为非计算机专业人员心血来潮之作，大量依赖 vibe coding 完成，仅供学习交流使用。
> - 程序图标（含 `assets/resource/logo/` 等图标资源）不随项目开源，著作权归 [米酒气泡水](https://huajia.163.com/main/profile/wEayJn7E) 所有，商用权归开发者所有。
>
> 本软件为第三方工具，通过识别游戏画面模拟常规交互动作，简化《刀剑乱舞-ONLINE-》的重复性操作。本项目遵循相关法律法规，绝不会修改任何游戏文件或数据。
>
> 因使用本软件而产生的任何问题，均与本项目及开发者无关。
> - 请勿在任何平台的《刀剑乱舞-ONLINE-》官方账号下提及 MATR。

> [!CAUTION]
>
> 根据游族网络《刀剑乱舞-ONLINE-》用户许可协议，严禁使用任何形式的妨碍游戏公平性辅助工具或程序（外挂）。官方已多次对违规账号采取封禁措施，包括但不限于封号警告、永久封禁等处罚。
>
> **您应充分了解并自愿承担使用本工具可能带来的所有风险，包括账号封禁、数据丢失等。**

## 快速开始

### 1. 下载

前往 [Releases](https://github.com/NotZoruak/MATR/releases) 页面，在最新版本下方点击「Assets」展开文件列表，根据系统下载对应压缩包并解压到空目录：Windows 下载 `MATR-vx.x.x-win-x64.zip`，Apple Silicon Mac 下载 `MATR-vx.x.x-macos-arm64.zip`。

> 已安装的用户可通过软件内设置面板检查更新，支持 GitHub 和 [Mirror酱](https://mirrorchyan.com) 双下载源切换（Mirror酱 需购买 CDK 激活）。
>
> 注意：Windows 包适用于 x86_64 架构；macOS 包适用于 Apple Silicon（arm64）架构。Linux 和其他架构暂不支持。

### 2. 启动

双击 `matr.exe` 即可运行。首次启动耗时可能较长，请耐心等待。

> 启动时若弹出 ".NET Desktop Runtime 10.0" 或 "VCRUNTIME140.dll" 等系统错误提示，说明缺少运行依赖。右键 `DependencySetup_依赖库安装_win.bat` → **以管理员身份运行**，安装完成后重新启动 MATR。

## 核心能力

### 状态机驱动

基于有限状态机组织自动化流程，通过识别当前游戏状态进行状态转换。任务可以从中途接管，在异常处理完成后回到可继续执行的状态，而不必始终从流程起点重新开始。

### 任务编排

通过任务编排将出阵、回城、补充刀装、队伍切换、远征处理和结果记录等操作组合为完整流程。不同任务可以复用通用处理逻辑，并根据任务选项调整执行路径。

### 异常恢复

持续处理任务执行过程中可能出现的异常状态，包括意外弹窗、卡死、游戏进程异常和模拟器重启。启用相关设置后，MATR 可以重新启动模拟器实例与游戏进程，并在恢复后继续执行任务。

### 同步后勤

同步后勤用于协调出阵任务与远征任务。当地下城等流程不会自动返回本丸时，系统仍可根据远征状态主动插入返回本丸收取奖励、重新派遣等后勤处理，减少长期运行中的远征资源损失。一键日课同样支持同步后勤：使用「本丸后勤」任务中的设置，在日课开始前处理一次远征派遣、修刀与内番，再执行各日课项目。

### 事件日志与运行结果分析

工作记录工具可读取日志文件，按任务拆分并过滤可统计的数据，展示任务运行时间、运行状态、出阵次数、行军次数、完成圈数和返回本丸次数，并汇总资源获得与刀剑掉落。记录支持在任务运行期间刷新查看，任务结束后也可以手动保存，或将多条记录合并统计。

## 支持的任务与活动

<img alt="MATR 主界面" src="./screenshots/MATR 主界面（v0.10.1）.png" width="960" />

<img alt="MATR 工作记录" src="./screenshots/MATR 工作记录（v0.11.1-beta.1）.png" width="960" />

### 任务

- **后勤**：统一处理远征、修刀和内番，可通过其他任务中的同步后勤选项，在出阵中穿插后勤处理。启用全局设置中的远征智能调度后，可定时从活动页面主动返回本丸检查队伍状态。启用长期远征计划后，可在远征队伍中刀剑男士疲劳值低于阈值时，自动刷花并恢复编队，再次派遣远征。
- **自定编队**：按预设配置指定部队的刀剑、刀装与马匹，默认排在「一键日课」之前，需要先编队时勾选该任务即可。
- **一键日课**：按固定顺序执行登录奖励、暖心礼包、合成、刀解锻刀、演练、领取奖励和领取邮件；支持同步后勤，在日课开始前使用「本丸后勤」任务中的设置处理远征、修刀与内番。
- **合战场**：选择时代、地域、部队和阵形进行长期出阵，支持重伤、刀装、疲劳、撤退和同步后勤处理。
- **地下城**：选择目标层数和部队进行出阵，支持重伤、刀装、疲劳、每轮回本丸、动画跳过和同步后勤。
- **战术强化训练**：选择部队和难度进行活动出阵，支持换队长、门票不足时停止和同步后勤。
- **联队战**：选择部队和难度进行活动出阵，支持部队交替、换队长和门票处理。
- **刷花**：选择部队单骑出阵 1-1，提升刀剑男士的疲劳度。
- **刀解**：按刀种筛选刀剑并自动解体，同时处理邮箱。
- **习合**：按刀种筛选刀剑进行习合，支持搓糖功能，自动跳过乱7以上刀剑（不对上锁刀剑生效）。

### 小工具

- **本丸**：包含仓库、工作记录和刀帐三个页面。仓库用于识别和保存本丸资源与道具数量，并可生成核心资源变化折线图；工作记录用于查看任务运行过程中的收获与特殊状况；刀帐用于统计立绘拥有信息。仓库与刀帐支持自动识别。
- **限锻计算工具**：根据锻刀公式、当前积分、资源和道具数量，计算达到目标积分所需的锻刀次数及剩余资源，支持自动读取当前积分和资源。
- **数据库**：包含极化经验表、远征收益表和剪影识别。剪影识别可按置信度同时给出并排列多条结果，同时显示对应剪影完整图片。

## QQ频道

频道号 `pd68335487`，用于日常交流、使用咨询和经验分享。Bug 反馈请优先通过下方问卷或 GitHub Issues 提交。

## 反馈与建议

MATR 自带日志打包功能：在任务页面的“日志”卡片右上角点击文件夹图标，选择需要的日志和截图后点击“导出日志”。打包完成后，请将压缩包通过 [问题反馈与日志收集问卷](https://ycnviwngeokc.feishu.cn/share/base/form/shrcnEJvA6mbBOSU2RO7DnRm8Qh) 或 [GitHub Issues](https://github.com/NotZoruak/MATR/issues) 提交。

提交反馈时请尽量包含以下信息：

- **Bug 报告**：描述操作步骤、预期结果和实际结果，附上截图或日志（`debug/` 目录下）
- **功能建议**：描述期望的功能场景和使用目的

> 提交前请先搜索已有 issue，避免重复。

## 目录结构

```
MATR/
├── assets/
│   ├── interface.json                    ← 任务与选项配置
│   └── resource/
│       ├── base/
│       │   ├── pipeline/                 ← 自动化任务流水线（JSON）
│       │   ├── image/                    ← 模板匹配图片
│       │   ├── custom/                   ← 自定义动作脚本
│       │   └── model/ocr/                ← OCR 模型
│       ├── logo/                         ← 程序图标
│       ├── silhouette/                   ← 剪影识别样板
│       └── announcement/                 ← 公告
├── _src/                                 ← C# 源代码与解决方案
│   ├── MFAAvalonia/                      ← 核心库、界面、服务和业务逻辑
│   ├── MFAAvalonia.Desktop/              ← Windows/macOS 桌面端宿主
│   ├── MFAAvalonia.Android/              ← Android 端宿主
│   ├── MFAAvalonia.Tests/                ← 自动化测试
│   └── MFAAvalonia.sln                   ← Visual Studio 解决方案
├── docs/                                 ← 项目文档
├── tools/                                ← 构建/发布脚本
├── screenshots/                           ← README 展示图片
├── runtimes/                             ← 本地 .NET 运行时库
├── .github/                              ← GitHub Issue 模板与工作流
│   └── workflows/
│       ├── mirrorchyan_release.yml       ← 发布时上传 Mirror酱
│       └── check.yml                     ← PR 检查
└── LICENSE
```

## 源码运行

如果希望从源码运行或参与开发，请先安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，然后执行：

```powershell
git clone https://github.com/NotZoruak/MATR.git
Set-Location MATR
dotnet run --project _src/MFAAvalonia.Desktop
```

也可以先构建桌面端项目：

```powershell
dotnet build _src/MFAAvalonia.Desktop
```

程序运行时需要从项目根目录加载 `assets/` 等资源目录。若要修改自动化流程，请优先阅读 `docs/` 中的相关说明，并遵循现有 pipeline 和自定义动作的编写约定。

## 欢迎迁移到其他版本与平台

目前 MATR 主要面向国服 PC 端，并通过 ADB 连接模拟器运行。我们非常欢迎熟悉《刀剑乱舞》日服，或希望将 MATR 迁移到安卓端的开发者参与：

- **日服迁移**：欢迎熟悉日服的开发者直接将适配内容贡献到 MATR，补充日服界面、文本、流程差异对应的识别资源和 pipeline；如果希望基于 MATR 的整体逻辑和实现思路独立创建适配日服的项目，也同样欢迎。无论采用哪种方式，最好都附上可公开使用的截图及测试说明。
- **安卓端迁移**：欢迎完善 `_src/MFAAvalonia.Android/`，或探索适用于安卓设备的连接、权限和运行方式。
- **协作方式**：可以先在 [GitHub Issues](https://github.com/NotZoruak/MATR/issues) 说明目标和现状，也欢迎直接提交 PR。较大的改动建议先开 Issue 讨论方案，避免重复工作。

如果你完成了日服或安卓端的适配，也欢迎与社区分享，让更多玩家能够使用和维护这些版本。

## 致谢

### 开源项目

- [MaaFramework](https://github.com/MaaXYZ/MaaFramework) — 基于图像识别的自动化黑盒测试框架
- [MFAAvalonia](https://github.com/SweetSmellFox/MFAAvalonia) — 基于 Avalonia UI 的 MaaFramework 通用 GUI 解决方案
- [Mirror酱](https://mirrorchyan.com) — 软件分发与 CDK 激活管理平台
- [MaaAssistantArknights](https://github.com/MaaAssistantArknights/MaaAssistantArknights) — 《明日方舟》小助手，全日常一键长草
- [MFAToolsPlus](https://github.com/SweetSmellFox/MFAToolsPlus) — MaaFramework 新一代开发辅助工具箱
- [MaaLogAnalyzer](https://github.com/MaaXYZ/MaaLogAnalyzer) — 可视化日志分析工具，告别手翻百万行日志

### 开发者

感谢以下开发者对 MATR 的贡献：

[![贡献者](https://contrib.rocks/image?repo=NotZoruak/MATR&max=1000)](https://github.com/NotZoruak/MATR/graphs/contributors)


感谢 MaaFramework 提供自动化框架和低代码开发流程，以及 MFAAvalonia 提供的图形化界面方案，让非专业人员也能轻松实现自己的 MAA 项目。也感谢 Claude、DeepSeek 和 Codex，帮助我完成自定义动作编写、UI 个性化设置、流程校验和 Bug 排查。最后，感谢每一位提出建议、反馈问题和支持 MATR 的用户，刃工智能的成长离不开大家的陪伴与支持。


## Star History

如果觉得软件对你有帮助，帮忙点个 Star 吧！（网页最上方右上角的小星星），这就是对我们最大的支持了！

<a href="https://star-history.com/#NotZoruak/MATR&Date">
  <img alt="Star History Chart" src="screenshots/star-history.png" />
</a>
