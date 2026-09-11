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

设置区固定为“常规 / 全局”两页，不保留上游“高级”页。全局页需要保留 `global_option`，以及刀解/合成许可名单和刀剑掉落播报入口；客户端类型必须置于首位，许可名单入口紧随其后，名单编辑在设置区子页打开。任务勾选框必须绑定 `IsCheckBoxEnabled`，不能让资源设置项或运行中的实例参与勾选。

任务列表顶部的全选与取消全选必须合并为一个 `ToggleSelectAllCommand` 按钮：存在未选任务时点击全选；所有可选任务均已选中时再次点击全不选。不得恢复为上游的两个独立按钮。

任务选项的下级选项有两种明确模式：默认模式显示小齿轮，点击后在设置区子页编辑；资源定义设置 `inline_sub_options: true` 时，则直接在母选项下展开。`MaaInterfaceOption` 必须保留该 JSON 字段及合并规则，`TaskOptionGenerator` 必须据此选择呈现方式。checkbox 类型必须保持方框加文字，而不是上游的 ToggleButton 卡片样式。

下拉框搜索必须由资源中的 `is_searchable` 单项控制：`MaaInterfaceOption` 保留该 JSON 字段，`TaskOptionGenerator` 将其传给 ComboBox 搜索行为。不得把所有下拉框强制设置为可搜索。

### `task-formation-config.normal-task`

“自定编队”必须作为 `assets/interface.json` 中的普通任务注册，入口为 `FormationConfig`，通过任务专属的 `FC_选择预设` 设置选择编队预设；不得重新加入上游的特殊任务列表。

预设选择页支持新增、编辑、复制、粘贴、删除和勾选预设，自定编队任务可勾选多个预设并按设置页从上到下的顺序依次编成。多选编号保存在任务选项数据的 `preset_ids` 中；`MaaProcessor` 在任务装配阶段按预设逐个展开为多次编队任务，再把单个预设转换为 `FormationConfigAction` 参数与编队 pipeline 覆盖。一键日课不再内置“开始前启用预设部队”，需要先编队时由用户启用默认排在日课之前的自定编队任务。升级时不得仅保留 `FormationConfig.json`，否则任务虽有 pipeline 却无法选择预设或注入参数。

刀装与刀剑名称匹配由 `FormationNameMatcher` 统一处理。刀装中存在铳、弓、枪、盾这类单字目标，必须先在归一化后的文本上做包含判断，再做单字长度判断，否则单字目标拿不到形近字容错。归一字形需保留銃/铳、统/铳、槍/枪，避免 OCR 把“铳兵”识别成“统兵”后扫到列表底部仍判定未找到。

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

一键日课的登录奖励、暖心礼包、合成、刀解和锻刀使用 MATR 自定义的游戏日完成台账。完成日期按每日 5:00 切换；状态写入 `debug/logs/daily-task-completion.log`。开启刀解时，首次刀解至少一把；若收取完成锻刀所需刀位不足，则按缺口刀解腾位。两种刀解均写入同一当天完成记录：已经完成当天刀解后，仍会在收刀缺位时继续按缺口刀解。每页选择许可名单中的刀剑前，必须读取当前已选数量，并且只选择剩余所需数量，不能因同页存在多把许可刀剑而超选。当天锻刀完成记录只阻止新建锻刀，仍必须进入锻刀状况页收取已完成刀剑。无合成素材、刀解素材不足或未完成 3 次锻刀均不得标记为完成。

台账由 `DailyTaskCompletionService` 统一读写，并通过自定义 action 接入 pipeline。升级时需同时保留日志文件格式、检查/写入 action 注册，以及日课各项目的成功路径和跳过路径。

### `work-records.name-dialog-registration`

工作记录的保存、另存与重命名均通过 `WorkRecordNameDialogViewModel` 输入名称。该 ViewModel 必须在 `App.ConfigureViews` 注册为 `WorkRecordNameDialogView`；上游升级时即使两个源文件仍存在，也不得遗漏这条映射，否则保存会提示找不到对应视图。

### `queue.resume-and-continue-on-error`

保留失败继续、用户停止、断点续跑、轮次统计和结束后操作边界。必须基于新上游 `RunResult`/取消模型重放，并覆盖关键队列路径。

