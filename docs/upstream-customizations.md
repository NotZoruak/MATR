# MFAAvalonia 定制层总览

本文件是 `upstream-customizations.json` 的人读说明。升级时先以目标上游版本建立桌面 GUI 基线，再按条目重放 MATR 行为；不能通过整文件覆盖旧源码来伪装升级。

## 维护规则

- 每次上游升级前，导出当前工作区补丁与来源清单。
- 每个与上游不同的源码文件，必须能关联一个台账 ID 或一条明确的桌面排除记录。
- `drop` 只表示已有证据表明上游实现等价吸收；它不表示未经审查地删除 MATR 功能。
- 每次升级结束后，将保留项的 `last_verified_upstream` 更新为实际验证版本，并写入升级报告。

## 定制项

### settings.background-image-description-spacing

`GuiSettingsUserControl.axaml` 的背景图片说明右边距使用 12 个逻辑像素，避免右侧固定宽度按钮与原有 70 像素留白共同挤压说明文字，导致逐字换行。上游升级时保留此间距，并检查窄窗口中的说明换行及按钮显示。

### `task-loader.global-options-hidden`

全局选项不应显示为普通任务项，但仍必须能在设置界面配置。升级时需适配上游模板和预设模型。

设置区固定为“常规 / 全局”两页，不保留上游“高级”页。全局页需要保留 `global_option`，以及刀解/合成许可名单和刀剑掉落播报入口；名单编辑在设置区子页打开。任务勾选框必须绑定 `IsCheckBoxEnabled`，不能让资源设置项或运行中的实例参与勾选。

任务列表顶部的全选与取消全选必须合并为一个 `ToggleSelectAllCommand` 按钮：存在未选任务时点击全选；所有可选任务均已选中时再次点击全不选。不得恢复为上游的两个独立按钮。

任务选项的下级选项有两种明确模式：默认模式显示小齿轮，点击后在设置区子页编辑；资源定义设置 `inline_sub_options: true` 时，则直接在母选项下展开。`MaaInterfaceOption` 必须保留该 JSON 字段及合并规则，`TaskOptionGenerator` 必须据此选择呈现方式。checkbox 类型必须保持方框加文字，而不是上游的 ToggleButton 卡片样式。

下拉框搜索必须由资源中的 `is_searchable` 单项控制：`MaaInterfaceOption` 保留该 JSON 字段，`TaskOptionGenerator` 将其传给 ComboBox 搜索行为。不得把所有下拉框强制设置为可搜索。

### `task-formation-config.normal-task`

“自定编队”必须作为 `assets/interface.json` 中的普通任务注册，入口为 `FormationConfig`，通过任务专属的 `FC_选择预设` 设置选择编队预设；不得重新加入上游的特殊任务列表。

预设选择页支持新增、编辑、复制、粘贴、删除和切换当前预设。运行前由 `MaaProcessor` 将所选预设转换为 `FormationConfigAction` 参数与编队 pipeline 覆盖；每日任务的“预设部队”也复用同一套预设及装备编辑页。升级时不得仅保留 `FormationConfig.json`，否则任务虽有 pipeline 却无法选择预设或注入参数。

### `task-captain.skip-positions`

任务运行前，`MaaProcessor.CreateNodeAndParam` 必须读取当前任务的换队长下级选项，通过 `CaptainSettingsHelper` 和 `CaptainSettingsDecision` 将 `skip_positions` 注入对应拖拽 action。合战场、地下城、联队战和江户城分别使用各自的配置；未勾选位置或关闭换队长时显式传入空数组，战术强化同样传入空数组。

注入只覆盖 action 参数，保留疲劳处理选项设置的 `next` 和 `enabled`。升级上游时不能仅保留设置界面与拖拽动作而漏掉运行参数注入。验证任务参数包含已选位置，取消勾选后为空数组，并确认疲劳撤退与刷花组合下仍保留该参数。

### `ui.matr-tools-and-layout`

保留 MATR 的业务工具入口、中文界面布局及任务区宽度。桌面 UI 以新上游根窗口为基础；`Views/Mobile/RootViewContent` 是桌面和移动端共用的根级导航壳，必须随桌面端迁入。仅独立的移动端宿主与页面不在本次范围。

桌面图标使用 MATR 的 `Assets/logo.ico`，不可被上游默认图标替换。

