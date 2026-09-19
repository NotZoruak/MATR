# 自定义动作迁移纯 pipeline 设计

> 状态：迁移分任务推进中；一键日课、海陆联队与秘宝之里已完成部分迁移
> 最后更新：2026-09-19
> 范围：`assets/resource/base/pipeline/*.json` 与 `assets/interface.json` 中实际引用的自定义动作与自定义识别

## 背景

项目规范要求「能用纯 pipeline 表达的逻辑一律用纯 pipeline 实现，不新增 custom action」。但代码库里已经积累了 60 多个自定义动作，其中有相当一部分的行为本质上等价于 pipeline 内建能力：写一条 GUI 日志、点一个固定坐标、按矩形随机点击、等一段固定时间。这些实现放在 C# 里，迁移成本随版本累积，也把本该由 pipeline 承载的业务语义藏进了代码。

本文档把这些「本可以纯 pipeline 实现」的项列出来，给出替代方案、迁移前置条件和批次建议，供后续逐批迁移使用。迁移不采用按同类型 node 集中替换的方式；实施时应依次重构现有任务，在任务重构过程中以协议能力替代非必要 custom action。本文档只做设计，不包含代码或 pipeline 改动。

统计口径：扫描 `assets/resource/base/pipeline/*.json` 与 `assets/interface.json` 里的 `custom_action` 与 `custom_recognition` 字段。开工前首次扫描统计到 372 处自定义动作引用、26 处自定义识别引用，涉及 54 个动作名与 7 个识别名，完整数据见文末附录。随迁移推进的当前实测为 339 处自定义动作引用（51 个动作名）、25 处自定义识别引用（6 个识别名），`focus` 已在 6 个文件中共启用 33 处。

## 已完成改动与当前进度

迁移已经开工。前置条件（`special:` 标记、工作记录「特殊情况」链路、`anchor` 路由）已落地并有实机验证；一键日课、海陆联队与秘宝之里均已迁移完能由 `Click` 与 `focus` 表达的部分，三者剩余项分别记录在对应的小节里。后续改动必须按本节状态继续推进，不能把尚未实现的分类能力当作已可用。

| 项目 | 状态 | 已完成内容或下一步 |
|---|---|---|
| 官方协议与参考实现 | 已完成 | 已归档 MaaFramework 官方文档到 `docs/maafw-official/`，并引入只读参考仓库 `_reference/MaaEnd/`；新增或修改任务设计时先核对二者。 |
| 一键日课盘点 | 已完成 | 已统计 `DailyTask.json` 中的 custom action 与 custom recognition 引用，并确认状态台账、容量判断、战斗风险判断、素材选择、掉落识别等逻辑暂不属于首批纯 pipeline 迁移对象。 |
| `special:` 标记识别 | 已完成 | `FocusHandler` 已在官方消息键形式的对象、字符串数组、字符串三种 `focus` 写法中识别开头精确匹配的 `special:`，并在实时展示前移除该前缀。普通文本保持原样。该行为已有自动化测试覆盖。 |
| 特殊情况写入链路 | 已完成 | `FocusHandler` 已把 `special:` 的识别结果传递给 `AddMarkdown(recordAsSpecial: true)`；该路径以 Info 级别写入 `[Record][Special]`，不借用 Warning。 |
| 工作记录解析器 | 已完成 | `WorkRecordBuilder` 已识别 `[Record][Special]` 并归入特殊情况，同时保留 `WRN` → 特殊情况的既有规则；已覆盖正常特殊结果、真实 Warning、普通记录三类测试。 |
| 一键日课重构 | 进行中 | 已完成 9 条完成日志、5 条跳过日志与 5 条演练战败日志迁移，及 6 个本次运行跳过出口的 `anchor` 迁移。锻刀容量不足且未开启刀解、演练战败使用 `special:` Info 记录；本次运行跳过仅控制路由、不输出日志。anchor 机制已完成实机验证；刀剑掉落与状态台账类逻辑尚未改动。 |
| 修刀与刀装补充公共链 | 已完成（海陆联队、秘宝之里） | 已把修刀链与刀装补充链抽到 `Repair.json` 与 `EquipSupply.json`，公共 node 不带任务前缀，出口统一改用 `[Anchor]Hub` / `RepairDone` / `RepairAborted` / `SupplyDone`，任务在入口 node 声明锚点；海陆联队与秘宝之里均已改为引用公共链并删除各自任务内的同名节点，已登记到 `docs/复用节点清单.md`。秘宝之里的刷花子枢纽 `HF_` 链本次不纳入，后续会单独抽成一条任务中刷花的公用流程，详见下文「秘宝之里实施记录」。下一步接江户潜入与战术强化。 |
| 海陆联队重构 | 进行中 | 已完成 `RB_SortieSuccess`、`RB_IsConfirmPurchase`、`RB_TerminateRound` 三条日志打点的 `focus` 迁移。剩余 9 处引用中，`RB_CheckCaptainDamage` 的 `CaptainDamageAction` 属于批次一候选，`RB_RetreatOnCaptainDamage` 的 `LogAction` 借 Warning 级别进入特殊情况、属于批次二，其余 7 处均在「明确保留」清单内。详见下文「海陆联队实施记录」。 |
| 秘宝之里重构 | 已停止（等待刷花公用流程） | 已完成难度选择 node 合并，以及 `HP_IsMarching`、`HP_IsConfirmPurchase`、`HP_SortieSuccess`、`HP_TerminateRound` 四处日志打点的 `focus` 迁移；剩余 10 处自定义动作引用全部属于「明确保留」清单，唯一的候选是 `HF_Hub` 的刷花打点，随 `HF_` 链一并等待刷花公用流程。详见下文「秘宝之里实施记录」。 |
| 后续任务重构 | 进行中 | 海陆联队已完成修刀与刀装补充的公共链拆分；接着处理本丸后勤与常驻作战。 |

`special:` 只是 MATR 约定的内容前缀，不是 MaaFramework `focus` schema 的字段。当前实现会在实时展示前清理该前缀，并仅对日志文件写入 `[Record][Special]`；实时日志和文件日志均保持 Info 级别。该链路已经可以承载迁移后的正常特殊结果，但现有 Warning 词表仍须逐条评估，不能不分语义地全部替换为 `special:`。

### 一键日课实施记录

2026-09-19 已完成第一小批：完成日志。登录奖励、暖心礼包、合成、刀解四条原本仅用于写日志的插入式 node 已删除，日志改挂到对应的完成台账动作 `Node.Action.Succeeded` 回调；锻刀完成、演练三胜、演练五位置、任务奖励、邮件领取五条保留原分支出口，仅将 `GuiLogAction` 替换为 `focus`（空操作由 `action` 缺省表达，不再显式写 `DoNothing`），保持原 `next` 路径不变。此次不涉及 `special:`，所有消息均为普通 Info 完成记录。

