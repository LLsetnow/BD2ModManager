# Brown Dust 2 dating prefab 解包与交互点数据说明

本文把 `Illust_dating18.prefab` 的分析方法推广到游戏包中所有 `Illust_datingN.prefab`。解包输入为游戏 Addressables/MonoScripts/Spine 文件和 `Brown-Dust-2-Asset-master`；本方法不以本地 interaction-export JSON 作为数据源。

## 当前数据范围

当前游戏目录的 Addressables catalog 列出 22 个主 dating prefab：`Illust_dating1` 至 `Illust_dating22`。catalog 另外还列有路径为 `Assets/AddressableResources/BundleCommon/Char/Datingillust/Censorship/Illust_dating16.prefab` 的 censorship prefab；它应作为变体单独识别，不能仅凭名字与主版合并。

本次游戏构建中，22 个主 prefab 根对象都能在以下 Addressables UnityFS 包中找到：

```text
F:\Neowiz\Browndust2\Browndust2_10000001\BrownDust II_Data\Addressable\f17229717fff2baf0b72e1eb98e19b91\717417e2cf01848e84a181be131d9299\__data
```

Addressables 的缓存路径会随游戏版本变化。泛化到其他构建时，先从 `StreamingAssets/aa/catalog.json` 枚举 `Illust_dating*.prefab` 和变体路径，再定位对应 bundle；若 catalog 解码器不可用，可扫描缓存的 Addressables `__data`，建立“GameObject 根名 → bundle 路径”索引。

当前构建其余关键资源：

- Unity 版本：`2022.3.62f2`。
- MonoScripts bundle：
  `F:\Neowiz\Browndust2\Browndust2_10000001\BrownDust II_Data\StreamingAssets\aa\7b3151263eedf1a905516ba565244d00_monoscripts.bundle`
- Spine Addressables bundle：
  `F:\Neowiz\Browndust2\Browndust2_10000001\BrownDust II_Data\Addressable\8ae2977d5a837a9470decf0f02fec7be\4bbcf4e0b2a2439f0c015abf693d528c\__data`
