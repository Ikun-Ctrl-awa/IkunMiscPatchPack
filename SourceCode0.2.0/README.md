# 艾堃的杂项与补丁包

《杀戮尖塔 2》public-beta v0.111.0 的本地兼容补丁包，当前版本 **0.2.0 测试版**。包含 3 项杂项、32 项独立补丁；配置变更需重启游戏。

本仓库是**源码分发包**：保留当前主模组、测试夹具、接口审计和历史补丁生成器源码；不含游戏／工坊 DLL、SDK、二进制差分、图片、存档、日志、历史备份、生成物或完整游戏测试副本。

## 内容

- `AikunMiscPatchPack/src/`：当前主模组，全部运行时补丁与 mod形态。
- `AikunMiscPatchPack/tests/`：差分组合与依赖检查源码。
- `AikunMiscPatchPack/tests020/`：原生功能回归夹具源码；未附带本机账户克隆、Steam 订阅或游戏副本修改工具。
- `AikunMiscPatchPack/audit020/`：昔涟接口／存档差分和 YUI Cards 审计工具源码。
- 其余 `*Repair/`、`AdaptiveTopBarMod/`：自行编写的历史 patcher、helper 和测试源码。没有拷贝整份第三方反编译实现。
- `AikunMiscPatchPack/build_variants.py`、`make_catalog.py`：历史差分生成配方，需要自行准备原件、辅助构建和本地工具路径。
- `docs/`：维护、测试边界与输入说明。

## 本地编译

需要 .NET SDK 9、游戏 v0.111.0 的三个编译引用，以及兼容的 RitsuLib 0.6.7 SDK 四个 DLL。SDK 和游戏文件自行在本地提供，不提交到仓库。

复制 `Local.Build.props.example` 为 `Local.Build.props`，填写本机路径；该文件已被忽略。也可通过 `STS2_GAME_MANAGED_DIR`、`STS2_RITSU_SDK_DIR`、`STS2_CECIL_DIR`、`STS2_WORKSHOP_DIR`、`STS2_PATCH_INPUTS_DIR` 环境变量配置。

`PatchInputsDir` 是本地私有输入根目录，应包含原版本对应的 `catalog.json`、`assets/*.xor.br` 与 `assets/heart-keys/` 四张 PNG。运行完整差分检查还需 `variants/`。维护者可从自己的完整工程／归档中提供；新维护者需按生成器源码与合法获得的原件重建差分，并自行提供相应图片。这里只提供代码，不暗中以 Base64 或其他格式打包这些文件。

```powershell
dotnet build AikunMiscPatchPack/src/AikunMiscPatchPack.csproj -c Release
dotnet build AikunMiscPatchPack/tests020/Probe.csproj -c Release
# 还需设置 STS2_WORKSHOP_DIR 与 STS2_PATCH_INPUTS_DIR 才能执行差分检查。
dotnet run --project AikunMiscPatchPack/tests/Tests.csproj -c Release
```

缺少输入时，构建会明确报错，不会生成缺失补丁数据或钥匙图的残缺版本。`MonoCecilDir` 用于差分检查与历史生成器。主模组通过反射适配可选内容模组，不要求将其加入硬引用。

历史生成器保留旧工作区布局与示例安装路径，需要先按各自源文件配置本地依赖；它们不是已验证的无人值守重建流水线。`audit020` 也包含历史快照提取入口，只有自行准备相应历史材料时才能运行。完整引擎夹具需要独立测试环境与可选模组，单独编译探针不代表测试已执行。不要在玩家的正式存档上直接运行夹具。

## 主要功能

更好的顶栏及海克斯选择／屏幕边界适配、剧情可以跳过、mod形态、多重附魔、融合升级、Act 4 钥匙顶栏、往昔之章概率混入、千咒卷轴、七咒之戒与海克斯生命兼容、复视遗物三选一、昔涟结算接口、哥伦比娅卡图与附魔汉化等。完整配置键和说明见 `src/Options.cs`。

点火生成魂焰需要原作者的 **YUI The Spire Expansion - Cards**（工坊 ID 3747562908，模组 ID YUICardExpansion）；本包不提供替代卡。当前补丁保护永久点火，保留战斗临时附魔清理，并适配魂焰预览。

## 上传 GitHub

解压源码 ZIP，上传本目录的内容即可；不要上传完整备份 ZIP。包内没有 Git 历史。`.gitignore` 可保护通过 Git 提交的本地输入，但网页上传仍应手动排除本地输入与生成物。提交前可运行：

```powershell
python scripts/check_source.py
```

尚未替维护者选择公开许可；代码许可由维护者决定。第三方游戏、SDK、模组和美术资源不随本仓库分发。