同日完成第二小批：跳过日志。合成无可用对象、锻刀未开启、无空闲锻刀位、演练跳过强敌四条改为普通 Info `focus`；「待收取刀剑超过空余刀位，未开启刀解」属于正常结束但需回顾的结果，改为 `special:` 的 Info `focus`，写入工作记录的特殊情况。五个分支出口均保留，避免改变现有跳过与返回逻辑。

同日完成第三小批：演练战败日志。五个位置的战败识别 node 保留原有 `max_hit: 1`、识别范围和回到对手选择的 `next`，仅将 `GuiLogAction` 替换为带 `special:` 的 `focus`。战败结果会以 Info 级别写入日志，并在工作记录中归入特殊情况。至此，`DailyTask.json` 不再引用 `GuiLogAction`。

### 一键日课本次运行跳过的 `anchor` 迁移（已实施并完成实机验证）

本次运行跳过不写入每日完成台账，且只影响当前的一次日课任务链。现有实现使用 `DailyTaskStepSkipAction` 将项目名写入 `DailyTaskCompletionService` 的内存集合，`DailyTaskStepRecognition` 先读取该集合，入口 `DailyTaskRunResetAction` 再在每次运行开始时清空集合。该组状态只服务于总路由器的候选项筛除，可由 MaaFramework 的 `anchor` 直接表达。

未重写 `DT_ProjectRouter` 的业务顺序，只把已有本次运行跳过需求的六个直接候选替换为锚点候选：同步后勤、合成、锻刀、演练、任务奖励、邮件。登录奖励、暖心礼包、刀解继续保持直接候选。日课入口 `DailyTask` 在每次执行时通过 `anchor` 字段将六个路由锚点分别初始化为对应的 `DT_Step*`；这一步显式覆盖上次任务链可能留下的运行期锚点状态。

当某项目决定本次跳过时，原 `DT_*SkipCurrentRun` 出口已改为空操作（`action` 缺省，不再显式写 `DoNothing`），并将对应路由锚点设置为空字符串。例如合成无可用对象时，`DT_MixSkipCurrentRun` 写入 `"anchor": { "DT_RouteMix": "" }`。官方协议规定空字符串表示清除该锚点；`DT_ProjectRouter` 中的 `[Anchor]DT_RouteMix` 随后解析不到目标，候选会被跳过，路由器继续尝试下一个项目。各出口原有的 `next` 收尾路径保持不变。

六个跳过出口不配置 `focus`：本次运行跳过只用于控制路由，不输出 GUI 或文件日志，也不写入工作记录。已删除 `DailyTaskStepSkipAction`、`DailyTaskRunResetAction`、动作注册、`DailyTaskCompletionService` 的运行期跳过内存集合及其读写清理方法，并移除 `DailyTaskStepRecognition` 对该内存状态的读取；`DailyTaskCompletionMarkAction`、每日完成台账和 `DailyTaskStepRecognition` 对持久化完成次数的判断保持不变。

已完成 JSON 回归测试、桌面构建与实机验证。本次验证确认同步后勤的当次跳过不会再显示于 GUI、写入 `.log` 或进入工作记录；其余五个跳过出口使用相同的无 `focus` 结构。锻刀槽位均在进行中时，`DT_ForgeClaimCompletedHub` 会等待超时并按既有 `on_error` 走“无空闲锻刀位”分支，框架留下的 `invalid node id` 记录是该既定设计的副产物，不属于 anchor 迁移异常。

### 海陆联队实施记录

`RegimentBattle.json`（旧 `LRentaisen.json`，已随本轮重构移除）在完成修刀与刀装补充公共链拆分后，又迁移了三条能由 `focus` 表达的日志打点。

`RB_SortieSuccess` 原本用 `LogAction` 打点，现改为模板识别加上挂在 `Node.Action.Succeeded` 的 `focus`，文案 `[海陆联队] 出阵`，node 不再写 `action`。`RB_IsConfirmPurchase` 原本由 `LogAction` 同时承担点击与打点，现改为 `Click` 的 `target` 取原 `click` 矩形，`focus` 文案带 `special:` 前缀，写入工作记录的特殊情况。`RB_TerminateRound` 保留原有的 OCR 识别与 `inverse`，只把 `LogAction` 换成 `focus`，文案 `[海陆联队] 完成一圈`。三条文案逐字保留，工作记录的出阵计数与完成一圈计数不受影响。

剩余 9 处引用中，`RB_CheckCaptainDamage` 的 `CaptainDamageAction` 只写一条不带词表前缀的 GUI 日志（`检测到队长重伤，撤退撤退`），属于批次一候选，改成识别 node 的 `focus` 即可；`RB_RetreatOnCaptainDamage` 的 `LogAction` 是 Warning 级别加点击（`[海陆联队] 队长重伤撤退`），须先在批次二判定它属于正常业务结果还是真正的 Warning。其余 `CompleteCurrentTaskAction` 两处、`GoalPtCheckAction`、`DragCaptainAction`、`SwordDropLogAction`、`TeamSwitchCheckAction`、`RestartGameAction` 均在「明确保留」清单内。

### 秘宝之里实施记录

秘宝之里（`Hanapai.json`）未列入「近期实施范围与顺序」的四个任务，按并行安排先行处理。本轮只迁移能由 `Click` 与 `focus` 表达的日志打点，刷花链 `HF_` 整体留待后续的专用公用流程。

准备工作：`HP_SelectDifficulty` 原本是不带识别与动作的空壳跳转 node，真正的点击在 `HP_ClickDifficulty`。两者已合并为一个 `HP_SelectDifficulty`，保留原 `pre_delay`、`Click` 的 `target` 与 `next`，`interface.json` 中「选择难度」易／普／难／超难四个 case 的覆盖 key 同步改名；`HP_IsEventPage` 与 `HP_CheckGoalPt` 的 `next` 本来就指向 `HP_SelectDifficulty`，无需改动。合并后少了一次跳转，原来两个 node 各自承担的默认 `pre_delay` / `post_delay` / `rate_limit` 只剩一份，难度点击会比原先早约半秒发生。

日志打点迁移：`HP_IsMarching` 原本由 `LogAction` 同时承担点击与打点，现改为 `Click` 的 `target` 取原 `click` 矩形、`focus` 挂 `Node.Action.Succeeded`；`HP_IsConfirmPurchase` 同样是点击加打点，改用 `Click` 加 `focus`，文案带 `special:` 前缀；`HP_SortieSuccess` 与 `HP_TerminateRound` 只是打点，`focus` 之外不再写 `action`。四条日志的文案逐字保留，`WorkRecordBuilder` 的出阵计数、行军计数、完成一圈计数与购买门票归类与迁移前一致。

