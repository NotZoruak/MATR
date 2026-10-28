# MaaFW CPU OCR 补丁

`MaaFramework-5.14.3-cpu-ocr-no-spin.patch` 对应 MaaFramework 5.14.3，源码提交为 `8963191215ca49d6b037802acd07d20a4213160a`。它只关闭 CPU OCR ONNX 会话的线程自旋，保留 4 个推理线程以及原有检测、识别和后处理逻辑。

Windows 打包时，`tools/pack_win.ps1` 会从 `runtimes/win-x64/native/MaaFramework.dll` 注入补丁运行库，并校验 SHA-256：

```text
A27D305F5462E93E81EB11F961DA01575724EE450F4D9540B59EED866F164298
```

该 DLL 位于 Git 忽略的 `runtimes/` 目录，不会进入源码提交。若新检出仓库或清理了本地运行库，需要先基于上述 MaaFW 源码提交应用补丁、构建 Windows x64 原生运行库，再将生成的 `MaaFramework.dll` 放到 `runtimes/win-x64/native/`。打包脚本会在找不到文件或哈希不符时中止，避免发布未打补丁的 Windows 包。

当前没有经过构建和验证的 macOS 补丁 dylib，因此 `pack_all.ps1` 会明确提示 macOS 包仍使用官方 MaaFW 5.14.3 运行库。
