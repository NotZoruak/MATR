# MATR Project (Touken Ranbu Automation Assistant)

## Project Structure

```
MATR/
├── assets/
│   ├── interface.json               # Project entry config (resource version, task definitions, pipeline references)
│   └── resource/                    # MaaFW resource pack
│       ├── base/
│       │   ├── pipeline/            #   Pipeline definitions (JSON)
│       │   ├── custom/              #   Custom actions (C# scripts, one action class per file)
│       │   ├── image/               #   Template match images (1280×720 baseline)
│       │   └── model/               #   OCR model files
│       ├── logo/                    #   Startup logo resources
│       ├── silhouette/              #   Silhouette recognition reference images
│       └── announcement/            #   Version release notes (Markdown)
├── _src/                        # C# source code (Avalonia desktop app)
│   ├── MFAAvalonia/             #   Core library: Models, ViewModels, Views, Services, Controls, etc.
│   ├── MFAAvalonia.Desktop/     #   Desktop host project (MATR.exe entry)
│   └── MFAAvalonia.Android/     #   Android host project
├── docs/                        # Project docs (task design, usage conventions, dev logs, etc.)
├── tools/                       # Build/release scripts (clean_build.ps1, compress_json.py, pack_all.ps1, pack_win.ps1, pack_mac.ps1)
├── runtimes/                    # .NET native runtime libraries (multi-platform, multi-arch; distributed locally, not committed to git)
└── .github/                     # Issue templates
```

> The following directories are auto-generated at runtime and excluded in `.gitignore`: `config/`, `debug/`, `logs/`, `temp/`, `backup/`, `libs/`, `plugins/`.

### Upstream Source Customizations