空操作表达：`focus` 只用于打点时，node 不再显式写 `"action": {"type": "DoNothing"}`。官方协议中 `action` 可选、默认即 `DoNothing`，省略后仍会发出动作阶段回调。已用运行日志核实：`84d52a63` 时期的 `FC_VerifySelectedTeam` 只写 `recognition` / `next` / `on_error`，日志里仍以 `"action":"DoNothing"` 正常输出 `Node.Action.Starting` 与 `Node.Action.Succeeded`，因此挂在 `Node.Action.Succeeded` 上的 `focus` 不会失效。

剩余项：`Hanapai.json` 还有 10 处自定义动作引用、7 个动作名，全部属于「明确保留」——`CompleteCurrentTaskAction` 两处、`RestartGameAction` 两处、`FatigueCheckAction` 两处，以及 `SwordDropLogAction`、`GoalPtCheckAction`、`DragCaptainAction`、`HF_Hub` 的 `LogAction` 各一处。其中 `HF_Hub` 属于刷花链，本轮不动。

刷花公用流程（后续专门实施）：刷花状态机目前在三个任务里各存一份，`SF_`（合战场）、`UF_`（地下城）、`HF_`（秘宝之里），识别参数、动作参数、等待与超时基本一致，只有前缀、打点文案与回主枢纽的目标不同。后续会像修刀链与刀装补充链那样，专门抽成一条任务中刷花的公用流程：公共 node 不带任务前缀，出口由任务在入口 node 声明锚点，任务侧只保留「疲劳处理-刷花」选项对入口 node 的启用覆盖。`HF_Hub` 的 `[秘宝之里] 刷花` 是工作记录刷花次数的来源（非「后勤」前缀计入 `FlowerBrushCount`），抽链时不能写死在公共链里，要由挂载点或任务参数注入。

## 判定标准

满足下列任一条件，视为「本可以用纯 pipeline 实现」：

- 动作只输出日志或提示，不读写外部状态，且输出内容在 pipeline 里是静态文本。
- 动作只做识别命中后的点击、滑动、按键，坐标来自固定矩形或识别命中矩形加固定偏移。
- 动作只做固定时长等待，或者等待某个画面出现、消失。
- 动作只做次数限制、分支跳转、单次打点这类流程控制。
- 动作的差异只来自任务选项，且选项在任务启动前就已经确定，可以由 `pipeline_override` 静态注入。

下列情形不属于本清单，应继续保留 custom action：

- 读写配置文件、识别草稿、完成台账、调度时间等持久化状态。
- 依赖跨 node 的可变运行期状态，例如当前部队、已选素材、本轮胜利次数。
- 需要数值比较、算术、字典匹配、正则归一、多帧轮询后取最优值。
- 需要调用宿主能力，例如提前结束队列项、重启游戏、发送系统通知或网络请求。
- 需要逐像素分析图像，或需要把自己撰写的 OCR 结果与刀帐目录做联合校验。

## pipeline 内建能力对照

| 自定义动作里的常见写法 | 纯 pipeline 对应能力 |
|---|---|
| `LoggerHelper.Info` + `AddLog` 输出 GUI 日志 | node 的 `focus`，`display` 为 `log` |
| `AddLog` 输出系统通知 | `focus` 的 `display` 追加 `toast` 或 `notification` |
| `context.Click(x, y)` 固定坐标 | `action.type = "Click"`，`target` 写矩形 |
| 在矩形内随机取点点击 | `Click` 的 `target` 本身就是矩形随机取点，语义一致 |
| 识别命中后点击命中位置加偏移 | 识别 node 加 `Click`，`target` 用当前识别结果，`target_offset` 写偏移 |
| 等待画面出现 | 在 `next` 里加该画面的识别 node |
| 等待画面消失 | 识别 node 加 `inverse`，或使用 `post_wait_freezes` 限定变化区域 |
| 固定时长等待 | `pre_delay` / `post_delay`，需要零延迟时显式写 0 |
| 限制打点次数 | `max_hit`，语义为「该 node 最多被成功识别多少次」 |
| 限制循环次数 | `repeat`、`max_hit` 配合 `next` 分支 |
| 按顺序分流 | `next` 顺序识别，首个命中即执行，天然覆盖首轮截图命中 |
| 横切分支挂载 | `[JumpBack]`，不要写成主线步骤 |
| 选项驱动的坐标差异 | `select` 的 case 里用 `pipeline_override` 覆盖对应 node 的 `target` 或 `enabled` |
| 本轮任务结束 | node 不再有可走分支，流程自然结束，无需动作主动调用宿主 API |

`focus` 的实测实现路径：`MaaProcessor` 的回调收到带 `focus` 的 node 后交给 `FocusHandler.DisplayFocus`，新协议以消息类型为键，可用键为 `Node.Action.Starting`、`Node.Action.Succeeded`、`Node.Action.Failed` 等 `MaaMsg` 常量值，值支持字符串、字符串数组或 `{ "content": ..., "display": ... }` 对象，`display` 缺省为 `log`。`content` 会经过 i18n key 与 `{image}` 占位符解析。

`Click` 的 `target` 有三种写法：写矩形表示固定区域，写 `true` 表示使用当前 node 的识别命中区域，写 node 名表示使用该 node 的识别命中区域；`target_offset` 在命中区域之上再叠加偏移。识别命中后再点击的迁移都依赖第三种写法加偏移。

## Pipeline 字段用法参考

本节记录迁移中会直接使用的协议字段。字段的官方语义以本地归档 `docs/maafw-official/` 为准；字段在 MATR 中最终如何展示或落盘，还需以本项目的 Client 实现为准。

### `focus`

`focus` 是挂在单个 node 上的消息模板字典。MaaFramework 只会把它原样带入回调详情；消息内容如何解析、显示和记录完全由接收回调的 Client 决定。

键使用回调消息类型，值使用字符串或模板对象。迁移普通完成日志时，统一挂在 `Node.Action.Succeeded`，使日志只在动作真正成功后输出，避免识别阶段的高频回调造成重复日志与额外取图开销。

```json
"focus": {
  "Node.Action.Succeeded": {
    "content": "日课 登录奖励领取完成",
    "display": "log"
  }
}
```

模板对象的官方字段如下：

| 字段 | 类型 | 作用 |
|---|---|---|
| `content` | string | Markdown 文本、以 `$` 开头的国际化 key、文件路径或 URL；可使用回调详情中的占位符，例如 `{task_id}`、`{node_id}`、`{action_id}`、`{elapsed}`、`{anchor}`。 |
| `display` | string 或 string[] | 渠道标识，可用 `log`、`toast`、`notification`、`dialog`、`modal`；省略时默认为 `log`。它不定义具体输出路径。 |
| `trace` | boolean | 是否将本次结果上传遥测；本项目禁用遥测，当前不需要配置。 |