任务队列的外层异步执行必须等待 `ExecuteTasks` 完整返回后再进入停止流程。调用 `TaskManager.RunTaskAsync` 时应使用 `Func<Task>` 重载，不能把异步 lambda 绑定到 `Action` 重载，否则会在第一个 `await` 后提前执行停止逻辑，并将正常完成的任务记录为“手动停止”。

任务队列需要在普通任务之间自动插入 `GoHome`，确保下一个游戏任务从本丸开始；最后一个任务后不插入。MFAA 提供的特殊任务通过 `Entry` 标识（如 `CountdownAction`、`WebhookAction`）识别，特殊任务前不得插入回本丸。特殊任务集合由任务队列策略与任务添加界面共用，升级时不得恢复为无条件插入。

有限重复的 MAAFW 任务在每轮成功后必须向 GUI 日志输出“任务完成：任务名 进度 X/Y”。该行为位于 `MFATask.Run` 的循环内，`MaaAction` 返回成功后触发；无限重复与单次任务不输出。升级时不得因将 action 改为返回 `MaaJobStatus` 而遗漏该输出。

资源侧 `CompleteCurrentTaskAction` 用于提前结束当前队列项，不可调用全局 `Stop`、清空队列或取消运行令牌。`MaaProcessor` 必须在执行每个队列项时登记该项，action 通过其所属 `MaaTasker` 定位处理器并请求跳过当前项的剩余重复次数。请求在当前 MaaFW 流程成功返回后消费：该轮不增加完成轮数，当前项以成功状态结束，随后保留自动插入的回本丸项和队列后续项。请求必须是队列项私有的一次性状态，不能因同名任务或多实例泄漏。

提前结束时，`MFATask` 必须经由 `TaskQueueViewModel` 以 Warning 记录级别写出 `[任务名] 任务结束 原因：原因` 词表行。`WorkRecordBuilder` 将其解析为工作记录的特殊情况；不可仅输出普通 GUI 日志，否则结束原因不会进入工作记录。

任务失败时除保留界面内提示与外部通知外，还必须调用 `ToastNotification.Show` 发送系统通知，使失败、成功的任务结束反馈保持一致。升级时检查失败分支，避免只剩界面内日志而用户错过失败结果。

合战场任务的轮数与其它任务完全一致：`repeatable` 为 `true`，次数取自任务级 `repeat_count`，「过去」与「异去」共用同一份设置。`MaaProcessor.CreateNodeAndParam` 直接使用 `InterfaceItem.RepeatCount`，不得恢复历史上「从 `过去/异去` 下级选项 `异去_重复次数` 读取轮数」或「给过去写死三轮」的定制逻辑（该逻辑曾两次被上游升级覆盖）。

「异去」每圈流程自身停在 `S_IsIsekaiRegionEnd`（无 `next`）即一圈结束，无需额外定制。「过去」每一圈打完回到本丸后同样要把控制权交回队列，因此由资源侧自定义识别 `SortieRoundDoneRecognition` 判断「本次任务运行是否已经出阵过」（判定依据为 `S_SortieSuccess` 的命中计数配合 `TaskJob.Id` 与基线），命中时走 `S_IsSortieRoundDone`（打点「[合战场] 完成一圈」＋`"next": []`）结束本轮运行。判定必须走 `next` 正常分支，不得改用「动作返回 false 走 `on_error`」：MaaFW 的 `SaveOnError` 全局选项会在 on_error 触发时写入 `debug/on_error/` 截图，按圈数刷屏。

引擎运行标识在每次 `post_task` 变化，命中计数的生命周期以基线比较兜底；刷花链（`SF_ClickSortieNow` / `SF_IsHome`）不参与轮次判定，回到主链后继续。

### `task.sync-expedition-reuse`

同步后勤是 MATR 对 MFAAvalonia 远征流程的定制扩展。合战场、地下城、陆联、战术强化、江户潜入与一键日课启用“同步后勤”时，`MaaProcessor.CreateNodeAndParam` 必须从当前实例的“后勤”任务读取“部队一”至“部队五”的选项，并将这些选项的 `pipeline_override` 合并到当前任务。

远征队伍的检查开关必须沿用后勤任务的配置：选择“休息”时对应的 `E_CheckTeamN` 为 `false`，选择远征地图时对应的 `E_CheckTeamN` 为 `true`，同时合并对应的地图选择参数。同步选项本身不得固定把五个 `E_CheckTeamN` 全部设为 `true`，否则休息队伍会进入 `E_SelectMapN`，并可能因自定义选图动作返回失败而重复进入远征页面。