`_src/` is a second-development fork of [MFAAvalonia](https://github.com/SweetSmellFox/MFAAvalonia). The following lists MATR's customizations over upstream:

所有针对 MFAAvalonia 上游行为的定制修改，都必须同步记录到 `docs/upstream-customizations.md` 和 `docs/upstream-customizations.json`。

| Customization | Files involved | Description |
|---|---|---|
| Removed `agent/` directory | `AppPaths.cs`, `VersionChecker.cs`, `PendingUpdateDeletionHelper.cs` | MATR does not use the Python agent; removed related path creation and update logic |
| Global options not shown as task items | `TaskLoader.cs` | Global options are controlled via the settings panel and not listed in the task list |
| Custom formation as a normal task | `assets/interface.json`, `TaskOptionGenerator.cs`, `MaaProcessor.cs` | “自定编队” uses task-specific presets and runtime parameter injection; it must not be restored as an upstream special task |
| Replaced EXE icon | `MFAAvalonia.Desktop/` | Uses MATR custom icon instead of the upstream default |
| Preserved MATR publish names | `MFAAvalonia.Desktop.csproj` | `AssemblyName` and `OutputName` must remain `MATR`, so release artifacts are named `MATR.exe` and remain compatible with the packaging scripts |
| Added utility tools to left sidebar | `_src/MFAAvalonia/` | Integrated silhouette recognition, dev logs and other utility entries into the left sidebar |
| Adjusted task list sidebar width | `_src/MFAAvalonia/` | Modified the sidebar width to fit Chinese display and usage habits |
| Fixed task queue completion status | `_src/MFAAvalonia/Extensions/MaaFW/MaaProcessor.cs` | Ensures the asynchronous task queue is fully awaited before stopping, preventing normally completed tasks from being recorded as manually stopped |
| Disabled telemetry UI | `TelemetryService.cs`, `AboutUserControl.axaml` | MATR does not collect or send telemetry; do not restore the upstream “Help improve software” switch |

> When upgrading the MFAAvalonia upstream version, verify whether the changes above are affected.

### Key Directory Notes

| Directory | Purpose | When to modify |
|---|---|---|
| `assets/resource/base/pipeline/` | Pipeline definitions, one JSON per task | Adding/modifying task flows |
| `assets/resource/base/custom/` | Custom action C# scripts | Writing a new action for non-standard operations |
| `assets/resource/base/image/` | Template match images, based on 1280×720 | Adding template images for new recognition nodes |
| `assets/resource/base/model/` | PaddleOCR models | Placing models when OCR recognition is needed |
| `_src/MFAAvalonia/` | Core C# code | Modifying program features, UI, config |

### Custom Actions

Current custom actions are located in `assets/resource/base/custom/`, 29 in total. They include task flow actions, team switching and decision helpers, naiban outfit handling, battle and logistics logging, warehouse scanning, sword-book scanning, and update-data persistence actions.

| Category | Files |
|---|---|
| Task and battle flow | `CompleteCurrentTaskAction.cs`, `DungeonFloorSelectAction.cs`, `ExpeditionMapSelectAction.cs`, `MixGreedySelectionAction.cs`, `NewMixTargetSelectionAction.cs`, `StopOnDamageAction.cs` |
| Team, recognition and decision helpers | `ExpeditionTeamRestRecognition.cs`, `NewMixTargetSelectionRecognition.cs`, `SortieRoundDoneRecognition.cs`, `UpdateDataIntervalRecognition.cs`, `TeamSwitchCheckAction.cs` |
| Naiban outfit helpers | `NaibanFilterClickAction.cs`, `NaibanFindSwordAction.cs`, `NaibanOutfitLogAction.cs`, `NaibanOutfitSelectionAction.cs` |
| Logging | `DispatchLogAction.cs`, `GuiLogAction.cs`, `KobanChestLogAction.cs`, `LogAction.cs`, `RepairStartLogAction.cs`, `ResourcePointLogAction.cs`, `SwordDropLogAction.cs` |
| Data and scanning | `SwordBookScanAction.cs`, `UpdateDataMarkSuccessAction.cs`, `UpdateDataPrepareAction.cs`, `UpdateDataSaveAction.cs`, `UpdateDataWaitAction.cs`, `WarehouseReadResourceAction.cs`, `WarehouseScanItemsAction.cs` |

队伍交替逻辑由 `TeamSwitchCheckAction.cs` 承担，旧联队战的 `TeamSwitchAction.cs`、`TeamSwitchNeededRecognition.cs` 与资源侧 `TeamSwitchDecision.cs` 已删除；`_src/MFAAvalonia/Extensions/MaaFW/Custom/TeamSwitchDecision.cs` 是应用侧静态辅助类，仍被 `TeamSwitchCheckAction` 使用，不要一起删除。

Follow the naming and structure of existing files when adding a custom action: one file per action class.

### Pipeline Task List

| File | Task |
|---|---|
| `Sortie.json` | Sortie (battlefield) |
| `Expedition.json` | Expedition |
| `Underground.json` | Underground (Osaka Castle) |
| `Disassemble.json` | Disassemble (arms disposal) |
| `FlowerBrush.json` | Flower brush (morale recovery) |
| `GoHome.json` | One-click return home |
| `LRentaisen.json` | Mock drills (rentaisen) |
| `NewMix.json` | Sword fusion |
| `TacticalTraining.json` | Tactical training |
| `DailyTask.json` | Daily task collection |
| `EdoCastle.json` | Edo Castle event |
| `FormationConfig.json` | Formation configuration |
| `SwordBook.json` | Sword book scanning |
| `UpdateData.json` | Data update |
| `Warehouse.json` | Warehouse scanning |

## AI Response Guidelines

When the user makes the following requests, the AI should follow the corresponding default approach:

| User request | Default AI approach |
|---|---|
| "Fix unstable node" | Add intermediate recognition nodes or adjust recognition thresholds/ROIs |
| "Retry on failure" | Analyze the root cause (which node, which recognition mismatch) and fix the node; never add blind retries |
| "Write a pipeline" | Ask the user for screenshots, ROIs, and screen transition info before writing; never fabricate coordinates |
| "Write a custom action" | Follow the naming and structure of existing files in `assets/resource/base/custom/`; one class per file |

## Coding Style

### C# (under `_src/`)

- .NET 10.0, C# 14, Nullable enabled
- File-scoped namespace declarations (`namespace MFAAvalonia.Helper;`)
- 4-space indentation, PascalCase public members, `_camelCase` private fields
- Log via `LoggerHelper`; avoid `Console.WriteLine`

### JSON (Pipeline / Resource Config)

- Pipeline 文件的编写与格式以「Pipeline 编写规范」章节为唯一来源，本节不再重复
- 资源 JSON（`interface.json`、`default_pipeline.json` 等）使用 2 空格缩进
- The Chinese term 节点 is strictly forbidden; always use the English word "node"
- Use English parent node / child node / sibling node for hierarchy; Chinese kinship terms such as 父节点 / 子节点 / 兄弟节点 are forbidden
- Resource paths use forward slashes `/`

## Pipeline 编写规范

适用范围：`assets/resource/base/pipeline/*.json`，以及 `assets/interface.json` 中的 `pipeline_override`。新增或修改 pipeline 时按本章节执行，其他章节不再重复相关条款。本章节约束新增与改动的 node，存量的既有写法按现状保留，不为此发起批量改动。

### 文件格式

- 一个任务一个文件，文件名与任务名一致
- UTF-8 无 BOM，严格 JSON：禁止注释与尾随逗号
- 格式以 `python tools/compress_json.py` 的输出为准：2 空格缩进、纯数字数组单行、不超过 5 个元素的字符串数组单行、单键非嵌套对象单行、不保留空 `param`
- 生成或修改 pipeline 后运行 `python tools/compress_json.py --sort-keys` 统一排版与字段顺序；改动 `interface.json` 的 `pipeline_override` 时追加 `--interface`。脚本目前只有写盘模式，只校验不写盘的 `--check` 模式列入 `docs/待修复问题.md`
- 文件内 node 按业务流程分组、组内按执行顺序排列，禁止按字母序重排

### 字段顺序

node 内字段按下述顺序排列，该顺序取自 `@nekosu/prettier-plugin-maafw-sort`，按执行流程排列：

```
desc / doc → enabled / max_hit → sub_name → recognition → inverse → pre_wait_freezes → pre_delay → action → anchor → repeat → repeat_wait_freezes → repeat_delay → post_wait_freezes → post_delay → timeout → rate_limit → next → on_error → focus / attach
```

- 未列入上述顺序的字段追加在末尾
- `recognition` 与 `action` 保持 `type` 在 `param` 之前，两者的 `param` 内部同样按键位顺序排列
- `is_sub` 与 `interrupt` 已在 MaaFramework 5.1 废弃，禁止使用，弹窗与加载改用 `[JumpBack]`
- 字段顺序由脚本重排，不需要手工调整；不带 `--sort-keys` 运行时只排版，不改变字段顺序
- `assets/interface.json` 中 `pipeline_override` 内的 node 同样适用该顺序
- 2026-09-14 已对全部 pipeline 与 `interface.json` 执行一次统一排版，字段顺序交给脚本维护

### 字段结构

- `recognition` 统一写为 `{"type": ..., "param": {...}}`
- `action` 需要点击目标时写为 `{"type": "Click", "param": {"target": [...]}}`；目标由 GUI 注入时只写 `{"type": "Click"}`
- `action` 缺省即为 `DoNothing`，空操作 node 不写 `action`，不要显式声明 `{"type": "DoNothing"}`；`pipeline_override` 中把既有动作覆盖为空操作时仍需显式写
- Custom 动作写为 `{"type": "Custom", "custom_action": ..., "custom_action_param": {...}}`
- `post_wait_freezes` 只使用 `time` 与 `target`

### 默认值

- `assets/resource/base/default_pipeline.json` 已为全部 node 设定 `rate_limit: 500`、`pre_delay: 100`、`post_delay: 100`
- 需要零延迟的 node 必须显式写 `pre_delay: 0` / `post_delay: 0`，依赖默认值会引入 100 毫秒等待

### 命名

- 前缀按任务划分，见 `docs/复用节点清单.md` 的前缀对照表
- node 名使用 PascalCase
- 动词段推荐使用 `Is` / `Detect` / `Check` / `Verify` / `Click` / `Try` / `Navigate` / `Post` / `Wait` / `Log` / `Hub`。该表为推荐用词，不作为强制条款，已有 node 不因用词差异回改
- 入口 node 名与文件名、任务名一致；被其它任务调用的子流程文件以其入口 node 名登记到 `docs/复用节点清单.md`

### 引用

- 优先引用同一文件内的 node
- 跨文件引用只允许指向已登记的公共接口 node，新增前先登记到 `docs/复用节点清单.md`

### 识别与坐标

- 所有坐标、ROI 与模板图基于 **1280×720**，ROI 与 target 为 `[x, y, w, h]`
- TemplateMatch 必须给出 `threshold` 与 `green_mask`
- 颜色识别使用 `upper` 与 `lower`

### 延迟与等待

- 用识别推进流程，不用固定延迟等画面。需要等待画面变化时使用 `pre_wait_freezes` / `post_wait_freezes`，并用 `target` 限定检测区域，只关注会变化的部分
- `pre_delay` / `post_delay` 只在确实需要给界面留出响应时间时使用。禁止用延迟掩盖识别失败，延迟在低性能设备上仍会失效
- 需要等某个结果出现时，新增一个识别 node 去确认它，而不是在上级 node 上加延迟
- `timeout` 用于「等不到就换路」，必须配合 `on_error` 给出失败去向，不用于拉长固定等待
- 存量 node 中的固定延迟不改动行为，改动到相关 node 时评估能否换成 `wait_freezes` 或中间识别 node

### 识别与操作的循环

- 每一步操作都要有前置识别：识别 A → 操作 A → 识别 B → 操作 B
- 禁止「整体识别一次 → 连续点击 A、B、C」。点击后画面可能已被弹窗替换，跳转可能需要后台加载，网络交互可能失败
- 点击会提交交互或改变账号数据的按钮后，必须再识别一次结果，确认操作已生效
- `next` 要覆盖该画面所有可能分支，保证首轮截图就能命中，并尽量保持线性流程

### 失败与重试

- 失败先定位根因：哪个 node、哪个识别不匹配、画面为什么与预期不同，再修识别参数或补分支
- 只有开发者明确指出某个 node 需要 `on_error` 时，才可为该 node 添加或保留 `on_error`；未明确指出时，禁止自行添加。
- 禁止两类重试：自身失败后再试一次的循环型重试，以及用重试掩盖真实问题的补偿型重试
- 禁止用 `max_hit` 掩盖死循环。`max_hit` 的语义是「该 node 最多被成功识别的次数」，超出后从 `next` 列表中被跳过，只用于确实需要限制次数的业务场景
- 确实无法靠识别解决的场景（游戏动画卡死、网络波动）须经讨论后引入有限次重试，并在 `on_error` 给出明确出口

### 弹窗与加载

- 弹窗、加载与中间态属于正常分支：主线能跑、弹窗能处理、加载能等过去、不在目标场景时能自动跳过
- 横切分支使用 `[JumpBack]` 挂载到相关 node 的 `next`，不写成主线步骤
- 新增任务必须包含本项目的通用中断处理链，清单见 `docs/复用节点清单.md`
- 主枢纽的 `on_error` 指向对应前缀的 `RestartGame`，卡死等待时间由全局选项覆盖

### 复用优先

- 写新 node 前先查 `docs/复用节点清单.md`，确认是否已有同能力 node
- 同能力 node 在不同任务中沿用同一后缀与相同参数，只更换前缀
- 新增跨文件引用的公共接口 node 时，同步登记到 `docs/复用节点清单.md`
- 新增或修改 node、任务及其设计前，优先核对 MaaFramework 官方文档的本地归档 `docs/maafw-official/manifest.json`，确认协议字段、语义与版本约束；本地归档需要刷新时再访问官方文档。
- 同时优先参考只读仓库 `_reference/MaaEnd/` 中同类 pipeline、资源组织、测试和自定义扩展的设计取舍，理解完整上下文后再实现；不得照搬其坐标、ROI、模板图或游戏专属业务逻辑。

### 实现方式优先

- 能用纯 pipeline 表达的逻辑一律用纯 pipeline 实现，不新增 custom action
- 循环、状态记忆与条件跳转优先使用协议自带能力：`next` 顺序识别、`enabled`、`max_hit`、`anchor` / `[Anchor]`、`[JumpBack]`、`repeat`
- 协议能力确实无法表达，或纯 pipeline 表达后的复杂度显著高于代码实现时，才考虑新增 custom action；新增时需在设计文档中写明纯 pipeline 不可行的具体原因

### 禁止项

- 禁止使用已废弃的 `is_sub` 与 `interrupt`
- 禁止在未提供界面截图、ROI 与跳转说明的情况下编写 pipeline
- 禁止用固定延迟或重试掩盖识别问题

## 跨平台开发规范

MATR 只维护一份共享代码，同时支持 Windows 和 macOS。Windows 是日常主要开发环境，macOS 作为需要持续兼容和在发布前验证的目标环境，不维护两套分支代码。

### 日常修改的判断

- Pipeline、业务逻辑、普通 Avalonia 界面、配置和资源处理通常属于共享代码，Windows 编译和运行验证即可。
- 文件路径、外部程序启动、Python、ADB、Shell、原生库、Windows API、图标、应用清单、字体、权限和发布脚本属于平台相关修改，必须同时检查 Windows 和 macOS 行为。
- 修改完成后先运行 `dotnet build _src/MFAAvalonia.Desktop`，涉及启动、路径、原生库或项目配置时再运行 Release 发布验证。
- 没有 macOS 设备时，使用 GitHub Actions 的 macOS 运行器进行 macOS 构建验证；正式发布前必须确认 Windows 和 macOS 包都能生成。

### 跨平台代码要求

- C# 平台判断统一使用 `OperatingSystem.IsWindows()`、`OperatingSystem.IsMacOS()` 和 `OperatingSystem.IsLinux()`。
- 路径统一使用 `Path.Combine`，不得写死 Windows 盘符、反斜杠路径或只适用于 Windows 的目录布局。
- Python 虚拟环境路径按平台处理：Windows 使用 `venv/Scripts/python.exe`，类 Unix 系统使用 `venv/bin/python`。
- Windows 专属 API、应用清单和图标必须使用可靠的平台条件隔离。项目文件不得依赖未必存在的 `$(OS)` 属性；平台判断应依据目标 RID，普通构建则回退到 .NET SDK 宿主 RID。
- 修改 MaaFramework 或其他原生库路径时，同时检查 Windows 的 `win-x64` 和 macOS 的 `osx-arm64` 运行时布局。
- Shell 分隔符、文件权限、字体回退和外部程序启动方式不得假设两个系统完全相同。

### 平台验证范围

- Windows 验证：确认 `MATR.exe` 使用 GUI 子系统、不弹出命令行窗口、显示项目图标，并完成基本启动和 ADB 连接测试。
- macOS 验证：确认 `osx-arm64` 构建成功、`MATR.app` 可以双击启动、资源可以加载，并完成基本 ADB 和任务流程测试。
- 只涉及共享业务逻辑的修改不要求每次手动验证 macOS；涉及平台相关内容的修改不得只凭 Windows 测试通过就视为完成。

### Source File Encoding

- `.ps1` files: UTF-8 with BOM
- All other source files (`.cs`, `.json`, `.md`, etc.): UTF-8 without BOM

## Build & Common Commands

| Command | Purpose |
|---|---|
| `dotnet build _src/MFAAvalonia.sln` | Build the whole solution |
| `dotnet publish _src/MFAAvalonia.Desktop` | Publish the desktop version |
| `pwsh tools/clean_build.ps1` | Clean build output |
| `python tools/compress_json.py` | Compress JSON files |
| `pwsh tools/pack_all.ps1 -Version vX.Y.Z` | Package Windows x64 and macOS Apple Silicon release zips |
| `pwsh tools/pack_win.ps1 -Version vX.Y.Z` | Package from root-level synchronized Windows release artifacts; for ordinary releases prefer `pack_all.ps1` |
| `pwsh tools/pack_mac.ps1 -Version vX.Y.Z -PublishDir <目录>` | Package a macOS Apple Silicon release |

## Commit Conventions

Follow [Conventional Commits](https://www.conventionalcommits.org/zh-hans/) v1.0.0:

| Type | Use case |
|---|---|
| `feat` | New feature (task, node, recognition logic) |
| `fix` | Bug fix |
| `perf` | Performance optimization |
| `refactor` | Code refactoring (non-functional, non-fix) |
| `docs` | Documentation-only change |
| `style` | Formatting, whitespace (no semantic change) |
| `chore` | Dependency updates, build scripts, maintenance |

提交标题统一使用以下格式：

```text
[可选表情] <type>(<scope>): <中文摘要>
```

约定如下：

- `type` 使用上表中的英文类型，`scope` 必须写受影响的任务 pipeline 文件名或 `.cs` 文件名；没有对应文件时才可省略。
- 标题使用中文，简明说明用户可感知的变化或本次修改的核心目的；建议控制在 72 个字符以内。
- 表情可选，只在确实有助于快速识别类型时使用，例如 `🐛 fix`、`🎨 style`、`⚙️ chore`；不要每条提交都强行添加。
- 一个提交只包含一个逻辑变化，不要把无关的格式化、重命名或临时调试混入功能提交。
- 一个逻辑变化无法干净地单独成一次提交时（例如同一文件里混着多个主题、hunk 级拆分又受换行归一化或工具限制无法完成），按文件粒度整体提交，并在提交信息里说明该文件同时包含哪几项改动；禁止为了让提交历史好看而临时还原、回退或重写文件内容再改回来。
- 正文只在用户明确要求时才写：用户没有要求写正文时，只提交标题，不要自行补充正文。被要求写正文时，大功能、跨文件重构或影响行为的修复在标题后空一行补充正文，使用项目符号说明实际改动、设计取舍、兼容性影响和验证方式，通常 3～6 条即可。
- 如果修复了 Issue，在正文或标题末尾注明 `fix #123`；不要只写“修复问题”而不说明问题是什么。
- 不要在提交信息中添加 `Co-authored-by` 行；协作者信息由项目协作流程单独记录。

示例：

```text
feat(queue): 支持任务完成状态正确落盘

- 等待异步任务队列完整结束后再更新最终状态。
- 正常完成的任务不再被记录为手动停止。
- 保持已有任务队列和界面显示逻辑不变。
- 已运行桌面项目构建验证。
```

> **The AI must not run `git commit` or `git push` on its own** unless the user explicitly asks to commit.

## Branch Strategy

完整流程、命令和常见误区见 `docs/分支流程.md`，本节只列出必须遵守的结论。

- **`develop`**：仓库默认分支，日常开发与外部 PR 的默认落点。新功能、多 node 流程改动和需要验证的新逻辑都先合入这里。
- **`main`**：稳定发布线，禁止直接提交。变更只能来自 `develop` 的发布合并或 `hotfix/*` 分支的修复合并，且都必须通过 GitHub PR 合并（本地合并后直接 push 会被拒绝），正式版在此打 tag。
- **`feat/<feature-name>`**：复杂功能从 `develop` 切出，验证后合并回 `develop`。
- **`fix/<scope>`**：常规缺陷修复从 `develop` 切出，合并回 `develop`。
- **`hotfix/<scope>`**：已发布版本的紧急修复从 `main` 切出，修完必须同时合回 `main` 和 `develop`，只合一边会让修复在下一个版本丢失。
- 测试版 tag 打在 `develop` 上，正式版 tag 打在 `main` 上。测试版不合并 `main`，直接在 `develop` 打 tag 发布；正式版必须先用 GitHub PR 把 `develop` 合并进 `main`，再在 `main` 上打 tag。测试版因此不必每个版本都开一次 PR。
- 每个正式版发布后（含 `hotfix/*` 合回）`main` 与 `develop` 必须回到同一提交，避免长期分叉；测试版期间允许 `develop` 领先于 `main`。
- MirrorChyan 依据 tag 名推断发布频道，发布 tag 必须与 `pack_all.ps1 -Version` 传入的版本一致。
- 分支保护：`main` 禁止直接 push，必须通过 PR 合并；`develop` 只禁止 force push 和删除。当前不要求 review，因为 GitHub 不允许批准自己开的 PR，等有第二个协作者后再把 Required approvals 设为 1。

### gh CLI 必须显式指定仓库

- 本仓库同时配置了 `origin`（`NotZoruak/MATR`）与只读的 `upstream`（`MaaXYZ/MFAAvalonia`）。多远端下 `gh` 会把仓库识别成上游，表现为 `gh pr create` 报 `No commits between main and develop`、`gh release create` 报 tag 未推送到 `MaaXYZ/MFAAvalonia`。
- 所有 `gh` 命令都要带上 `--repo NotZoruak/MATR`；`gh api` 改用完整路径，例如 `gh api repos/NotZoruak/MATR/pulls`。
- `upstream` 的推送地址已设为 `no_push`，但拉取地址仍会让 `gh` 误判，因此该参数不能省略，也不要依赖当前目录推断仓库。

## Review Checklist

When modifying code or pipeline, confirm:

- [ ] JSON fields conform to the MaaFW protocol; no typos or unsupported properties
- [ ] Every node has a defined failure destination where the flow can stall; entry and terminal nodes may omit `on_error`
- [ ] The `next` list covers all possible following screens so the correct node is hit in the first recognition cycle
- [ ] Coordinates, ROIs, and template images are based on the 1280×720 baseline
- [ ] New custom actions are placed in `assets/resource/base/custom/`; the file name equals the action class name
- [ ] Pipeline, interface.json, and resource files stay consistent
- [ ] Abnormal interruptions (popups, unexpected dialogs) have handling paths
- [ ] Pipeline files follow「Pipeline 编写规范」: formatted by `python tools/compress_json.py`, field structure and naming rules applied

## Version Numbering (SemVer 2.0.0)

All version numbers follow [Semantic Versioning](https://semver.org/lang/zh-CN/) `MAJOR.MINOR.PATCH`.

### MFAAvalonia Application

- Version is defined in `ApplicationVersion` in `_src/MFAAvalonia/MFAAvalonia.csproj` and the `Version` property in `_src/MFAAvalonia/ViewModels/Windows/RootViewModel.cs`; **both must stay in sync**
- The application itself is updated infrequently; bump manually at release time

### Resource Version

- Version is defined in the `Version` field of `interface.json` at the resource pack root
- Increment rules:

| Increment | Trigger | Example |
|---|---|---|
| **PATCH** | Bug fixes, fine-tuning recognition thresholds/ROIs/timing | `1.2.3 → 1.2.4` |
| **MINOR** | Adding nodes/tasks/recognition logic, restructuring tasks; backward compatible | `1.2.3 → 1.3.0` |
| **MAJOR** | Large-scale rewrite that overhauls the entire flow (rare) | `1.2.3 → 2.0.0` |

- Daily renaming/deleting of node names and reorganizing tasks are **MINOR**-level changes; they do not trigger a MAJOR bump
- PATCH resets to zero when MINOR is bumped; MINOR and PATCH both reset to zero when MAJOR is bumped
- A resource at `0.y.z` is considered in development; release `1.0.0` once stable

#### 修改资源版本号时必须同步的位置

每次提升资源版本号，以下位置必须全部同步，否则版本不一致：

| 位置 | 内容 |
|---|---|
| `tools/pack_all.ps1` | `-Version` 参数（发布 zip 命名，随打包命令传入） |
| `assets/interface.json` | `"version"` 字段 |
| `assets/interface.json` | `"custom_title"` 字段（窗口标题中嵌入的版本号） |
| `assets/resource/announcement/` | 新增对应版本的更新公告 Markdown（文件名格式 `N-vX.Y.Z 更新公告.md`） |

- 版本格式示例：`v0.9.0-beta.2`，四处保持一致
- 更新公告末尾追加「版本变更」行（`**版本变更**：vX.Y.Z → vX.Y.Z`）：正式版写上一正式版 → 本版，测试版写上一测试版 → 本测试版
- 更新公告正文统一按「新增」「修复」「依赖更新」「优化」四个小节编排；只记录面向用户可感知的版本变化。开发过程中对本版本新增功能的细节调整、内部实现问题和未在上一版本公开的中间状态，不单独作为更新公告条目；应合并描述为最终交付结果。
- 每条 commit 只能对应「新增」「修复」「依赖更新」「优化」中的一个类别；如果一次 commit 同时包含多个类别，必须拆分 commit，不能在同一条 commit 或公告条目中混合归类。
- 版本更新公告统一只保留当前版本的一份，内容必须汇总上一个正式版到当前版本的全部用户可见改动；测试版之间不单独累积多份更新公告。长期公告单独保留，不计入版本更新公告。
- 更新公告只记录「版本变更」行所表示的上一版本到当前版本之间的变化，不跨版本回顾历史内容。
- 历史版本的公告内容与开发日志中的旧版本记录保持原样，不回改；旧版本公告从当前资源包中移除即可，Git 历史会保留记录。
- 资源包中只保留一份当前版本更新公告，统一使用 `0-` 前缀，内容汇总上一个正式版到当前版本的全部改动；测试版之间不新增独立公告。长期公告单独保留，新增或调整长期公告时按现有序号顺延，并使用 `git mv` 保留重命名历史。
- MirrorChyan 上传按 tag 推断频道，发布 tag 必须与 `pack_all.ps1 -Version` 传入的版本一致，并与包内 `assets/interface.json` 的 `version` 一致；三项不一致时用户端判定没有更新，发布等于没发出去
- 发布前把版本号提交推送到远端属于推荐做法而非前提：分发只读 tag 名与 Release 产物，推送只影响源码可复现与 CI 覆盖面

## Release Process

正式版：先通过 GitHub PR 把 `develop` 合并进 `main`（使用 Merge commit），合并后本地执行 `git switch main` 与 `git pull --ff-only`，再打包并在 `main` 上打 tag。`main` 已开启分支保护，本地合并后直接 `git push` 会被拒绝。

测试版：不合并 `main`，直接在 `develop` 上打 tag、打包并创建 GitHub Release（勾选 pre-release），省掉每个测试版一次 PR；等下一个正式版发布时再一次性把 `develop` 合并进 `main`。测试版首次走该路径后，要在 MirrorChyan 后台确认该版本进入测试版频道。

**Local one-command packaging is the release flow** (since 2026-08-29; the GitHub Actions release workflow has been removed):

```
pwsh tools/pack_all.ps1 -Version vX.Y.Z
```

This produces both zips in the repo root:
- `MATR-vX.Y.Z-win-x64.zip` — framework-dependent (~104 MB); users need .NET 10 installed
- `MATR-vX.Y.Z-macos-arm64.zip` — self-contained (~127 MB); macOS users can run out of the box

Key points:
- The macOS package is **cross-published on Windows**: run `dotnet restore -r osx-arm64` before `dotnet publish -r osx-arm64`. After any csproj change the old assets file lacks the RID target and publish fails with NETSDK1047.
- `libloader.dll` (MaaFramework startup hook) is now wired into publish output via `Desktop.csproj` (`<Reference>` + `STARTUP_HOOKS`); no manual placement or hand-editing of deps.json/runtimeconfig.json is needed.
- NetBeauty is disabled (its `.;` literal-directory bug hid managed libs on Windows). The release package uses `runtimes/libs`; `pack_win.ps1` prioritizes `runtimes/libs` generated by the current publish output and removes native-library duplicates from that directory.
- When only JSON resources changed, `dotnet publish` can be skipped in theory, but running `pack_all.ps1` end-to-end is the safest path and is recommended.

`pack_all.ps1` publishes to `_src/bin/AnyCPU/Release/<RID>/publish` and passes that output directly to the platform packaging scripts. Only when creating a Windows test package from the workspace root should the current `win-x64` publish output first be synchronized to the root, then invoke `pack_win.ps1` without `-PublishDir`.

Manual Windows artifact sync (only when updating the workspace root directly):

```
# 先发布：dotnet publish _src/MFAAvalonia.Desktop -c Release -r win-x64 -p:Platform=AnyCPU --self-contained false
$publish = '_src/bin/AnyCPU/Release/win-x64/publish'
Copy-Item "$publish/MATR.exe", "$publish/MATR.dll", "$publish/MATR.deps.json", "$publish/MATR.runtimeconfig.json", "$publish/libloader.dll" -Destination . -Force
Copy-Item "$publish/runtimes/*" -Destination "runtimes" -Recurse -Force
# 根目录若有同名旧副本，必须以当前核心库覆盖，避免 CLR 优先加载旧版本。
Copy-Item "$publish/runtimes/libs/MFAAvalonia.Core.dll" -Destination . -Force
pwsh tools/pack_win.ps1 -Version vX.Y.Z
```

> ⚠️ Do not use `_src/bin/AnyCPU/Release/MFAAvalonia.Core.dll`: it is a stale desktop-project cache. The DLL in the current `win-x64/publish/runtimes/libs/` directory is the correct release artifact.

### Development and Test Runtime Boundaries

- `D:\Claude_Workspace\MATR` is the development area. Build, publish, packaging, and artifact-copy operations are performed here.
- “发布拷贝”特指：先将当前版本发布到 `_src/bin/AnyCPU/Release/win-x64/publish`，再把发布产物同步到开发区根目录 `D:\Claude_Workspace\MATR`，供用户直接启动检查；不指向测试运行区，也不包含向测试运行区同步。
- 开发区根目录发布拷贝至少包括 `MATR.exe`、`MATR.dll`、`MATR.deps.json`、`MATR.runtimeconfig.json`、`libloader.dll`、当前发布生成的 `runtimes` 内容，以及 `runtimes/libs/MFAAvalonia.Core.dll`。不得使用 `_src/bin/AnyCPU/Release/MFAAvalonia.Core.dll` 作为发布拷贝来源。
- `D:\Apps\小只工具\MATR` 是测试运行区，默认严格只读。AI 只能读取该目录中的文件、目录和运行状态，禁止创建、修改、覆盖、复制、同步、移动、重命名、删除、启动、停止或以其他方式操作其中的任何内容。
- 只有用户明确指定测试运行区中的具体文件地址和具体操作时，AI 才可以执行该项操作；用户未明确指定时，不得以发布验证、运行测试、同步程序或任何其他理由写入或操作测试运行区。