对 Pipeline 中的 `focus`，实际应使用 `Node.*` 消息键，例如 `Node.Action.Starting`、`Node.Action.Succeeded`、`Node.Action.Failed`、`Node.Recognition.*`、`Node.NextList.*`、`Node.WaitFreezes.*` 与 `Node.PipelineNode.*`。键必须与回调消息完全一致，拼写错误会静默失效。普通日志不使用 `Node.Recognition.*`，因为它们的触发频率远高于动作阶段。

MATR 当前将 `display: "log"` 分发到 `FocusHandler` 的 `AddMarkdown`：它会同时进入实时日志面板和实例日志文件，并以 Info 级别落盘。这是 MATR 的 Client 行为，不是 MaaFramework 协议保证。`focus` 没有 Warning 级别字段，不能自行编造 `display: "warning"`；需要归入工作记录「特殊情况」的正常业务结果，应依赖下文的工作记录分类优化，而不应伪装成 Warning。

本项目还兼容旧式 `start`、`succeeded`、`failed`、`toast`、`aborted` 字段，但它们不是官方当前推荐写法。新增或迁移内容一律使用消息键形式。完整回调族、历史兼容差异与性能细节见本文末尾的「focus 字段完整参考」。

## 迁移候选清单

### 第一优先：日志与提示类

这一组数量最大、风险最低，迁移收益也最直接。迁移方式是把独立的日志 node 改成 `focus` 挂载点，或者直接挂到上游已有业务 node 上，随后删除纯日志 node。

| 动作 | 引用处数 | 当前职责 | 纯 pipeline 替代 |
|---|---|---|---|
| `GuiLogAction` | 48 | 把 `message` 写到 GUI 日志，`warn:` 前缀转警告 | node 的 `focus`，`display: log` |
| `LogAction` | 125 | 写文件日志词表；59 处同时点击一个矩形 | `focus` 写 Info 词表，`Click` 承担点击 |
| `CaptainDamageAction` | 1 | 检测到队长重伤后写一条日志 | 识别 node 的 `focus` |
| `StopOnDamageAction` | 2 | 写日志、弹 toast、让本轮任务结束 | `focus` 的 `display` 用 `["log","toast"]`，任务结束由无后续分支表达 |

`LogAction` 的 125 处里，70 处是 Info、55 处当前借用 Warning 级别让工作记录归入「特殊情况」；`GuiLogAction` 的 48 处里有 5 处带 `warn:` 前缀。这些业务结果不必然是程序 Warning，迁移前须先完成下文的工作记录分类优化；普通 Info 部分可以先动。

迁移前：

```json
"S_ConfirmRetreat": {
  "recognition": {
    "type": "OCR",
    "param": {"roi": [548, 295, 182, 56], "expected": "确认返回本丸"}
  },
  "action": {
    "type": "Custom",
    "custom_action": "LogAction",
    "custom_action_param": {"message": "[合战场] 返回本丸", "click": [490, 469, 1, 1]}
  },
  "post_wait_freezes": {"time": 200, "target": [480, 459, 20, 20]},
  "next": ["S_CheckHomeBrightness", "S_ConfirmRetreat"],
  "on_error": ["S_DetectWhereAmI"]
}
```

迁移后：

```json
"S_ConfirmRetreat": {
  "recognition": {
    "type": "OCR",
    "param": {"roi": [548, 295, 182, 56], "expected": "确认返回本丸"}
  },
  "action": {"type": "Click", "param": {"target": [490, 469, 1, 1]}},
  "post_wait_freezes": {"time": 200, "target": [480, 459, 20, 20]},
  "next": ["S_CheckHomeBrightness", "S_ConfirmRetreat"],
  "on_error": ["S_DetectWhereAmI"],
  "focus": {
    "Node.Action.Succeeded": {"content": "[合战场] 返回本丸", "display": "log"}
  }
}
```

采用 `Node.Action.Succeeded` 而不是 `Node.Action.Starting`，是为了让日志与点击结果一一对应，也避免点击尚未提交就打出成功语义的日志。

对于「插入式」词表打点 node（`recognition` 缺省、`action` 为 `LogAction`、只负责在某个业务动作之后记一条词表），迁移后可以把 `focus` 直接挂到目标业务 node 上，再删除打点 node 并把 `next` 链恢复成迁移前的形状。这样每次迁移实际是减少一个 node，而不是等量替换。

### 第二优先：固定坐标点击与固定等待类

| 动作 | 引用处数 | 当前职责 | 纯 pipeline 替代 |
|---|---|---|---|
| `LogAction` 的 click 部分 | 59 | 识别命中后在矩形内随机点击 | `Click` 的 `target` 语义相同 |
| `UpdateDataWaitAction` | 5 | 可被停止打断的固定时长等待 | `pre_delay` / `post_delay` |
| `PageScrollAndHoldAction` | 2 | 上滑翻页，在终点继续按住 1 秒 | `Swipe` 的 `begin`、`end`、`duration` |

`UpdateDataWaitAction` 拆成 `post_delay` 的写法直接替换即可，但 MaaFramework 的原生延迟在用户点击停止时能否立即中断，需要实机验证一次；若原生延迟要等满才响应停止，这个动作就不能动，或者需要给延迟本身做改造。

`PageScrollAndHoldAction` 的 `Swipe` 支持 `duration`，能把「滑动加末端保持」压成一个动作，但游戏是否只认 `touch_down` 到 `touch_up` 之间的独立保持，需要实机确认。这一项先验证再迁移。

### 第三优先：识别命中后动态点击类

这一类当前用 C# 做「先识别，再用识别结果算点击位置」，纯 pipeline 用「识别 node 加 `Click`，`target` 取当前识别结果，`target_offset` 写固定偏移」表达。

| 动作 | 引用处数 | 当前职责 | 纯 pipeline 替代 |
|---|---|---|---|
| `EquipmentFallbackAction` | 2 | 在部队选择页扫描缺装标记，点击同一行的一键装备按钮 | 颜色或模板识别 + `Click` + `target_offset` 横向偏移到按钮 |
| `KobanChestLogAction` | 1 | 模板命中后打点一次，再等图标消失 | 模板识别 node + `focus` + `max_hit: 1`；消失等待交给 `inverse` 识别或 `post_wait_freezes` |
| `ResourcePointLogAction` | 2 | OCR 奖励文本并打点，弹窗期间轮询取最完整读数 | 基础打点可用 OCR node + `focus`；轮询取最优读数无法用纯 pipeline 表达 |
| `NaibanFindSwordAction` | 2 | 内番选刀列表滚动找目标，找不到时选第一把可用刀剑 | OCR node + `Click` + 上滑 node + `[JumpBack]` 循环，兜底 node 点固定第一行 |
| `FormationFindSwordAction` | 6 | 编队选刀列表滚动找目标，命中后点行右侧按钮 | 同上，`target_offset` 写行内按钮偏移 |