“修刀”的“筛选条件”是 checkbox 多选项。多个刀种或伤势的 `pipeline_override` 必须先递归合并到同一个 `E_FindRepairableSword` 覆盖对象；不能按 case 分别生成同名 node 的多层覆盖，否则 MaaFramework 只会使用最后一层，表现为只点击最后一个筛选条件。

该逻辑还负责同步修刀、内番和远征刷新间隔；升级时必须保留实例配置重新读取兜底，避免配置缓存为空或被惰性枚举污染时丢失队伍设置。2026-09-05 的 MFAAvalonia v2.16.1 升级曾移除整段同步配置复用逻辑，导致已配置的第二至第五队不再检查；后续修复不得只在 `interface.json` 的同步选项中补充固定启用开关。

一键日课的接入点是 `DT_IsHome.next → Expedition`，与出阵任务的 `*_IsHome.next` 同形；后勤在 `E_AllTeamsBusy` 处结束，该 node 在任务层覆盖为「关闭队伍状态面板（`repeat: 2`、`repeat_delay: 500`）→ `DT_PrepareHub`」，新增的回落 node 是 `DT_WaitRefresh`。日课不做倒计时与队伍面板 OCR 扫描，因此不覆盖 `E_TimerStart`，`E_AllTeamsBusy` 的 action 必须写死为 Click——否则开启“远征智能调度”时全局覆盖会把该 node 改成 `DoNothing`，队伍状态面板不会关闭。

### `recovery.game-and-emulator-restart`

MFAAvalonia 2.16.1 升级曾丢失 `93e62c16` 引入的动作循环与无回调检测接入。必须保留 `TaskRecoveryMonitor`、任务回调记录和 `TryRunTasksAsync` 中独立于底层 `Wait` 的检查循环。检测只在 ADB 普通任务且开启「卡死重启」时启用，静默阈值沿用「卡死等待时间」（默认 120 秒），排除智能等待、人工弹窗及已经开始的游戏恢复。停止和完成任务时撤销检测。

日课的画面检测枢纽、各业务枢纽与各阶段 Gate 在 `DailyTask.json` 中写死 `timeout: 120000`（共 49 个 node），不再依赖「卡死重启」选项覆盖等待时间；该开关对日课只切换 `on_error` 指向的恢复枢纽（`DT_RestartGame` 与 `DT_RestartGameReturn*`）。

恢复时先异步请求底层停止，再执行外部游戏与模拟器恢复，不能先等待挂起的底层停止；底层退出确认有 30 秒上限。恢复后重连并重新执行当前中断任务，保留外层队列及已完成轮次；恢复失败必须停止队列，不能继续向未退出的执行器追加任务。恢复动作应显式接收所属处理器和取消令牌，避免切换实例后操作错误设备，手动停止后不得继续启动任务。MuMu 的 Windows 命令不得在其他平台执行。

连接失败时，开启「尝试启动模拟器」「重启 ADB Server」「关闭并重启 ADB 进程」中的任意一项，并填写有效游戏路径，均应允许沿用启动设置启动模拟器；首次连接没有设备地址时也必须进入此路径。必须保留已保存 ADB 目标的严格匹配，不能误连其他实例。

保留 MATR 对 MFAAvalonia 卡死恢复动作的二次开发。游戏客户端由全局设置首位的“客户端类型”统一配置：选择“官服”时固定使用 `com.youzu.djlw`，选择“其它”时在同页展开带问号说明的包名输入。`RestartGameAction` 必须读取此项；旧版“卡死重启”中的“目标应用”包名须在加载时迁移为新配置。优先通过 `cmd package resolve-activity` 解析实际启动 Activity，再使用 `am start -n` 启动，不能依赖部分模拟器缺失的 `monkey` 命令。

恢复流程必须区分模拟器重启失败与游戏启动失败：模拟器重启失败时记录错误并让恢复动作失败；模拟器已恢复但游戏启动失败时记录警告，并将控制权交回任务 pipeline，继续尝试游戏图标、登录和主枢纽流程。升级 MFAAvalonia 的自定义动作注册、ADB 配置读取或任务错误处理时，必须保留该行为。

