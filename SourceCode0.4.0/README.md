# 艾堃的杂项与补丁包 · 0.4.0测试版源码

适配《杀戮尖塔2》public-beta v0.111.0，包含8项杂项和40项补丁。游戏运行时唯一模组前置是兼容的RitsuLib；其他被修补的模组属于可选依赖。当前功能说明以`AikunMiscPatchPack/src/Options.cs`为准。

本目录用于上传GitHub，只有文本源码、测试、生成器和构建配置。不含游戏/工坊DLL、SDK、差分、图片、存档、日志、历史备份或已编译输出。完整安装包单独提供。尚未替维护者选定代码许可证，不擅自将项目许可为MIT等条款。

## 构建

需要.NET SDK 9、当前游戏的sts2.dll/GodotSharp.dll/0Harmony.dll和RitsuLib 0.6.7编译SDK四个DLL。复制`Local.Build.props.example`为`Local.Build.props`，填写本机目录；该文件不提交。私有资源目录须按`docs/required-inputs.json`准备完整输入，缺少任意一项会明确构建失败，不能用占位图或忽略资源来发布。

```powershell
dotnet build AikunMiscPatchPack/src/AikunMiscPatchPack.csproj -c Release
dotnet build AikunMiscPatchPack/tests020/Probe.csproj -c Release
python -B scripts/check_source.py
python -B scripts/check_inputs.py --root <私有构建输入目录>
python -B -m unittest discover -s AikunMiscPatchPack/tests020/python_tests -v
```

也可设置STS2_GAME_MANAGED_DIR、STS2_RITSU_SDK_DIR、STS2_CECIL_DIR、STS2_WORKSHOP_DIR、STS2_PATCH_INPUTS_DIR。静态差分测试还需提供私有variants和旧原件夹具；执行`dotnet run --project AikunMiscPatchPack/tests/Tests.csproj -c Release`。

## 功能与测试边界

新增东尼系列卡牌、办公室、血魔宗/雷电宗事件与遗物、独立灼热/泣血/电弧、蟑螂形态、可升级诅咒“区”。修复火堆SL、事件背景重载、赐福指定奖励、复合注能自动打出和融合余烬默认费用；更好的顶栏支持大体型怪物意图下移。保留此前兼容修复，不含已撤回的启动优化。

`tests020`保留当前原生检查和测试选择器，未提供复制真实账户设置的私人宿主。应自行准备物理游戏副本、独立用户目录并禁用Steam平台写入。AIKUN_TEST_OUTPUT_ROOT指定结果根目录，AIKUN_ENGINE_TEST_REQUEST指定请求文件；专项不能冒充完整回归。编译探针不等于已运行游戏验证。

真实故障输入在公开副本中为可选：AIKUN_BOSS_HP_INCIDENT_SAVE、AIKUN_EVENT_SL_INCIDENT_SAVE、AIKUN_IMBUED_INCIDENT_SAVE和AIKUN_EMBER_INCIDENT_CARD。未提供时不执行这些附加用例，不能宣称重现了私人故障存档。

`AikunMiscPatchPack/builders`及其他Repair目录保留自行编写的生成器/helper源码；第三方反编译代码和二进制不分发。历史配方需要STS2_WORKSPACE_DIR指向维护工作区，DOTNET_EXE可指定dotnet，不能视为无输入的一键重建流程。

## 上传GitHub

上传本目录内容，包括点开头的配置文件。不要上传完整工程归档、Local.Build.props、本地依赖或构建输出。网页上传不会应用.gitignore，仍须运行源码检查。此包未创建或推送远程仓库。
