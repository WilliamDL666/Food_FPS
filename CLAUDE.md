# 霓城封锁 NEON LOCKDOWN · Agent 工作手册

Unity 2022.3.54f1 单机 3v3 美食 FPS（爆破 / 大乱斗 / 抢食 / 变大 / 靶场）。场景、关卡、角色基本都由 C# 代码在运行时生成，资产由 `Tools/blender_*.py` 程序化建模。

## 系统地图（改什么去哪里）
| 领域 | 主要文件 | 设计文档 |
| --- | --- | --- |
| 枪械 / 伤害 / 弹道 / 数值表读写 | `Assets/Scripts/CombatModel.cs`（`Tune`、`DataTables.ApplyDefaults`）、`GameConfig.cs`（`Config.Arsenal`、`Config.Heroes`）、`DessertGuns.cs` | `Docs/战斗设计文档_角色_枪械_3C.md` §3 |
| 3C（移动、姿态、体力、镜头、输入） | `Fighter.cs`、`FPSRig.cs`、`CombatModel.cs` 的 `Tune` | 同上 §2 |
| 角色 / 技能 / 大招 | `Skills.cs`、`FighterAvatar.cs`、`GameConfig.cs` | 同上 §4 |
| 关卡（整张地图程序化生成） | `Arena.cs`（`Build()` → `FoodCity/MidStreet/NorthStreet/SouthStreet/Plaza/SiteAArea...`）、`FoodMap.cs`、`Interactables.cs` | README 各版本说明 |
| 模式 / 规则 / 遥测 | `Match.cs`、`FoodPlay.cs` | README |
| AI | `Bot.cs` | — |
| 打击感 / 特效 / UI | `Fx.cs`、`FoodFx.cs`、`GameUI.cs` | — |
| 自动化测试 | `PlaytestRunner.cs`（`Check("Mxx", ...)`） | `VALIDATION.md` |
| 打包 / 场景生成 / 烘焙 | `Assets/Editor/BuildDemo.cs`、`BakeArena.cs` | — |
| 建模 | `Tools/blender_*.py`（Blender 5.x 无界面），导入 `Assets/Editor/NeonImport.cs` | — |

## 数值的唯一来源（重要）
- **C# 默认值才是真正的来源**：`Config.Arsenal` / `Config.Heroes` / `Config.*Speed` 等，加上 `DataTables.ApplyDefaults()` 里的覆盖。
- `build` 会调用 `DataTables.WriteDefaults`，用 C# 默认值**覆盖** `Assets/StreamingAssets/Data/*.csv`。只改那里的 CSV 会在下次打包时丢失。
- 快速调参：直接改 `Build/NeonSiegeDemo_Data/StreamingAssets/Data/*.csv`，程序运行时会读这份表，不用重新打包就能跑 `range` / `match` 看结果。
- 定稿：把调好的值写回 C# 默认值 → `build` → `verify`。

## 验证命令（每次改动后必须跑，不要只说"应该没问题"）
```
python Tools/agent/neon.py data      # 数值表规则校验（规则在 Tools/agent/data_rules.json）
python Tools/agent/neon.py build     # 打包（约几分钟；--full 会重建场景 + 烘焙光照，很慢，只在改了 Editor 生成逻辑或光照时用）
python Tools/agent/neon.py test      # 45 项自动化测试，约 1.5 分钟
python Tools/agent/neon.py range     # 靶场实测 TTK vs 理论
python Tools/agent/neon.py match [bomb|ffa|cook|grow]
python Tools/agent/neon.py shots     # 截图 → Build/Screenshots，用 Read 看 png
python Tools/agent/neon.py verify    # data → build → test → range → match
```
- `build` 前 Unity 编辑器必须关闭这个项目，否则命令行打不开。遇到这种情况就停下来请用户关编辑器。
- 改了什么就跑对应的检查：改数值 → `data` + `range`；改 3C / 规则 → `test`；改关卡 → `test` + `match` + `shots`；收尾 → `verify`。
- 新功能要在 `PlaytestRunner.cs` 里加对应的 `Check("Mxx", ...)`，编号接着最后一个往下排。

## 工作约定
- 设计方法和设计文档一致：**设计意图 → 规则 → 数值 → 验证 → 迭代**。任何改动先写清楚意图和要达到的可测指标。
- 子 Agent 分工见 `.claude/agents/`，完整流程见 `/design-change` 技能。
- **必须由用户拍板的点**：新系统 / 新玩法的设计方向、手感参数的最终取值、影响多个系统的数值大改。给 2–3 个方案和取舍，不要自己定。
- 交付时更新：`README.md` 顶部加版本说明（V21、V22…），`VALIDATION.md` 顶部加验证记录（表格 + 修过的问题 + 已知问题），涉及战斗的同步改设计文档。
- 文档用中文，句子短、表格多，风格对齐现有 README / VALIDATION。
- 不要 commit / push，除非用户要求。
- `Assets/FPSRaw/` 是不可再分发的第三方素材，不要改动或引用进新内容。