`KobanChestLogAction` 的「一次掉落只打点一次」语义要注意：`max_hit` 限制的是成功识别次数，打点 node 被打点一次后不会再命中，正好可以用来去重；但动画期间模板反复命中属于识别层面的抖动，迁移后要把轮询间隔的容错换成 `post_wait_freezes` 的 `target` 限定，否则可能回到「重启识别仍命中、重复打点」的老问题。

列表滚动找刀的两个动作在迁移时要明确保留的行为差异：`ListOcrScan` 里的「与上一屏 OCR 结果相同即判定到底」需要比较两次文本，纯 pipeline 无法表达。迁移后可以改成「滚动到固定次数上限后进入兜底 node」，或者在保留动作的前提下只迁移命中后的点击部分。

### 第四优先：选项驱动的坐标点击类

这一类的动作逻辑是「读参数里的编号或名称，再点击对应坐标」。参数在任务启动前就由任务选项确定，因此可以用 `select` 的 case 加 `pipeline_override` 把坐标静态写进 node，运行时不需要任何 C# 计算。

| 动作 | 引用处数 | 当前职责 | 纯 pipeline 替代 |
|---|---|---|---|
| `ExpeditionMapSelectAction` | 35 | 按后勤配置的目的地点击时代与地域 | 按目的地拆分 `pipeline_override`，直接覆盖 `target` |
| `DungeonFloorSelectAction` | 1 | 按配置楼层点击对应楼层层级 | 同上 |
| `NaibanFilterClickAction` | 2 | 按内番目标刀种点击筛选按钮 | `select` 覆盖筛选按钮 `target` |
| `FormationFilterClickAction` | 6 | 按槽位刀名对应刀种点击筛选按钮 | 同上 |
| `FormationRecordSlotClickAction` | 2 | 按目标部队号点击记录槽 | `select` 覆盖同编号记录槽 `target` |
| `FormationEquipTeamClickAction` | 1 | 按目标部队号点击装备解除面板按钮 | 同上 |
| `SelectFlowerTeamAction` | 1 | 按运行期记录的部队号点击部队按钮 | 需要先有 pipeline 侧的状态载体，当前不具备 |

这一组的迁移收益仅次于日志组：`ExpeditionMapSelectAction` 的 35 处引用全部在 `assets/interface.json` 的 `pipeline_override` 里，本身就已经是「选项 case 覆盖 node」的形态，改成直接写坐标不需要动 pipeline 结构，只需要把每个 case 里的 `custom_action` 换成 `Click` 加 `target`。

`SelectFlowerTeamAction` 读的是 `FlowerStateTracker` 里由其它动作写入的部队号，属于跨 node 运行期状态，不能直接换成静态覆盖，要等疲劳检测那条链路一并评估。

### 第五优先：可用协议能力替代的状态与次数判断

| 动作或识别 | 引用处数 | 当前职责 | 可迁移性 |
|---|---|---|---|
| `SortieRoundDoneRecognition` | 1 | 通过出阵 node 的命中计数判断这一圈是否已打完 | 可用 `max_hit` 加顺序分支表达，需要验证计数在每轮任务运行时是否从零开始 |
| `TeamSwitchCheckAction`（原 `TeamSwitchNeededRecognition` 加 `TeamSwitchAction` 合并） | 1（海陆联队） | OCR 剩余轮次，按配置决定换哪支部队并双击确认 | 需要跨轮次的当前部队状态，暂不具备纯 pipeline 表达条件 |
| `DispatchLogAction` | 6 | 读取实例配置里的目的地，输出 GUI 日志与词表 | GUI 日志部分可用 `focus`；目的地文本需要由选项注入或保留读取配置 |
| `RepairStartLogAction` | 8 | 读修复画面刀名与资源消耗，组合成一行词表，并在确认前点击 | `mode=gui` 的日志部分可用 `focus`；多 ROI OCR 拼接与刀名归一必须保留 |

`RepairStartLogAction` 的多 ROI 拼接是关键约束：pipeline 的单个 OCR node 只能读一个 ROI，无法把刀名 ROI 与三个资源 ROI 的文本合成一行词表。若要纯 pipeline 化，只能改成多个 node 各出一行日志，会破坏工作记录的现有词表格式，因此不在迁移范围内。

## 前置改造项

以下能力需要在开始迁移之前确认或补齐，否则部分候选会迁移到一半卡住。

### 工作记录的特殊情况分类

「特殊情况」是工作记录的业务分类，不是日志级别。它应同时包含两类事件：一是任务正常完成、但结果需要用户回顾的业务结果，例如无票终止、重伤撤退和跳过锻刀；二是真正的 Warning，例如 OCR、截图或配置异常。前一类保持 Info 级别，后一类保持 Warning 级别，二者都进入工作记录的「特殊情况」。

当前 `WorkRecordBuilder` 主要依赖 `WRN` 级别把词条加入「特殊情况」，因此无法区分「正常业务结果」与真正 Warning。`focus` 的 `log` 通道又固定经 `AddMarkdown` 以 Info 落盘，直接迁移会让第一类事件静默变成普通记录。

改造方向如下：

- 定义仅在 `focus.content` 中使用的项目约定标记 `special:`；它是文本约定，不是 `focus` 的额外 schema 字段。
- `FocusHandler` 识别并移除该标记，保持实时日志面板只显示正常业务文本，并将分类结果传给 `AddMarkdown(recordAsSpecial: true)`。
- 文件日志对该记录写入 `[Record][Special]` 标记，保持 Info 级别；`WorkRecordBuilder` 已识别此标记并加入「特殊情况」。
- 真实 Warning 继续走现有 Warning 级别路径；`WorkRecordBuilder` 保留 `WRN` →「特殊情况」的归类规则。

迁移侧现可写成 `"content": "special:[联队战] 无票终止"`，而用户看到的日志仍为 `[联队战] 无票终止`。普通词表、`[Special]` 词表和 Warning 词表均已有解析器测试，确认计数、展示文本与特殊情况归类正确。

### focus 的日志归属实例

`GuiLogAction` 用 `AddLog` 时通过 `ResolveOwnerProcessor` 定位执行任务的实例，多实例场景下不会把日志写到别的实例。`FocusHandler` 由每个 `MaaProcessor` 自己创建并持有对应 `TaskQueueViewModel`，归属应当一致，但迁移后需要在多实例环境验证一次。

### focus 的字段位置与渲染差异

`focus` 在字段顺序规范里排在 `on_error` 之后。日志文本经过 Markdown 渲染，方括号前缀不受影响，但文本里的下划线、星号、反引号需要检查是否需要转义。GUI 上的时间戳、颜色与 `AddLog` 略有差异，迁移后要确认用户可读性没有下降。