任务运行态采用两行布局：第一行只显示任务名称和设置齿轮；第二行显示运行耗时与状态图标。状态图标必须与设置齿轮共用同一列，并使用相同的 24×24 布局边界；Suki 图标几何的视觉中心存在偏移，状态图标组需向右平移 3 个逻辑像素，使其视觉中轴与齿轮重合。不得将耗时或状态图标重新放回名称所在行，否则窄窗口会优先截断中文任务名称。

### `services.update-data-scheduling`

更新数据任务按间隔执行，状态存入实例配置。需验证间隔跳过与实例重新加载。

### `daily-task.per-game-day-completion`

一键日课的登录奖励、暖心礼包、合成、刀解和锻刀使用 MATR 自定义的游戏日完成台账。完成日期按每日 5:00 切换；状态写入 `debug/logs/daily-task-completion.log`。只有实际完成路径写入台账：无合成素材、刀解素材不足或未完成 3 次锻刀均不得标记为完成。

台账由 `DailyTaskCompletionService` 统一读写，并通过自定义 action 接入 pipeline。升级时需同时保留日志文件格式、检查/写入 action 注册，以及日课各项目的成功路径和跳过路径。

### `work-records.name-dialog-registration`

工作记录的保存、另存与重命名均通过 `WorkRecordNameDialogViewModel` 输入名称。该 ViewModel 必须在 `App.ConfigureViews` 注册为 `WorkRecordNameDialogView`；上游升级时即使两个源文件仍存在，也不得遗漏这条映射，否则保存会提示找不到对应视图。

### `queue.resume-and-continue-on-error`

保留失败继续、用户停止、断点续跑、轮次统计和结束后操作边界。必须基于新上游 `RunResult`/取消模型重放，并覆盖关键队列路径。

任务队列的外层异步执行必须等待 `ExecuteTasks` 完整返回后再进入停止流程。调用 `TaskManager.RunTaskAsync` 时应使用 `Func<Task>` 重载，不能把异步 lambda 绑定到 `Action` 重载，否则会在第一个 `await` 后提前执行停止逻辑，并将正常完成的任务记录为“手动停止”。

任务队列需要在普通任务之间自动插入 `GoHome`，确保下一个游戏任务从本丸开始；最后一个任务后不插入。MFAA 提供的特殊任务通过 `Entry` 标识（如 `CountdownAction`、`WebhookAction`）识别，特殊任务前不得插入回本丸。特殊任务集合由任务队列策略与任务添加界面共用，升级时不得恢复为无条件插入。

有限重复的 MAAFW 任务在每轮成功后必须向 GUI 日志输出“任务完成：任务名 进度 X/Y”。该行为位于 `MFATask.Run` 的循环内，`MaaAction` 返回成功后触发；无限重复与单次任务不输出。升级时不得因将 action 改为返回 `MaaJobStatus` 而遗漏该输出。

任务失败时除保留界面内提示与外部通知外，还必须调用 `ToastNotification.Show` 发送系统通知，使失败、成功的任务结束反馈保持一致。升级时检查失败分支，避免只剩界面内日志而用户错过失败结果。

合战场任务的 `repeatable` 固定为 `false`，但“异去”模式的轮次数由“过去/异去”选项下的 `异去_重复次数` 输入项决定。`MaaProcessor.CreateNodeAndParam` 必须从该下级选项读取 `repeat_count`，并将正整数与 `-1`（无限循环）直接作为队列轮次；不得因合战场未标记为可重复而压成单次执行。“过去”模式保持三轮的既有策略。

### `task.sync-expedition-reuse`

同步后勤是 MATR 对 MFAAvalonia 远征流程的定制扩展。合战场、地下城、陆联、战术强化和江户潜入启用“同步后勤”时，`MaaProcessor.CreateNodeAndParam` 必须从当前实例的“后勤”任务读取“部队一”至“部队五”的选项，并将这些选项的 `pipeline_override` 合并到当前任务。

远征队伍的检查开关必须沿用后勤任务的配置：选择“休息”时对应的 `E_CheckTeamN` 为 `false`，选择远征地图时对应的 `E_CheckTeamN` 为 `true`，同时合并对应的地图选择参数。同步选项本身不得固定把五个 `E_CheckTeamN` 全部设为 `true`，否则休息队伍会进入 `E_SelectMapN`，并可能因自定义选图动作返回失败而重复进入远征页面。