所有恢复事件必须同时写入 GUI 和文件日志，并按阶段使用 `[重启游戏]` 或 `[重启模拟器]` 词表。卡死监控的首条 Warning 固定为“检测到游戏疑似卡死：<原因>”，其中循环原因统一简化为“动作循环”，但保留触发 node 与动作；游戏重启失败后必须输出“游戏重启失败，重启模拟器”，模拟器重启失败输出“模拟器重启失败，停止任务”。成功阶段使用 Info，不显示在工作记录的特殊情况中。

合战场过去模式的“避战检非”通过两个检非识别 node 写入 Warning 词条 `[重启游戏] 遭遇检非` 后调用 `RestartGameAction`。该重启必须传入 `log_auto_recovery: false`，不能误记为卡死恢复；`GuiLogAction` 写入 Warning 时必须同时在 GUI 与文件日志记录同一条词表，工作记录须将其显示为“遭遇检非违使，重启游戏”。

### `adb.mumu-emulator-extras-input`

ADB 输入方式设为“自动”时，`MaaProcessor` 必须为名称包含 `MuMu` 的设备补充 `EmulatorExtras`，并保留 MaaFramework 已发现的全部输入方式。MaaFramework 某些 MuMu 版本会返回缺少该位的掩码，导致连续触控退回至不支持拖动的 `AdbShell`，使习合素材列表无法翻页。

用户显式选择的输入方式不得被覆盖。验证自动模式下 MuMu 的输入掩码包含 `EmulatorExtras`，并通过多页习合素材列表确认连续上滑可用。

### `runtime.resource-path-and-packaging`

保留资源大小写兼容、桌面发布结构、图标与 `libloader` 启动钩子；不恢复 Python agent。验证完整包资源加载、Windows/macOS 发布及 agent 排除。

Windows 发布目录可能同时在 `runtimes/libs` 与根级 `libs` 放置托管依赖。发布包统一使用 `runtimes/libs`，并且 `MATR.runtimeconfig.json` 的 `NetBeautyLibsDir` 与 `SubdirectoriesToProbe` 必须都指向该目录；打包脚本仅在输入目录存在根级 `libs` 时合并其内容，但必须随后以 `runtimes/libs` 的本次发布产物覆盖同名文件，防止残留的旧核心程序集进入压缩包。

桌面项目的 `AssemblyName` 与 `OutputName` 必须保持为 `MATR`。上游默认的 `MFAAvalonia` 输出名会使 `pack_win.ps1` 误用开发根目录残留的旧 `MATR.exe`，从而将旧核心程序集打入测试包；Windows 打包必须先 `dotnet publish`，将宿主文件和 `runtimes` 同步到开发根目录，再以根目录为输入执行 `pack_win.ps1`。

MATR 的资源包固定在 `assets/`：`AppPaths.InterfaceJsonPath`、`AppPaths.InterfaceJsoncPath` 和 `AppPaths.ResourceDirectory` 必须分别解析到 `assets/interface.json`、`assets/interface.jsonc` 与 `assets/resource`。上游若改回程序根目录布局，会触发默认资源兜底并显示空任务列表。

`VersionChecker` 必须兼容完整程序包和仅资源包的更新结构：完整程序包中的 `assets/interface.json` 与 `assets/resource` 需要保留 `assets` 路径并识别程序文件；仅资源包根目录中的 `interface.json` 与 `resource` 则必须映射到 `assets/interface.json` 与 `assets/resource`。增量更新的资源文件、目录删除和目录创建，以及更新结束时的版本元数据回写，都必须使用同一套路径映射，避免更新后程序从错误的根目录读取资源、在根目录创建 `interface.json` 或提示资源加载失败。

`MaaProcessor.ProjectDir` 必须解析为 `interface.json` 所在目录（MATR 即 `assets/`），并且所有 resource 路径均以它替换 `{PROJECT_DIR}`。`MaaProcessor.CheckInterface` 的默认资源路径必须为 `{PROJECT_DIR}/resource/base`；两项必须配套，最终解析到 `assets/resource/base`，不得在程序根目录创建不需要的 `resource/base/pipeline/sample.json`。

### `resource.sword-drop-recognition`

MATR 使用 `SwordDropLogAction` 记录合战场、地下城、联队战和战术强化中的刀剑掉落，并支持播报和初掉落截图。初掉落不使用动画文字 OCR，而是检查 1280×720 基准画面的 `[180,397,8,30]` 区域；区域内所有像素都必须命中 RGB `[195,13,24] ±1`，命中后才进入初掉落截图与刀名识别流程。