### 延迟的可中断性

`UpdateDataWaitAction` 与多个自定义动作都用 `SleepWithStopCheck` 实现「可被停止打断的等待」。pipeline 的 `pre_delay` / `post_delay` 是否在停止时立即返回，需要一次实机验证。若不可中断，这一组保持现状。

## 明确保留的自定义动作

按原因分组，便于后续再有类似需求时快速判断，不必重复评估。清单包含 pipeline 直接引用的动作，也包含被这些动作调用或属于同一流程的辅助类。

### 宿主 API 与队列语义

`CompleteCurrentTaskAction`、`RestartGameAction`、`SmartWaitAction`、`ExpeditionTimerAction`、`ExpeditionTimerCheckAction`、`ExpeditionTimerRecognition`、`ExpeditionTimeTracker`。

### 文件与配置持久化

`UpdateDataPrepareAction`、`UpdateDataSaveAction`、`UpdateDataMarkSuccessAction`、`UpdateDataIntervalRecognition`、`WarehouseReadResourceAction`、`WarehouseScanItemsAction`、`SwordBookScanAction`。

### 数值比较与统计

`FatigueCheckAction`、`GoalPtCheckAction`、`DrillDangerCheckAction`、`ForgeCapacityCheckAction`、`RepairCooldownCheckAction`、`EdoActionSelectAction`。

### 图像像素分析与多帧轮询

`SwordDropLogAction`、`NaibanOutfitLogAction`、`ClickTopRepairableSwordAction`、`MixGreedySelectionAction`、`NewMixTargetSelectionAction`。

### 跨 node 状态机与台账

`DailyTaskCompletionCheckAction`、`DailyTaskCompletionMarkAction`、`DailyTaskStepRecognition`、`TeamSwitchCheckAction`、`TeamSwitchDecision`、`FormationConfigAction`、`FormationEquipStateMachine`、`FormationEquipSelectAction`、`DragCaptainAction`、`ExpeditionTeamRestRecognition`、`NaibanOutfitSelectionAction`、`MixFindAllowedMaterialAction`、`ForgeDisassembleSelectAction`、`NewMixTargetSelectionRecognition`、`EdoLastActionRetreatRecognition`。

### 需要名单或目录联合校验的识别

`SwordNameMatcher`、`SwordNameResolver`、`SwordDropNotificationMatcher`、`NaibanOutfitRecognitionState` 以及依赖它们的动作。

## 迁移批次与验证

### 近期实施范围与顺序

本轮仅重构以下四个任务，严格按此顺序推进：一键日课（`DailyTask.json`）→ 联队战／海陆联队（`RegimentBattle.json`，旧 `LRentaisen.json` 已随重构移除）→ 本丸后勤（`Expedition.json`）→ 常驻作战（`Sortie.json`）。每个任务完成重构和验证后再进入下一个任务，其余任务暂不纳入本轮迁移。

秘宝之里（`Hanapai.json`）不在上述顺序内，按并行安排先行处理：能由 `Click` 与 `focus` 表达的日志打点已迁移完毕，刷花链 `HF_` 不在本轮范围内，等待后续专门的任务中刷花公用流程。

### 批次一：Info 级日志

范围：`LogAction` 中 52 处 Info 纯打点、`GuiLogAction` 中 43 处无警告前缀、`CaptainDamageAction` 的 1 处。

做法：把插入式打点 node 的 `focus` 挂到上游业务 node，删除打点 node，恢复 `next` 链。

验证：GUI 日志内容与顺序不变；工作记录中「出阵」「完成一圈」等计数不变；`python tools/compress_json.py --sort-keys` 排版通过。

### 批次二：特殊情况记录与点击合并

范围：`LogAction` 中当前以 Warning 级别记录的 55 处（41 处带 `click`、14 处纯打点）、18 处 Info 带 `click` 的打点，以及 `GuiLogAction` 的 5 处 `warn:`。逐条判断其属于正常业务结果还是真正 Warning；前者改为 Info 加 `special:` 标记，后者保留 Warning 级别路径。

前置：先完成工作记录的特殊情况分类改造。

验证：工作记录「特殊情况」条目数量与迁移前一致；点击落点与迁移前一致，特别是 `post_wait_freezes` 的等待位置。

### 批次三：固定坐标与等待

范围：`UpdateDataWaitAction`、`PageScrollAndHoldAction`、`KobanChestLogAction`。

前置：完成延迟可中断性与 `Swipe` 末端保持的实机验证。

验证：停止按钮响应时间不变；小判箱一次掉落只打点一次；翻页后页面停留位置不变。

### 批次四：选项驱动的坐标点击

范围：`ExpeditionMapSelectAction`、`DungeonFloorSelectAction`、`NaibanFilterClickAction`、`FormationFilterClickAction`、`FormationRecordSlotClickAction`、`FormationEquipTeamClickAction`。

做法：在 `assets/interface.json` 的对应 case 里把 `custom_action` 换成 `Click` 加 `target`，同时保留原有的 `enabled` 覆盖。

验证：逐选项跑一遍目的地、刀种、部队号组合，确认点击目标与迁移前一致。

### 批次五：识别加动态点击

范围：`EquipmentFallbackAction`、`NaibanFindSwordAction`、`FormationFindSwordAction`、`ResourcePointLogAction` 的基础打点部分。

前置：确认 `target_offset` 在识别结果矩形上的偏移方向与平台缩放行为。

验证：列表滚动到底的兜底行为、缺装行按钮点击、资源点打点文本格式。

### 批次六：状态与次数判断

范围：`SortieRoundDoneRecognition`、`DispatchLogAction` 的日志部分、`RepairStartLogAction` 的 GUI 日志部分。

前置：确认 `max_hit` 的计数在每轮任务运行时重置，以及 `focus` 在正常结束与提前结束两种路径上的触发次数。

## 附录：引用统计

本表为 2026-09-19 迁移前的扫描快照，后续任务重构与旧文件移除不回溯更新；`LRentaisen.json`（联队战）已重构为 `RegimentBattle.json` 并移除，表中相关条目仅作历史参考。

自定义动作引用次数，按降序排列。