“修刀”的“筛选条件”是 checkbox 多选项。多个刀种或伤势的 `pipeline_override` 必须先递归合并到同一个 `E_FindRepairableSword` 覆盖对象；不能按 case 分别生成同名 node 的多层覆盖，否则 MaaFramework 只会使用最后一层，表现为只点击最后一个筛选条件。

该逻辑还负责同步修刀、内番和远征刷新间隔；升级时必须保留实例配置重新读取兜底，避免配置缓存为空或被惰性枚举污染时丢失队伍设置。2026-09-05 的 MFAAvalonia v2.16.1 升级曾移除整段同步配置复用逻辑，导致已配置的第二至第五队不再检查；后续修复不得只在 `interface.json` 的同步选项中补充固定启用开关。

### `recovery.game-and-emulator-restart`

MFAAvalonia 2.16.1 升级曾丢失 `93e62c16` 引入的动作循环与无回调检测接入。必须保留 `TaskRecoveryMonitor`、任务回调记录和 `TryRunTasksAsync` 中独立于底层 `Wait` 的检查循环。检测只在 ADB 普通任务且开启「卡死重启」时启用，静默阈值沿用「卡死等待时间」（默认 120 秒），排除智能等待、人工弹窗及已经开始的游戏恢复。停止和完成任务时撤销检测。

恢复时先异步请求底层停止，再执行外部游戏与模拟器恢复，不能先等待挂起的底层停止；底层退出确认有 30 秒上限。恢复后重连并重新执行当前中断任务，保留外层队列及已完成轮次；恢复失败必须停止队列，不能继续向未退出的执行器追加任务。恢复动作应显式接收所属处理器和取消令牌，避免切换实例后操作错误设备，手动停止后不得继续启动任务。MuMu 的 Windows 命令不得在其他平台执行。

连接失败时，开启「尝试启动模拟器」「重启 ADB Server」「关闭并重启 ADB 进程」中的任意一项，并填写有效游戏路径，均应允许沿用启动设置启动模拟器；首次连接没有设备地址时也必须进入此路径。必须保留已保存 ADB 目标的严格匹配，不能误连其他实例。

保留 MATR 对 MFAAvalonia 卡死恢复动作的二次开发。`RestartGameAction` 必须从当前 ADB 配置读取目标应用包名，优先通过 `cmd package resolve-activity` 解析实际启动 Activity，再使用 `am start -n` 启动，不能依赖部分模拟器缺失的 `monkey` 命令。

恢复流程必须区分模拟器重启失败与游戏启动失败：模拟器重启失败时记录错误并让恢复动作失败；模拟器已恢复但游戏启动失败时记录警告，并将控制权交回任务 pipeline，继续尝试游戏图标、登录和主枢纽流程。升级 MFAAvalonia 的自定义动作注册、ADB 配置读取或任务错误处理时，必须保留该行为。

### `adb.mumu-emulator-extras-input`

ADB 输入方式设为“自动”时，`MaaProcessor` 必须为名称包含 `MuMu` 的设备补充 `EmulatorExtras`，并保留 MaaFramework 已发现的全部输入方式。MaaFramework 某些 MuMu 版本会返回缺少该位的掩码，导致连续触控退回至不支持拖动的 `AdbShell`，使习合素材列表无法翻页。

用户显式选择的输入方式不得被覆盖。验证自动模式下 MuMu 的输入掩码包含 `EmulatorExtras`，并通过多页习合素材列表确认连续上滑可用。

### `runtime.resource-path-and-packaging`

保留资源大小写兼容、桌面发布结构、图标与 `libloader` 启动钩子；不恢复 Python agent。验证完整包资源加载、Windows/macOS 发布及 agent 排除。

Windows 发布目录可能同时在 `runtimes/libs` 与根级 `libs` 放置托管依赖。发布包统一使用 `runtimes/libs`，并且 `MATR.runtimeconfig.json` 的 `NetBeautyLibsDir` 与 `SubdirectoriesToProbe` 必须都指向该目录；打包脚本仅在输入目录存在根级 `libs` 时合并其内容，但必须随后以 `runtimes/libs` 的本次发布产物覆盖同名文件，防止残留的旧核心程序集进入压缩包。

桌面项目的 `AssemblyName` 与 `OutputName` 必须保持为 `MATR`。上游默认的 `MFAAvalonia` 输出名会使 `pack_win.ps1` 误用开发根目录残留的旧 `MATR.exe`，从而将旧核心程序集打入测试包；Windows 打包必须先 `dotnet publish`，将宿主文件和 `runtimes` 同步到开发根目录，再以根目录为输入执行 `pack_win.ps1`。

