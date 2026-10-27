# 艾堃的杂项与补丁包 · 0.3.0测试版源码

《杀戮尖塔2》public-beta v0.111.0的模组源码。包含4项杂项与36项独立补丁，配置标题、启用开关和展开按钮同行；保留回退版的既有功能，不包含此前撤回的启动优化。

这是用于GitHub的纯文本源码分发：保留当前运行时、全部原生测试夹具、静态检查、接口审核及自行编写的补丁生成器和helper。游戏/工坊DLL、SDK、二进制差分、图片、存档、日志、历史备份、已编译输出与私人游戏宿主均不随包分发。

## 构建

需要.NET SDK 9、游戏v0.111.0的 `sts2.dll`、`GodotSharp.dll`、`0Harmony.dll`，以及RitsuLib 0.6.7编译SDK的四个DLL。它们都由维护者在本地提供。游戏运行时唯一前置为兼容的RitsuLib；被修补的内容模组属于可选依赖。

1. 复制 `Local.Build.props.example` 为 `Local.Build.props`，填写本地依赖和私有资源目录。
2. 按 `docs/required-inputs.json` 准备完整 `catalog.json`、22份差分、4张钥匙图和2张卡图。仅下载源码不足以独立生成完整可运行模组；缺少任意必需输入时构建会明确失败。
3. 使用以下命令构建与检查：

```powershell
python -B scripts/check_inputs.py --root "私有构建输入目录"
dotnet build AikunMiscPatchPack/src/AikunMiscPatchPack.csproj -c Release
dotnet build AikunMiscPatchPack/tests020/Probe.csproj -c Release
# 静态检查还需配置STS2_WORKSHOP_DIR、STS2_PATCH_INPUTS_DIR及私有原件/variants。
dotnet run --project AikunMiscPatchPack/tests/Tests.csproj -c Release
python -B -m unittest discover -s AikunMiscPatchPack/tests020/python_tests -v
python -B scripts/check_source.py
```

环境变量也可代替MSBuild属性：`STS2_GAME_MANAGED_DIR`、`STS2_RITSU_SDK_DIR`、`STS2_CECIL_DIR`、`STS2_WORKSHOP_DIR`、`STS2_PATCH_INPUTS_DIR`。编译探针不等于执行原生回归；隔离宿主和发布验证边界见 [维护说明](docs/MAINTAINING.md)。

## 内容

- `AikunMiscPatchPack/src/`：当前完整运行时源码，包括血魔宗、临时血量、融合顺序/强化、铁砧、任务卡、尘封魔典、配置折叠及原有修复。
- `AikunMiscPatchPack/tests/`、`tests020/`、`audit020/`：当前静态、原生检查、选择器单元测试和接口审核源码。
- `AikunMiscPatchPack/builders/ReleaseBlockers/`：新旧版本兼容中的新版差分生成器。
- 其余 `*Repair/`、`AdaptiveTopBarMod/`：自行编写的历史patcher、helper和测试；不包含第三方反编译实现。
- `docs/`：版本基线、构建输入与维护说明。当前配置键和功能描述以 `src/Options.cs` 为准。

血魔宗初始4费、首次升级3费并保留；融合时按子卡顺序启用吸血，多次打出累计比例，打出过升级版后开启临时血池。其他修复和40项配置保持当前发布版实现。东尼算法与火堆融合分别按新旧原件精确哈希选择差分，未知版本安全跳过。

YUI点火生成魂焰需要原作者的 **YUI The Spire Expansion - Cards**（工坊ID 3747562908）；本包不提供替代卡，也不追溯改写旧存档。已发布基线的验证摘要见 [0.3.0记录](docs/RELEASE-0.3.0.md)。

## 上传GitHub

解压后上传本目录的内容，包括以点开头的配置文件。不要上传私有工程归档、`Local.Build.props`、本地资源或构建输出。这里没有Git历史，也没有代为创建或推送远程仓库。

`.gitignore`保护Git提交；网页上传仍需手动排除私有文件。提交前运行 `python -B scripts/check_source.py`。代码许可尚由维护者决定，本次整理不擅自指定MIT等许可证；第三方游戏、SDK、模组和美术资源不分发。

本次公开副本的编译与一致性核验见 [源码整理验证](docs/EXPORT-VERIFICATION.md)。