| 动作 | 次数 | 出现位置 |
|---|---|---|
| `LogAction` | 125 | EdoCastle 5、Expedition 3、Hanapai 6、LRentaisen 4、Sortie 74、TacticalTraining 5、Underground 28 |
| `GuiLogAction` | 48 | DailyTask 19、EdoCastle 3、Expedition 1、Hanapai 1、interface 4、Sortie 10、Underground 10 |
| `ExpeditionMapSelectAction` | 35 | interface 35 |
| `RestartGameAction` | 21 | 15 个 pipeline 各 1 至 3 处 |
| `FatigueCheckAction` | 12 | Expedition 6、Hanapai 2、Sortie 2、Underground 2 |
| `CompleteCurrentTaskAction` | 9 | EdoCastle 1、Hanapai 2、Sortie 6 |
| `ExpeditionTimerAction` | 8 | Expedition 2、interface 6 |
| `RepairStartLogAction` | 8 | EdoCastle 2、Expedition 2、Sortie 2、Underground 2 |
| `DailyTaskCompletionMarkAction` | 10 | DailyTask 10 |
| `SwordDropLogAction` | 7 | 6 个 pipeline 共 7 处 |
| `DragCaptainAction` | 6 | 6 个 pipeline 各 1 处 |
| `DispatchLogAction` | 6 | Expedition 6 |
| `FormationFilterClickAction` | 6 | FormationConfig 6 |
| `FormationFindSwordAction` | 6 | FormationConfig 6 |
| `WarehouseReadResourceAction` | 6 | Warehouse 6 |
| `UpdateDataWaitAction` | 5 | UpdateData 5 |
| `NaibanOutfitLogAction` | 3 | Expedition 3 |
| `PageScrollAndHoldAction` | 2 | Expedition 1、FlowerBrush 1 |
| `NaibanFilterClickAction` | 2 | Expedition 2 |
| `NaibanFindSwordAction` | 2 | Expedition 2 |
| `FormationRecordSlotClickAction` | 2 | FormationConfig 2 |
| `ResourcePointLogAction` | 2 | Sortie 1、Underground 1 |
| `EquipmentFallbackAction` | 2 | Sortie 1、Underground 1 |
| `StopOnDamageAction` | 2 | Sortie 1、Underground 1 |
| `SwordBookScanAction` | 2 | SwordBook 1、UpdateData 1 |
| `UpdateDataPrepareAction` | 2 | UpdateData 2 |
| `UpdateDataSaveAction` | 2 | UpdateData 2 |
| 其余 25 个动作 | 各 1 | 见 `assets/resource/base/pipeline` 与 `assets/interface.json` |

自定义识别引用次数。

| 识别 | 次数 | 出现位置 |
|---|---|---|
| `DailyTaskStepRecognition` | 13 | DailyTask 13 |
| `ExpeditionTeamRestRecognition` | 5 | Expedition 5 |
| `NewMixTargetSelectionRecognition` | 4 | NewMix 4 |
| `EdoLastActionRetreatRecognition` | 1 | EdoCastle 1 |
| `TeamSwitchNeededRecognition` | 1 | LRentaisen 1 |
| `SortieRoundDoneRecognition` | 1 | Sortie 1 |
| `UpdateDataIntervalRecognition` | 1 | UpdateData 1 |

## 附录：focus 字段完整参考（2026-09-19 补录）

本节补录 `focus` 字段的完整行为，用于支撑本文档「第一优先：日志与提示类」与「前置改造项」两节的决策。来源为 MaaFramework 官方协议原文与本仓库实现，只记录已核实的事实。

### 定位

`focus` 是 node 字段，类型 object，默认 null。官方协议在 Node Notifications 一节明确说明：node notifications 依赖上层实现，MaaFramework 原生不支持。MaaFramework 只把 node 的 `focus` 原样放进回调的 `details_json`，解析与展示完全由客户端负责。

因此本项目的 `focus` 行为由 `_src/MFAAvalonia/Extensions/MaaFW/FocusHandler.cs` 与 `MaaProcessor` 的回调处理决定，不随 MaaFramework 版本变化。官方把 `display` 标注为 Project Interface v2.3.0 引入，`trace` 标注为 v2.9.1 引入。

### 数据结构

`focus` 是字典，键为消息类型，值为模板字符串或模板对象。

简写形式，字符串等价于 `display: ["log"]`，`trace` 取默认值：

```jsonc
"focus": {
  "Node.Action.Succeeded": "[合战场] 返回本丸"
}
```

完整形式，用 `display` 指定渠道，`trace` 控制遥测上报：

```jsonc
"focus": {
  "Node.Action.Succeeded": {
    "content": "[合战场] 返回本丸，任务 ID：{task_id}",
    "display": ["log", "toast"],
    "trace": false
  }
}
```

### 可用的键

键必须与回调 message 字符串完全一致，使用区分大小写的 Ordinal 比较。拼错的键不会报错，只是静默不生效，迁移时需逐条核对。

完整消息族如下，每族均有 Starting / Succeeded / Failed 三态：

| 消息族 | 触发时机 |
|---|---|
| `Resource.Loading.*` | 资源加载 |
| `Controller.Action.*` | 控制器动作 |
| `Tasker.Task.*` | 任务开始与结束 |
| `Node.PipelineNode.*` | node 整体执行 |
| `Node.Recognition.*` | 识别阶段 |
| `Node.Action.*` | 动作阶段 |
| `Node.NextList.*` | next 列表识别 |
| `Node.WaitFreezes.*` | wait_freezes 执行 |
| `Node.RecognitionNode.*` | 仅 `run_recognition` 任务 |
| `Node.ActionNode.*` | 仅 `run_action` 任务 |

`focus` 只对该 node 自身发出的回调生效。`Tasker.Task.*` 一类回调的 details 里没有 node 的 `focus`，因此实际可用的是 `Node.*` 家族。

选键会显著影响日志噪音，下面是本机一次真实运行（`debug/maafw.log`）的回调频次：

| 消息 | 次数 |
|---|---|
| `Node.Recognition.Starting` | 3114 |
| `Node.Recognition.Failed` | 1875 |
| `Node.Recognition.Succeeded` | 1239 |
| `Node.RecognitionNode.Starting` | 1176 |
| `Node.RecognitionNode.Succeeded` | 837 |
| `Node.NextList.Starting` | 502 |
| `Node.Action.Starting` | 402 |
| `Node.NextList.Succeeded` | 402 |
| `Node.PipelineNode.Starting` | 402 |
| `Node.PipelineNode.Succeeded` | 400 |
| `Node.Action.Succeeded` | 400 |
| `Node.RecognitionNode.Failed` | 339 |
| `Node.NextList.Failed` | 100 |
| `Node.WaitFreezes.Succeeded` | 30 |
| `Node.WaitFreezes.Starting` | 30 |
| `Node.PipelineNode.Failed` | 2 |
| `Node.Action.Failed` | 2 |

识别类消息的数量是动作类的三到八倍，正文选用 `Node.Action.Succeeded` 的决定与这份数据一致。

### 占位符

替换逻辑遍历 `details_json` 的全部属性，因此 details 里存在的字段都能作为占位符，按消息类型不同而变化：`{name}`、`{task_id}`、`{node_id}`、`{reco_id}`、`{action_id}`、`{wf_id}`、`{phase}`、`{elapsed}`、`{anchor}`、`{entry}` 等。

