# MaaFW CPU OCR 补丁

## 当前使用：MaaFramework 5.14.3 双线程低 CPU 方案

MATR 当前依赖 `Maa.Framework.Runtimes 5.14.3`。Windows 使用 `MaaFramework-5.14.3-cpu-ocr-low-cpu.patch`，在原关闭自旋方案上将 CPU OCR 会话的推理线程数改为 2。原模型、识别前后处理与 GPU 路径保持不变。原生版本标识为 `v5.14.3+cpu-nospin-t2`。

对应官方源码提交 `8963191215ca49d6b037802acd07d20a4213160a`，依赖 MaaDeps v3.0.0、ONNX Runtime 1.29.0。补丁仍通过 FastDeploy 会话回调配置两个线程和两项 `allow_spinning=0`，不能用旧版 FastDeploy 补丁替代。

| 文件 | SHA-256 |
|---|---|
| MaaFramework.dll | `1018F1719EEAC20657BB8AC3BB357374DE9C33678DC0D9DF182B18DB513821E1` |
| fastdeploy_ppocr_maa.dll | `A7D12E9B0E7BB6257A67B56051823502DCB474A40E5911D4CBBBDD50F0606085` |
| onnxruntime_maa.dll | `56E9D7D3BEAAD24E2CE79885201FE19F1B4DBDF511CF1D4BCB03549999C604DC` |
| opencv_world4_maa.dll | `132C00F39772159EC6F3586E662F9996F725429F72AE42AF603B97AA80ACACEB` |

构建方式沿用历史 5.14.3 的 MSVC x64、RelWithDebInfo 配置及固定子模块，版本参数改为 `MAA_HASH_VERSION=v5.14.3+cpu-nospin-t2`。本机验证 39 组静态区域和 32 组当前截图 OCR，双线程与原四线程的文字、框、命中和分数完全一致。文字密集截图测试累计 CPU 时间约减少 26.2%，总耗时约增加 8.5%；单线程的密集画面耗时增幅较大，未选为默认。完整数值属于离线样本，不是实战总占用降幅。

资源默认 `rate_limit` 从 500 改为 1000 毫秒，只降低未命中等待时的轮询频率，原有显式覆盖优先，全部识别分支、超时及动作延迟保持原样。纯图片控制器验证立即命中不会等待一个轮询周期；750 毫秒中间方案的未命中探针截图次数从 9 次降到 7 次。用户接受轻微变慢后选定 1000 毫秒；本机两轮三分钟实测的平均 CPU 分别为 3.99%（750 毫秒）与 3.26%（1000 毫秒），但游戏画面未经严格配对，不能将全部差值归因于轮询间隔。轮询超时仍按框架原有方式在识别循环边界检查，不能作为严格毫秒级截止时间使用。

macOS 跟随官方 5.14.3 dylib，未构建双线程关闭自旋补丁，不能宣称具备相同原生收益。1000 毫秒资源默认值为共享资源配置。

## 历史使用：MaaFramework 5.14.2

此前的 5.14.2 对照使用 `Maa.Framework.Runtimes 5.14.2`。旧版 FastDeploy 没有 5.14.3 使用的会话回调，因此该版本补丁位于 FastDeploy 的 CPU 会话创建处。

补丁文件为 `FastDeploy-maafw-5.14.2-cpu-ocr-no-spin.patch`，对应 MaaDeps `v2.12.6` 中的 FastDeploy 提交 `e962983da6daba7d0c12f6bf5f8ff7173be70982`。修改仅在 `option.device == Device::CPU` 时为会话添加两个配置键，将 `session.intra_op.allow_spinning`、`session.inter_op.allow_spinning` 设为 `0`，不修改默认四线程、模型或 GPU 路径。

保留官方 MaaFramework 5.14.2、ONNX Runtime 1.19.2 与配套 OpenCV，只替换 `fastdeploy_ppocr_maa.dll`。因此原生框架版本仍显示 `v5.14.2`，不带 `+cpu-nospin`；判断是否使用补丁必须核对 FastDeploy 摘要。

Windows 打包脚本同时校验并复制以下四个文件，拒绝混用 5.14.3 的依赖：

| 文件 | SHA-256 |
|---|---|
| MaaFramework.dll | `339A933A2C10286ACCCD9EEC4B2BB8D9AE80D25AB3DCB29959322A41F8C318B2` |
| fastdeploy_ppocr_maa.dll | `1BC1AB44AC7537BA5C259C0133D1D8084A63A3E8BA666AFFBC5CA7B41269B82E` |
| onnxruntime_maa.dll | `57194B62A8C03E180CE311BBE9B2BE4824B7BC95CF72A951602BB8CFA925B7F3` |
| opencv_world4_maa.dll | `9912A1F359B3A94E30D738AA5ED02CDFA3D805405F88432C26BEA34B16CF6F8E` |

构建过程：检出上述 FastDeploy 精确提交，应用对应补丁，使用 MaaDeps v2.12.6 的开发库和 Windows x64 工具链，以 Release、动态库方式构建 `fastdeploy_ppocr`，输出名称为 `fastdeploy_ppocr_maa.dll`。构建参数包含 `CMAKE_RELEASE_POSTFIX=_maa`、`BUILD_SHARED_LIBS=ON`；ONNX 头文件 API 版本必须为 19，不能引用 MaaDeps 3.0.0 的头文件。

本机验证了 39 组 OCR，文字、框、分数及命中结果严格一致；原 DLL 的全部导出保留，没有新增导入库。30 次 OCR、每次间隔 200 毫秒的离线批次，累计 CPU 时间从 5.000 秒降至 1.797 秒，预热后的 1.5 秒空闲 CPU 从 46.875 毫秒降为 0。该数值不代表游戏运行中的总 CPU 降幅。

macOS 跟随 NuGet 依赖降为官方 5.14.2 dylib，未构建此 FastDeploy 补丁，也未进行 macOS 实机验证。不可将 Windows DLL 用于 macOS。

## 历史方案：MaaFramework 5.14.3

`MaaFramework-5.14.3-cpu-ocr-no-spin.patch` 对应 MaaFramework 5.14.3，源码提交为 `8963191215ca49d6b037802acd07d20a4213160a`。它只关闭 CPU OCR ONNX 会话的线程自旋，保留 4 个推理线程以及原有检测、识别和后处理逻辑。

此前使用 5.14.3 时，Windows 打包从 `runtimes/win-x64/native/MaaFramework.dll` 注入补丁运行库，并校验 SHA-256：

```text
A27D305F5462E93E81EB11F961DA01575724EE450F4D9540B59EED866F164298
```

原生 DLL 位于 Git 忽略的 `runtimes/` 目录，不会进入源码提交。新检出仓库或清理本地运行库后，需要按当前方案重新构建，不能混用历史构建与当前方案的配套运行库。

历史 5.14.3 方案同样没有经过构建验证的 macOS 补丁 dylib。
