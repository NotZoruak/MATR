# 贡献指南

本文件说明参与 MATR 开发需要遵守的规则，适用于提交 Issue、Pull Request 以及维护 fork 的开发者。只想知道怎么用，请看 README 的「反馈与建议」章节。

## 反馈问题与提出建议

- 提交前先搜索已有 Issue，避免重复。
- 报 Bug 请给出复现步骤、预期结果和实际结果，并附上导出的日志压缩包；任务页面的「日志」卡片右上角可以一键导出。
- 提功能建议请说明使用场景和期望行为，不要只写一句想要什么。

## 开发环境

MATR 只维护一份共享代码，同时支持 Windows 和 macOS，不维护两套分支。

| 命令 | 用途 |
|---|---|
| `dotnet build _src/MFAAvalonia.sln` | 构建整个解决方案 |
| `dotnet build _src/MFAAvalonia.Desktop` | 只构建桌面端，日常改动的最低验证要求 |
| `dotnet publish _src/MFAAvalonia.Desktop` | 发布桌面端 |
| `pwsh tools/clean_build.ps1` | 清理构建产物 |
| `pwsh tools/pack_all.ps1 -Version vX.Y.Z` | 打包 Windows x64 与 macOS Apple Silicon 发布包 |

技术栈为 .NET 10.0、C# 14、Avalonia 与 MaaFramework。

## 分支与 Pull Request

完整的分支模型、命令和常见误区见 [docs/分支流程.md](docs/分支流程.md)。贡献者需要知道的是：

- 仓库默认分支是 `develop`，所有 PR 都提交到 `develop`。
- 从 `develop` 切出自己的分支：新功能用 `feat/<feature-name>`，修复用 `fix/<scope>`。
- `main` 是稳定发布线，不接受直接提交，也不要向它提交 PR。
- PR 描述要写清楚关联的 Issue、改了哪些内容、实际执行过什么验证。不要只写"已测试"。
- 涉及界面、资源识别或流程失败的内容，请附截图或日志。

## 提交信息规范

遵循 [Conventional Commits](https://www.conventionalcommits.org/zh-hans/) v1.0.0。

| 类型 | 用途 |
|---|---|
| `feat` | 新功能、新任务、新识别逻辑 |
| `fix` | 缺陷修复 |
| `perf` | 性能优化 |
| `refactor` | 不改变行为的重构 |
| `docs` | 仅文档修改 |
| `style` | 格式与空白调整 |
| `chore` | 依赖、构建脚本与维护性改动 |

标题格式：

```text
[可选表情] <type>(<scope>): <中文摘要>
```

- `scope` 写受影响的任务 pipeline 文件名或 `.cs` 文件名，没有对应文件时才可省略。
- 标题使用中文，说明用户能感知到的变化，建议控制在 72 个字符以内。
- 一个提交只包含一个逻辑变化，不要把无关的格式化、重命名或临时调试混进来。
- 修复 Issue 时在标题或正文末尾注明 `fix #123`，并说明问题是什么。
- 不要添加 `Co-authored-by` 行。

## 代码风格

### C#

- 使用文件级命名空间声明，例如 `namespace MFAAvalonia.Helper;`。
- 4 空格缩进；公开成员使用 PascalCase，私有字段使用 `_camelCase`。
- 日志统一走 `LoggerHelper`，不要使用 `Console.WriteLine`。

### Pipeline 与资源 JSON

- 4 空格缩进，资源路径使用正斜杠 `/`。
- 禁止使用 `target_offset`，所有坐标偏移直接写在 `target` 数组里。
- 每个 node 都必须设置 `on_error`。
- 坐标、ROI 与模板图片统一基于 1280×720 基准分辨率。
- `next` 列表要覆盖所有可能的后续界面，保证第一次识别就能命中正确的 node。
- 层级关系一律使用英文 `parent node`、`child node`、`sibling node`，不要使用中文亲属称谓。

### 自定义 action

放在 `assets/resource/base/custom/`，一个类一个文件，文件名与类名一致。

### 文件编码

- `.ps1`：UTF-8 with BOM。
- 其他源文件（`.cs`、`.json`、`.md` 等）：UTF-8 without BOM。

## 跨平台要求

- 平台判断统一使用 `OperatingSystem.IsWindows()`、`OperatingSystem.IsMacOS()` 与 `OperatingSystem.IsLinux()`。
- 路径统一使用 `Path.Combine`，不要写死 Windows 盘符、反斜杠路径或只适用于 Windows 的目录布局。
- 涉及文件路径、外部程序启动、ADB、Shell、原生库、Windows API、图标、字体、权限和发布脚本的改动，必须同时检查 Windows 与 macOS 行为。
- 只涉及共享业务逻辑的改动，完成 Windows 构建验证即可；没有 macOS 设备时使用 GitHub Actions 的 macOS 运行器验证构建。

## 提交前自查清单

- [ ] JSON 字段符合 MaaFW 协议，没有拼写错误或不受支持的属性。
- [ ] 没有使用 `target_offset`，坐标都写在 `target` 数组里。
- [ ] 每个 node 都设置了 `on_error`。
- [ ] `next` 列表覆盖了所有可能的后续界面。
- [ ] 坐标、ROI 与模板图片都基于 1280×720 基准。
- [ ] 新增自定义 action 放在 `assets/resource/base/custom/`，文件名与类名一致。
- [ ] pipeline、`assets/interface.json` 与资源文件保持一致。
- [ ] 异常中断（弹窗、意外对话框）有对应的处理路径。
- [ ] 已运行 `dotnet build _src/MFAAvalonia.Desktop` 并确认通过。
- [ ] 平台相关改动已在 PR 描述中说明 Windows 与 macOS 的验证情况。

## 不要自行改动的内容

- 资源版本号与更新公告由维护者在发布时统一提升。PR 中不要修改 `assets/interface.json` 的 `version`、`custom_title` 以及 `assets/resource/announcement/` 下的公告文件，避免多个 PR 互相冲突；如果改动涉及新增任务或识别逻辑，在 PR 描述里说明即可。
- `docs/` 目录下除 `docs/分支流程.md` 外都是维护者的本地文档，已被 `.gitignore` 排除，不会随仓库分发，也不需要提交。