- 开源 Spine 资源根目录：
  `F:\Neowiz\Brown-Dust-2-Asset-master\spine\illust\illust_dating\`
- 开源 dating 音频索引：
  `F:\Neowiz\Brown-Dust-2-Asset-master\audio\dating\index.json`

开源音频索引当前包含 22 个 `illust_datingN` 条目。Spine 文件名不一定与 prefab 名一一相同：仓库中还存在 `illust_dating16_c`、`illust_dating22_1`、`illust_dating22_2` 等资源变体。因此应读取 prefab/Spine 资源引用并核对骨骼、动画，不能直接用 prefab 后缀拼出 `.skel` 文件名。

## 通用解包流程

### 1. 枚举场景 prefab

对 catalog 的 internal ID 取 basename，识别 `Illust_datingN.prefab`；同时保留完整路径，以区分 censorship 等变体。为每个 prefab 建立一个独立的 `sceneId`，例如 `illust_dating18`。

如果使用 UnityPy：

```python
import UnityPy
UnityPy.config.FALLBACK_UNITY_VERSION = "2022.3.62f2"
env = UnityPy.load(addressable_bundle_path)
objects = {obj.path_id: obj for obj in env.objects}
```

### 2. 以 prefab 根对象限制范围

在 bundle 中找到目标根 GameObject（例如 `Illust_dating18`），建立 Transform/RectTransform 到 GameObject 的映射，再沿 `m_Children` 递归遍历该根节点。只统计这个层级内的组件。不能在整包范围内单纯数 `SpineInteractionPoint`：bundle 含多个场景，组件类型会重复出现。

每个根对象上读取 `SpineInteractionGroup`；其子层级内读取 `SpineInteractionPoint`。实例类型应通过 `MonoBehaviour.m_Script` PPtr 对照 MonoScripts bundle 的 `MonoScript.m_ClassName` 确认。

当前构建中常用的 MonoScript path ID：

| `m_ClassName` | 当前 `m_PathID` | 作用 |
|---|---:|---|
| `SpineInteractionGroup` | `2701890557229066389` | 根对象上的 group 默认配置 |
| `SpineInteractionPoint` | `-7462845037553543458` | 交互点实例配置 |
| `SkeletonUtilityBone` | `1475756675136447996` | Unity Transform 与 Spine 骨骼的关联 |
| `SpineInteractionPointDestination` | `8084408536158993313` | 拖拽目的区组件 |

这些 path ID 只用于当前构建的组件识别。游戏更新后应重新从 MonoScripts bundle 按 class name 查询；Unity 对象自己的 `m_PathID` 也不是交互点的 `id`。

### 3. 提取交互点身份 tuple

对每个 `SpineInteractionPoint` 读取：

```python
identity = (
    int(point["_spineInteractionGroupId"]),
    int(point["_spineInteractionId"]),
    int(point["spineInteractionToolId"]),
)
```

含义如下：

| tuple 部分 | prefab 字段 | 含义 |
|---|---|---|
| `group` | `_spineInteractionGroupId` | 交互姿势/阶段编号；对应 `SpineInteractionGroup` 中的 group 配置 |
| `id` | `_spineInteractionId` | group 内的交互点编号；不是全场景唯一 ID |
| `tool` | `spineInteractionToolId` | 该点适用的工具编号；应与 group 的 `InteractionToolGroupDatas[].ToolId` 对照 |

`group` 配置的 `GroupDefaultSpineMotionName` 通常是该阶段的 idle 动画；`NeedToolKeep` 表示切换 group 时是否保留当前工具；`InteractionToolGroupDatas` 给出可用工具及其 Spine skin。

### 4. 不要把 tuple 当作每个 Unity 组件的唯一键

dating18 中 92 个交互点的 tuple 都唯一，但跨全部 dating prefab 并非如此。若同一 prefab 内两个不同 GameObject 使用相同 `(group,id,tool)`，这两个实例仍是两条序列化记录，不能直接去重。

用于跨场景语义关联时，使用 `(sceneId, group, id, tool)`；用于唯一定位某个 Unity 组件时，还要带上组件 `m_PathID` 或从 prefab 根到该 GameObject 的层级路径。`m_PathID` 仅在对应序列化文件/构建内稳定，不能作为跨版本的长期业务 ID。

音频索引的 point key 是 `group:id`，不含 `tool`，所以它用于匹配音频事件，不等于 Unity 组件唯一键。

## Group 与 Point 字段含义

### Group

`SpineInteractionGroup._defaultSpineMotionByGroup[]` 中每项是一种姿势阶段：

| prefab 字段 | 意义 |
|---|---|
| `InteractionPointGroupId` | group 编号 |
| `GroupDefaultSpineMotionName` | 默认 idle/阶段动画 |
| `GroupSwitchSpineMotionName` | 切换阶段时的过渡动画 |
| `InteractionToolGroupDatas[]` | 可选工具 `ToolId` 与 `SpineSkinName` |
| `NeedToolKeep` | 是否保留工具选择 |
| `_GaugeSettingData` | gauge 开关、初始分数、上下限、自动衰减、阈值动画和事件 |
| `Spine` | 可选 Spine 资源引用；不同场景/阶段需逐项检查 |

### Point

| prefab 字段 | 含义/转换提示 |
|---|---|
| `_spineInteractionGroupId`, `_spineInteractionId`, `spineInteractionToolId` | `(group,id,tool)` |
| 同一 GameObject 上 `SkeletonUtilityBone.boneName` | 交互点关联的骨骼名；需对照实际 skeleton 文件校验 |
| `_interactionActionType` | 本批资源中 `0` 为普通触摸、`1` 为拖拽、`2` 为陀螺仪类交互；按序列化值、对象名和 gyro 字段归纳 |
| `PlayMixAnimNames`, `IsPlayRandomMixAnim` | 交互反应动画列表及随机播放标记 |
| `PlayMotionName`, `_playMotionNames` | 交互结束后的 motion；数组顺序有意义 |
| `IsNextStateWhenActionEnd`, `ForceIdleWhenNextState` | 是否在动作结束后切换阶段；启用时后者是目标 group 编号 |
| `ClickMaxCount`, `ContinuousClickResetTime` | 点击次数上限与连续点击重置时间 |
| `_longPressSettingData` | 长按类型、阈值、循环/成功/失败动画 |
| `_gaugeSettingData` | 点级 gauge 开关、可交互范围、点击/长按分数 |
| `_hiddenPointSettingData` | 关联隐藏点、持续时间、排他/重置/关闭父隐藏点标记 |
| `_gyroSettingData`, `_micSettingData` | gyro 与麦克风触发参数 |
| `UseHapticWhenInteraction`, `_interactionZoneShape` | 震动标记与 Unity 原始交互区域类型 |
| `_activationObjectSettingData` | 激活对象引用；它与隐藏点配置不同 |

本次 22 个主 prefab 共发现 1,502 个点组件：`_interactionActionType` 统计为 `0: 1286`、`1: 166`、`2: 50`。场景配置 schema 若只提供 `drag: boolean`，无法原样表达原始的 action type 2；应把这种近似映射记录为转换规则，而不是原始枚举含义。

## 区域 PPtr 与几何

- `_rectTransform` 指向交互点当前使用的 RectTransform；`_orinRectTransform` 指向原始 RectTransform。两者尺寸可能不同。
- `_interactionBoxZoneRectTransform` 是区域 RectTransform 引用。
- `_destinations[]` 的元素是 `SpineInteractionPointDestination` 组件 PPtr。解析步骤是 `组件 → m_GameObject → RectTransform`，不是把 PPtr 当 RectTransform。
- 当前 `SpineInteractionPointDestination` 组件本身没有附加序列化值；区域形状/位置在所属 GameObject 的 Transform 层级和 RectTransform 上。
- `_hiddenPointSettingData._hiddenPoints[]` 若非空，也要解析组件引用，再关联目标点的 tuple 和组件身份。
- 转成场景 JSON 的四角可采用 `[左下, 左上, 右上, 右下]` 顺序；但 Unity 的 `m_AnchoredPosition` 是相对父 Transform 的局部坐标。转换成目标 Spine bone 的局部坐标时，还要处理锚点、pivot、旋转、缩放、父级骨骼和 Canvas 根缩放。不能把 `m_AnchoredPosition` 直接当成最终 `DatingRegion.corners`。

## 当前构建的主 prefab 清单

下表按各 `Illust_datingN` 根对象层级统计。`点组件数` 是序列化实例数量；`不同 tuple 数` 是 `(group,id,tool)` 去重数量。差值表示存在多个 Unity 组件共用同一 tuple，不表示可以丢弃。

| 场景 | Group 数 | 点组件数 | 不同 tuple 数 |
|---|---:|---:|---:|
| dating1 | 3 | 68 | 64 |
| dating2 | 3 | 60 | 60 |
| dating3 | 2 | 39 | 39 |
| dating4 | 2 | 58 | 56 |
| dating5 | 2 | 50 | 50 |
| dating6 | 6 | 49 | 49 |
| dating7 | 2 | 63 | 63 |
| dating8 | 3 | 62 | 62 |
| dating9 | 2 | 77 | 74 |
| dating10 | 3 | 58 | 58 |
| dating11 | 4 | 61 | 61 |
| dating12 | 3 | 87 | 86 |
| dating13 | 3 | 52 | 52 |
| dating14 | 2 | 132 | 128 |
| dating15 | 3 | 68 | 68 |
| dating16 | 2 | 119 | 119 |
| dating17 | 2 | 62 | 62 |
| dating18 | 7 | 92 | 92 |
| dating19 | 2 | 58 | 58 |
| dating20 | 7 | 83 | 76 |
| dating21 | 3 | 54 | 52 |
| dating22 | 2 | 50 | 50 |
| **总计** | — | **1,502** | **1,479** |

至少 dating1、4、9、12、14、20、21 有 tuple 重复。例：dating1 的 `(1,3,0)` 同时出现在 `point1_3_touch1` 和 `point1_3_touch2`；dating20 的 `(5,27,0)` 有 4 个组件实例。应保留每个组件记录，再检查骨骼、动画、坐标和层级是否说明它们是不同点或重复配置。

dating18 是可复核样例：7 个 group、92 个点组件、92 个唯一 tuple；25 个点设置了 `_interactionBoxZoneRectTransform`，所有 `_destinations[]` 合计 29 个引用，hidden-point 引用为空。

## Spine 与音频校验

每个场景都要按 prefab 的 SkeletonGraphic/SkeletonDataAsset 和 group Spine 引用找到正确的 skeleton/atlas，再检查 `SkeletonUtilityBone.boneName`、默认动画、点动画和 motion 是否存在。不要假设 `illust_datingN.prefab` 一定使用同名 `illust_datingN.skel`。例如开源仓库含有 `illust_dating16_c`、`illust_dating22_1` 和 `illust_dating22_2` 资源变体。

dating18 的游戏 TextAsset `.skel` 与开源文件逐字节一致，SHA256：`3277324d189560ab9e513b1ba644cced456ab5cd2170c158f6d5f855374fdd24`；`.atlas` 也一致，SHA256：`391e114708421449ae7fe20d7035de83bcf37c91a559034311c114626167ec1b`。该 skeleton 有 1,832 根骨骼和 171 个动画，dating18 的 92 个点骨骼均可解析。

dating18 prefab 中有 13 个动画字段引用无法在对应 `.skel` 找到：`mix2_19_1`、`mix1_0_1`、`mix1_14_1`、`mix2_0_1`、`mix2_10_1`、`motion2_12 `（末尾有空格）、`mix3_0_1`、`mix4_0_1`、`mix5_0_1`、`mix6_0_1`、`mix6_42_1`、`mix7_0_1`、`mix7_13_1`。其他场景也应分别做动画存在性检查；不要据此推定所有 scene 有相同缺失项。

若要补场景 JSON 的 `audio` 段，从开源 `audio/dating/index.json` 读取相应 `illust_datingN` 条目。该数据不在 prefab 实例字段中；音频点按 `group:id` 索引。

## 官方入口记录与表级 InteractionId（2026-11-12 本机核对）

数据库页解码参数、只读导出示例和原生插件实现的完整说明见[约会数据解包与原生入口插件实现](./dating-native-entries-unpack-and-plugin.md)。

`SpineInteractionPoint._spineInteractionId` 是点编号。约会入口需要的 `LobbySettingItemTable.Id` 和关联 `InteractionId` 应读取设计数据库；不能从点编号或 prefab 后缀推算。

本机构建的 common 设计缓存位于 `%USERPROFILE%/AppData/LocalLow/Gamfs/BrownDust II/Data/t/9F251C63BC72551C681EE75D328FA090D56E444B`。文件名对应 `SHA1("common_v1")` 的大写十六进制值。沿游戏 DLL 的数据库代码复现页级 AES-CBC 解码后，以内存 SQLite 只读查询；两个表的 `ProtoBuf` 列分别使用游戏自带 `LobbySettingItemTable.Parser` 和 `SpineInteractionTable.Parser` 解析。源缓存未修改，数据库 `quick_check` 为 `ok`。

本次缓存包含 563 条 Lobby 行和 22 条 Interaction 行。其中 27 条 Lobby 行引用 dating 资源，22 条有有效互动关联，另外 5 条的 `InteractionId` 为 0。解析结果与运行时日志中的 16 项 ID、服装和资源名全部一致，也与原生复制实验捕获的完整源行 32 一致。

| 场景 | 官方 Lobby 行 ID | InteractionId | CostumeId | PrestigeSkinId | 解锁章节组 / 章节 ID |
|---|---:|---:|---:|---:|---|
| dating7 | 77 | 1000296 | 296 | 296 | 0 / 0 |
| dating9 | 99 | 1001197 | 1197 | 1197 | 0 / 0 |
| dating12 | 121 | 1061492 | 61402 | 61492 | 0 / 0 |
| dating18 | 207 | 1000396 | 396 | 396 | 0 / 0 |
| dating19 | 225 | 14 | 67104 | 0 | 14 / 10 |
| dating21 | 274 | 1065193 | 65193 | 65193 | 0 / 0 |

dating12 的官方 `CostumeId` 是 61402；此前本地 P 目录中的 61492 对应它的 `PrestigeSkinId`。同资源且 `InteractionId=0` 的行分别为 dating7=218、dating9=217、dating12=161、dating18=208、dating21=275；这些行没有对应的有效互动表记录，选择入口时应区分。

完整六项数据保存在仓库外 `F:/Neowiz/releases/official-dating-six-20261112.json`，包含显式默认字段、原始 protobuf、缓存及解析 DLL 的 SHA256、运行时交叉校验结果。0.3.0 已使用这六项官方记录，用户确认六项均出现并能进入；日志也确认各项原生控制器初始化。输入、音频和 Reset 的各项行为未单独验收，详见上述实现文档。

## 使用边界

原始 prefab 适合确认 Unity 中的组件实例、分组、点身份、动画字符串、引用关系和原始 RectTransform。要把这些数据转换成 Mod Preview 的 `corners`、`collider`、`zone`、`destinations`、`radius`，还需独立实现并验证 Unity Transform 到 Spine bone-local 坐标的换算。

dating18 的[结构重建样例](../../illust_dating18-scene-rebuilt.json)保留了 group/point 主体和音频，但其几何坐标及 radius 尚不是无损复刻；不要把该样例当作所有 dating 场景的通用模板。

角色静态立绘、技能卡立绘、Spine 资源和角色技能音频的 Addressables 提取流程，另见[角色立绘与技能音频解包说明](./character-illustration-skill-audio-unpack.md)。