官方文档列出的字段并不完整。本仓库代码会读取文档未收录的 `action_details`（含 `box`、`action`），这类字段同样可以当作占位符，只是值为 JSON 字符串。

### display 渠道

| 值 | 官方语义 | 本仓库实现 |
|---|---|---|
| `log` | 运行日志，默认值 | `TaskQueueView.ConvertCustomMarkup` 后走 `AddMarkdown`，同时进实时日志面板与文件日志 |
| `toast` | 应用内轻提示，非阻塞 | `ToastHelper.CreateToastByType` |
| `notification` | 系统级通知，后台可见 | `ToastNotification.Show` |
| `dialog` | 非阻塞弹窗，任务继续 | 异步 `SukiMessageBox.ShowDialog` |
| `modal` | 阻塞弹窗，任务暂停 | `SetWaitingForModal(true)`，并在回调线程上 `Wait()` |

支持数组，一条消息可同时推多个渠道。未知渠道值被静默忽略。

### trace

仅在配置 `telemetry.sentry` 且开启 tracing 时生效。默认值为 `Node.PipelineNode.Failed` 上报、其余 `Node.*` 不上报。本项目已按约定禁用遥测，该字段当前没有实际作用。

### 与官方规范的差异

上游规范要求对象里只写 `content` / `display` / `trace`。本仓库额外兼容五种历史键：`start`、`succeeded`、`failed`、`toast`、`aborted`，且只在 `Node.Action.Starting` / `Node.Action.Succeeded` / `Node.Action.Failed` 上触发。`start` / `succeeded` / `failed` 接受字符串或数组，并支持 `[color:红]文本[/color]` 着色；`toast` 的第一、二项分别是标题与内容。

下文提到的旧式键与消息键形式，指 `focus` 取值的两套写法，由上游代码里的 `ProcessOldProtocol` 与 `ProcessNewProtocol` 分别处理，与 Pipeline 协议 v1/v2 无关：Pipeline v2 自 MaaFW v4.4.0 起支持且兼容 v1，差别只在 `recognition` 与 `action` 是否收进 `{type, param}` 二级字典。两套写法最终都汇入同一个 `DisplayFocus`，因此不存在兼容性风险。

两条代码路径实现分离，能力并不一致，这是迁移时最容易踩的地方：

| 能力 | `focus` 旧式键（start/succeeded/failed/toast） | 消息键形式（官方规范） |
|---|---|---|
| 计数器变量 `{count++}`、`{++count}`、`{a*b}` | 支持 | 不支持 |
| `[color:xxx]...[/color]` 着色 | 支持 | 不支持 |
| `{image}` 内嵌当前截图 | 不支持 | 支持 |

正文「`content` 会经过 i18n key 与 `{image}` 占位符解析」一句只描述了消息键形式的路径，计数器与着色属于旧式键独有。

计数器由 `AutoInitDictionary` 承载，默认含 `exploreCount` 键，生命周期是整个处理器实例，而非单次任务运行。

`aborted: true` 在当前代码里是死配置。`DisplayFocus` 虽有 `onAborted` 参数，唯一调用点只传了四个参数，该参数恒为 null。

### 内容解析链

模板内容经 `MFAExtensions.ResolveContentAsync` 处理，按顺序为：以 `$` 开头走国际化词条；绝对 http/https URL 中扩展名为 `.md`、`.markdown`、`.txt`、`.text` 的拉取正文，其余渲染成 Markdown 链接；文件路径（先做占位符替换）存在则读取文件；工程内大小写不敏感的相对路径兜底；都不匹配则按字面文本作 Markdown 处理。

基准目录是 `interface.json` 所在目录，取不到时回退 `AppPaths.DataRoot`。路径形如文件但解析失败会打一条警告。

### 启用条件与运行时开销

`hasFocus` 的判定是 `jObject["focus"]` 存在且非 null。代码注释记录了 MaaFramework 会在普通 node 回调里带上 `focus: null`，因此不能只看字段是否存在。

两项开销需要在批次规划时计入，正文尚未记录。

其一是回调放大。启用 focus 后，`DisplayFocus` 会收到该 node 的每一条回调并逐条查表，启用成本与该 node 的回调次数成正比，对识别类消息尤其明显。

其二是取图开销。无论模板里是否使用 `{image}`，每次回调都会抓一张图，先按 `reco_id` 取识别结果图，失败再回退 `GetCachedImage`。这是每条回调一次的固定成本。

代码注释同时记录了一个历史坑：`Node.Action.Starting` 阶段回调线程持有原生 action 锁，此时用 `MaaContext.Tasker` 反查会在 Android 上死锁，现实现改用处理器持有的托管 `MaaTasker` 引用。`modal` 渠道在回调线程上阻塞等待，属于同类需要注意的位置。

### 对正文前置改造项的补充

正文「工作记录的特殊情况分类」一条已确认成立：`log` 渠道走 `AddMarkdown`，最终以 `LoggerHelper.Info` 落盘，`focus` 对象没有 Warning 或业务分类字段；带 `recordAsWarning` 的路径会写成 `[Record] ` 前缀加 Warning 级别。正常业务结果与真正 Warning 必须由 MATR 的 Client 和工作记录解析器分别归类，不能依赖 `focus` 协议本身。

新增一条正文未记录的差异：`LogAction` 是刻意只写文件日志、不写 GUI 的，而 focus 的 `log` 渠道同时写文件与实时日志面板。日志组一次性迁移会让 125 处词表打点全部出现在实时面板里。这是观感变化而非技术障碍，需要在批次一开工前决定：接受它，或先给 focus 补一个只落文件的渠道。已迁移的一键日课、海陆联队与秘宝之里都直接用 focus 的 `log` 渠道，等于按前者处理；`LogAction` 仍然保留给尚未迁移的 Warning 与点击场景。

工作记录解析链路已实现并通过自动化测试。`WorkRecordBuilder` 的解析正则会主动跳过行首的 `[cfg=...]`、`[src=...]` 上下文块以及可选的 `[Record][Special]` 标记，再捕获 `[前缀] 行为词 详情`；`[Special]` 会独立于 `WRN` 将词条归入特殊情况。解析器测试覆盖普通 Info、Info 特殊结果与 Warning 三种分类路径。

### 现状

开工前 17 个 pipeline 文件与 `interface.json` 中共 372 处自定义动作引用、26 处自定义识别引用，`focus` 的使用数为 0；字段定义、序列化转换器与完整处理器均已具备，能力齐备但从未启用。迁移开工后这一节的前提已不成立：当前实测为 339 处自定义动作引用、25 处自定义识别引用，`focus` 已在 6 个文件中共启用 33 处，本节其余结论（回调放大、取图开销、消息键用法差异等）仍然有效。
