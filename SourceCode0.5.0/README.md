# IkunMiscPatchPack

艾堃的杂项与补丁包，0.5.0测试版，适用杀戮尖塔2 public-beta v0.111.0。唯一必要模组前置为RitsuLib 0.6.7或兼容新版；被修补的内容模组均为可选依赖。

全部代码位于本仓库一个根目录下：src是统一模组工程，tests是差分检查，tests020是原生回归探针，tools是统一生成器与辅助源码，scripts和docs提供构建检查及说明。没有一个补丁一个顶层目录的结构。

本包用于上传GitHub，只包含文本源代码、测试和构建配置。不包含游戏/工坊DLL、SDK、图片、差分二进制、参考照片、玩家存档、日志或开发机器配置。运行包另行提供。未擅自添加开源许可证。

## 构建

需要.NET SDK 9和Python 3。复制Local.Build.props.example为Local.Build.props，填写游戏、Ritsu SDK、Mono.Cecil和私有资源目录，也可配置STS2_GAME_MANAGED_DIR、STS2_RITSU_SDK_DIR、STS2_CECIL_DIR和STS2_PATCH_INPUTS_DIR。私有资源按docs/required-inputs.json提供，任意文件缺失或哈希不符都会拒绝构建。

```powershell
dotnet build src/IkunMiscPatchPack.csproj -c Release
dotnet build tests020/Probe.csproj -c Release
python -X utf8 scripts/check_source.py
python -X utf8 scripts/check_inputs.py --root <私有资源目录>
python -m unittest discover -s tests020/python_tests -v
```

输出为src/bin/Release/net9.0/IkunMiscPatchPack.dll。清单为根目录IkunMiscPatchPack.json。差分静态检查另需私有variants和原件夹具；使用STS2_PATCH_INPUTS_DIR指定对应目录，并设置STS2_WORKSHOP_DIR。工具工程可用-p:Generator=ReleaseBlockers等选择tools/Generators中的一个生成器，输入二进制仅从维护者本地提供。

## 改名及迁移

新模组ID、程序集和保存标识统一为IkunMiscPatchPack。加载时识别旧Aikun模型与能力标识，读取后再次保存使用新标识；旧模组配置目录仅复制缺失文件到新目录，不覆盖已有新配置，也不删除旧数据。升级请使用发布包的安装脚本，保留它创建的设置、旧模组及存档备份。不要同时启用新旧两份模组。

## 内容与验证

51项主设置：10项杂项、41项补丁，包括塔之诅咒、五角色X牌、东尼/血魔宗/雷电宗内容、便携睡袋、顶栏及兼容修复。额外休息处不能整理。AutoAnthony 0.3.153两项问题仍存在，保留并适配；未知二进制版本安全跳过。详细条目见src/Options.cs。

原生回归探针保留，但私人宿主、账户设置和故障输入不公开。运行时须自行准备物理游戏副本、独立用户目录并禁用Steam写入；IKUN_WORKSPACE_DIR用于定位测试工作区，IKUN_TEST_OUTPUT_ROOT指定结果根目录。未提供私人复现输入的用例会明确报缺少输入，不能据探针编译成功声称完成游戏回归。源码构建验证与维护环境中的完整回归分开记录。

上传本目录内容时包含点开头文件，运行scripts/check_source.py，不上传Local.Build.props、私有资源、构建输出或本地存档。本流程没有创建或推送GitHub仓库。
