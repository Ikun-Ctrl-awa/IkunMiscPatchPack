# 维护与构建输入

主入口为 `AikunMiscPatchPack/src/Entry.cs`，配置定义在 `Options.cs`。源码为0.3.0发布基线，4项杂项、36项补丁。更换分支时不要恢复已经回退的启动优化代码。

## 本地依赖

复制 `Local.Build.props.example` 为 `Local.Build.props`，填写游戏托管程序集目录、RitsuLib 0.6.7编译SDK目录、Mono.Cecil目录及私有构建输入目录。该文件不提交。主模组只需游戏三个引用、RitsuLib四个引用及完整嵌入资源。`docs/required-inputs.json` 提供29份输入的校验值（清单、22份差分、4张钥匙图和2张卡图）。不得以占位资源替代或忽略缺失资源后发布。

运行静态检查还需设置环境变量 `STS2_WORKSHOP_DIR` 与 `STS2_PATCH_INPUTS_DIR`，并准备同版本的 `variants/` 以及 `api/release-blockers/original/<SHA256>/` 中的新旧原件夹具；原件和修补DLL不分发。

历史patcher/helper项目保留原工作区的相对布局。运行它们时用 `STS2_GAME_MANAGED_DIR`、`STS2_WORKSHOP_DIR`、`STS2_GAME_ROOT` 指定本地路径。Python生成配方还需 `STS2_WORKSPACE_DIR` 指向含原件和既有辅助构建的私有维护工作区，`DOTNET_EXE` 可指定dotnet，`STS2_CECIL_DIR` 指定Cecil。历史配方不是无需额外材料的无人值守重建系统。

新版差分生成器位于 `AikunMiscPatchPack/builders/ReleaseBlockers/`。它以私有输入根目录为命令行参数，检查固定原件哈希，复用旧已验证helper类型，输出独立版本差分；运行前须准备旧 `variants/` 与四份合法取得的原件。生成器不写工坊原件。

## 原生测试

`tests020/`保留当前全部C#检查、选择器及16项Python单元测试。原私有 `prepare_sandbox.py`、一键游戏宿主脚本、Steam账号克隆逻辑和真实故障存档不提供。不要将探针装入正式游戏运行。

应自行准备独立物理游戏副本、独立用户目录、关闭Steam平台写入、复制当前依赖并核对源/副本哈希。设置 `AIKUN_TEST_OUTPUT_ROOT` 指定按版本分目录的结果根目录，`AIKUN_ENGINE_TEST_REQUEST` 指定本轮request.json。输出必须落在该版本的runs/下。请求协议与完整回归校验规则见 `TestSelection.cs`、`test_plan.py`。

七咒生命回放中的真实故障存档改为可选 `AIKUN_BOSS_HP_INCIDENT_SAVE`，未提供时仅运行其余可构造夹具，不得声称复现了该私人存档。完整回归必须显式标为regression，分别覆盖full/minimal；专项、旧结果及缺少宿主时的编译不能替代发布前完整回归。

公开副本不提供直接调用私有宿主的一键发行脚本。发布前先准备完整资源、静态检查和两套引擎回归，再自行打包DLL与模组清单。不要修改工坊原件或自动追溯修改玩家旧存档。