特化和极化仍由动画 ROI 的 OCR 识别。升级资源或自定义动作时，必须保留四个 pipeline 挂载点、颜色匹配规则和 `debug/sword_drop/` 截图行为。

### `resource.naiban-outfit-swordbook-link`

内番可联动本丸刀帐：识别到内番服时，基于已保存的刀帐状态同步拥有与内番服标记；同名条目优先更新已拥有条目中序号最大的记录，没有已拥有条目时更新最小序号记录并同时登记拥有。自动刷取内番服开启后，入口先确认今日内番表、移除现有安排，再优先选择已拥有且缺少内番服的两把刀剑；不足的位置选择任意可用刀剑。

升级时必须保留运行期目标固化、同名序号选择规则、开关对入口 `next` 的覆盖，以及资源 `custom` 目录中三项内番选刀 action 的动态编译。关闭自动刷取时必须保留原有直接开始内番流程。

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

### `external-notification.qmsg-v3`

QMsg 酱已升级至 v3 推送接口。`ExternalNotificationHelper.QMsg.SendAsync` 必须向 `{serverUrl}/v3/send/{apiKey}` 提交表单参数 `qq` 与 `msg`；不得恢复旧版的 `/send/{apiKey}` 路径，否则外部通知设置中的 QMsg 发送测试会失败。

升级时保留该接口路径，并通过测试项目的源码回归断言确认。用户填写的服务器地址为 `https://qmsg.zendee.cn`，机器人 QQ 字段可留空。

### `timer.windows-scheduled-wakeup`

MATR 在 Windows 上把应用内定时器同步为系统计划任务，补足「MATR 与模拟器都未运行」时的定时启动场景。`TimerModel` 在定时时间、重复规则、动作、实例与强制定时启动开关变化后调用 `PlatformTimerScheduler.RequestReschedule()`，`WindowsScheduledTaskSyncService.Initialize()` 在启动时接管该回调并按 1.5 秒去抖同步。不要把系统任务逻辑合并进 `TimerModel`，也不要让上游定时器重构丢掉这个接管点，否则界面上的定时器变化不会再同步到系统计划任务。

系统计划任务由 `schtasks.exe` 与任务 XML 创建，统一放在 `MATR` 任务文件夹下，命名为 `MATR.Timer.<安装目录归属令牌>.<定时器序号>`。归属令牌取当前 `MATR.exe` 完整路径的哈希前八位，使开发目录与运行目录的计划任务互不覆盖。任务说明写入配置指纹，指纹覆盖任务模板、命令行参数、程序路径、工作目录与重复规则，但不包含开始边界日期，因此同一个定时器不会每天被重建。

计划任务以当前登录用户身份运行、不要求管理员权限、开启「错过计划后尽快运行」，并且不限制运行时长，避免长时间运行的任务被系统终止。动作固定为 `--autostart --instance <实例 ID>`，开启强制定时启动时追加 `--forceStart`，启动后复用既有的单实例互斥锁、命名管道转发与 `StartCommandLineAutoRun`。定时器动作为「停止任务」、未选择重复日期、被禁用或所选实例失效时不创建计划任务，并清理本安装目录归属的旧任务；实例列表尚未加载时跳过同步，避免误删。非 Windows 平台不创建计划任务，设置界面只显示状态提示。

计划任务独立于 MATR 进程的运行状态，这是功能的既定语义：关闭 MATR 不会删除计划任务，只有禁用定时器、把动作改成「停止任务」、删除定时器或实例失效时才会删除。退出程序前会补跑尚未执行的同步请求，并在日志中记录仍保留的计划任务数量；同步执行期间到达的改动请求会在本轮结束后补跑，不得改回"进行中直接丢弃"。设置界面的说明文案必须保留「关闭程序不会取消定时」这一提示，否则用户会误以为关闭程序即取消定时。

计划任务的并行实例策略必须是 `Parallel`，不得改回 `IgnoreNew`。MATR 被系统计划任务拉起后会一直运行，`IgnoreNew` 会让任务实例长期停留在「正在运行」，系统随即忽略后续每天的触发：2026-09-11 实测任务处于运行状态时再次触发返回 `-2147020576`（任务已在运行），且没有拉起任何新进程。`Parallel` 下每次触发都会启动一个 MATR.exe，已有实例在运行时该进程转发命令后立即退出，未运行时则成为程序本体，因此既不会重复开窗，也不会漏掉触发。