MATR 的资源包固定在 `assets/`：`AppPaths.InterfaceJsonPath`、`AppPaths.InterfaceJsoncPath` 和 `AppPaths.ResourceDirectory` 必须分别解析到 `assets/interface.json`、`assets/interface.jsonc` 与 `assets/resource`。上游若改回程序根目录布局，会触发默认资源兜底并显示空任务列表。

`VersionChecker` 必须兼容完整程序包和仅资源包的更新结构：完整程序包中的 `assets/interface.json` 与 `assets/resource` 需要保留 `assets` 路径并识别程序文件；仅资源包根目录中的 `interface.json` 与 `resource` 则必须映射到 `assets/interface.json` 与 `assets/resource`。增量更新的资源文件、目录删除和目录创建也必须使用同一套路径映射，避免更新后程序从错误的根目录读取资源并提示资源加载失败。

`MaaProcessor.ProjectDir` 必须解析为 `interface.json` 所在目录（MATR 即 `assets/`），并且所有 resource 路径均以它替换 `{PROJECT_DIR}`。`MaaProcessor.CheckInterface` 的默认资源路径必须为 `{PROJECT_DIR}/resource/base`；两项必须配套，最终解析到 `assets/resource/base`，不得在程序根目录创建不需要的 `resource/base/pipeline/sample.json`。

### `resource.sword-drop-recognition`

MATR 使用 `SwordDropLogAction` 记录合战场、地下城、联队战和战术强化中的刀剑掉落，并支持播报和初掉落截图。初掉落不使用动画文字 OCR，而是检查 1280×720 基准画面的 `[180,397,8,30]` 区域；区域内所有像素都必须命中 RGB `[195,13,24] ±1`，命中后才进入初掉落截图与刀名识别流程。

特化和极化仍由动画 ROI 的 OCR 识别。升级资源或自定义动作时，必须保留四个 pipeline 挂载点、颜色匹配规则和 `debug/sword_drop/` 截图行为。

### `runtime.custom-action-loading-isolation`

MATR 的资源包包含运行时动态编译的自定义动作。`MFAExtensions.ToBitmap` 必须接受 MFAFramework 返回的 `IMaaImageBuffer` 接口，否则使用 `IMaaContext.GetImage()` 的动作会在编译阶段失败，随后在 pipeline 中表现为 `Action is null`。

自定义动作加载器必须按资源目录隔离缓存和文件监听器；资源切换或脚本变更后只能复用同一目录的缓存。`MaaProcessor` 只允许从当前资源声明的路径加载 `custom` 目录，并需对绝对路径去重，不能额外扫描安装目录中的旧资源，避免不同资源版本的动作混合注册。

升级时必须验证 `SwordDropLogAction`、`MixGreedySelectionAction` 和 `NewMixTargetSelectionAction` 能够动态编译并注册；同时确认资源切换后不会继续使用上一套自定义动作。

### `runtime.debug-log-maintenance`

保留 MATR 的磁盘日志维护。应用启动时必须调用 `AppPaths.CleanupOldDebugLogs`：轮转现有 `debug/maafw.log`，清理超过三天的备份日志和截图；当 `debug` 总大小超过 500 MiB 时，最多保留最新 10 个备份日志和 `on_error` 中最新 50 张 PNG 截图。

`MaaLogRotator` 必须在应用启动时启动，并在退出时停止；运行期间每 30 秒检查一次，在单个 MaaFramework 日志超过 20 MiB 时切分为备份。升级应用生命周期、日志目录或 MaaFramework 日志初始化时，验证该维护流程仍被调用，避免 `debug/` 无限增长。

### `privacy.telemetry-disabled`

MATR 的 `TelemetryService` 是不采集、不保存、不发送信息的兼容入口。关于页面不得显示上游“帮助改进软件”开关；它会默认显示开启，却没有实际效果并误导用户。升级时保留服务兼容入口以避免上游调用点失效，但移除该无效 UI。

导出日志成功提示仍应提供人工反馈入口：`FileLogExporter` 调用 `ToastHelper.SuccessWithSurvey`，由用户自行点击“去反馈bug”打开问卷链接。该入口不采集或发送任何遥测数据。
