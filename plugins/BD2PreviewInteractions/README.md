# BD2 Preview Interactions

BD2ModManager 的约会互动插件。当前版本 0.4.2，为全部 22 个约会场景提供独立的 **P 额外入口** 和 **官方原生入口** 开关，允许两种入口同时出现。

## 双入口管理（0.4.0）

Manager 页面和侧栏统一命名为「约会互动」，完整列出 22 项。每张角色卡片提供两个独立开关，两种入口均支持全局启停、逐项启停和全部开启/关闭。关闭某角色的官方原生入口，也会隐藏该角色账号已拥有的原生入口；只改变客户端列表，不修改账号数据。关闭整个「约会互动管理」会恢复游戏原始列表并保留全部偏好。

P 入口显示 P 标记，沿用本地 JSON、音频、自定义输入和 HUD，默认使用当前已启用模组；0.4.1 已移除每张角色卡片的「P 入口素材」下拉框，旧配置中的 `useMods=false` 不再阻止应用已启用模组。原生入口按当前完整游戏表发现，不依赖 JSON，沿用游戏的触点、阶段、音频、Reset、章节检查与进度处理，资源替换沿用 BrownDustX 正常设置。dating12 直接读取官方 CostumeId/PrestigeSkinId，不按本地头像编号猜测。

配置格式升级为 schemaVersion 2：顶层 `enabled` 控制管理总开关，`pEntriesEnabled`、`nativeEntriesEnabled` 分别控制两种方式；场景的 `enabled` 表示 P 开关，`nativeEnabled` 表示原生开关。旧 v1 配置自动迁移，0.3.0 当前六项原生入口保留，P 额外入口不会因迁移自动重复开启；已有的其他原生入口保持可见。旧 cfg 的 `UseNativeDatingEntries` 仅参与 v1 迁移，不再控制 v2 两种模式。

保存设置后重新进入约会界面生效，更换 DLL 后重启游戏。全部关闭时会提示没有启用入口并返回。原生记录缺失、同资源存在多个有效候选或关联异常时记录警告，P 开关保持用户原值；不会自动开启另一种入口。程序复制/恢复列表时会区分账号原列表与插件装饰后的列表，保证隐藏后可恢复且不重复注入。

0.4.2 修复本地补入的未拥有原生入口在 pose 触点触发奖励领取时出现的 `Home screen not owned / 154907`。这类入口跳过 `SpineInteractionReward` 请求，原生阶段、触点和动画继续运行，不产生账号奖励。账号真正拥有的入口保留原奖励流程；重新进入约会界面取得新的已拥有列表后，已获得角色会恢复正常奖励处理。

## 在约会互动页面配置模组

Manager `4.3.1-local.6` 在每张角色卡片上提供「配置模组」，直接打开与「角色」页面共用的完整配置窗口，并默认选中「约会」标签。窗口保留立绘、过场动画、约会三个标签，以及模组启停、单项预览、比较全部、删除模组和管理皮肤映射等原有操作。按场景编号定位角色，`illust_dating22_1`、`illust_dating22_2` 等部件会一起列在该角色的约会标签中。

模组选择直接保存到当前配置文件，与「模组」页面共用状态；P 和官方原生入口使用同一套已启用的约会模组。关闭窗口后，可点击约会页面底部的「同步模组」应用当前配置的更改，沿用现有冲突处理和自动同步设置。模组操作不会保存或覆盖尚未保存的 P / 原生入口设置。

## P 模式使用

1. 在已授权的 Preview 中打开需要的互动，点击「导出互动 JSON…」，选择保存位置。只导出当前 `illust_datingN.json`，保留授权有效期，不下载或复制音频。
2. 在 Manager 的设置 → 常规 → 目录中设置美术素材库根目录。打开约会互动设置时，缺少的 illust_datingN.json 会自动从 LLsetnow/Brown-Dust-2-Asset 下载到 audio/dating；音频也使用该目录。
3. 在侧栏「约会互动」中管理角色的两种入口。退出游戏后，在该页安装或更新插件。保存配置后重新进入游戏约会界面生效。

两页编辑同一份未保存配置，切换页面不会丢失选择，从任意一页保存都会保留另一页的修改。「约会互动管理」关闭后，重新进入约会界面恢复游戏原始列表。各条目的启停会保留，重新开启后恢复；没有总开关字段的旧配置默认开启。管理列表显示对应服装的立绘缩略图。

约会 JSON 与音频统一放在所选素材库根目录下的 audio/dating。资源路径由素材库根目录自动推导，不再单独设置 JSON 或音频目录。互动开关和手势参数写入游戏 BepInEx/plugins/BD2PreviewInteractions/settings.json，Manager 另存一份配置；游戏无需 Preview 同时运行。

P 模式只读取场景和音频；不写入官方账号进度、录制、奖励。导出到期后重新通过已授权 Preview 导出。

## P 模式操作与显示

- 滚轮缩放；只有右键拖动移动画面。
- 左键点按触点；拖动互动触点需先长按，默认 0.35 秒，再移动至少 5 屏幕像素。短按或空白左键不移动画面。
- 长按蓄力按配置时长结算，一次按下仅结算一次；松开、切换道具、pose 或退出场景会清理对应输入状态。
- 原生右上角 Reset 恢复首个 pose、初始好感度、默认道具和隐藏触点状态，保留当前视角；不实现 Hint Mode。
- 左侧好感度条和道具按钮复用游戏原生 UI 素材，不调用账号好感度/道具逻辑。
- 触点使用 Preview 风格圆点、圆环和拖动箭头，默认透明度 32%。只显示当前 pose、当前道具、好感度范围和隐藏条件允许的点。

道具按钮按列放在原生 Reset 下方，「显示触点」按钮接在道具列下方；位置根据 Reset 的实际屏幕坐标和窗口尺寸更新。长按门槛和触点透明度可在 Manager 调整。头像使用服装立绘的完整取景，保留 P 标记。

## P 模式加载与隔离

新场景在隐藏的实例中准备，完成后一次切换，加载时保留原画面。JSON 检查、Spine 骨骼解析和首次音频哈希校验在工作线程进行；声音按事件加载，缓存最多 24 个片段，语音与音效同步起播。过期、缺失或错误资源会显示提示并保留或恢复官方 UI。

加载本地预制体时关闭原生互动、骨骼跟随和动画控制脚本，只保留渲染及视觉布局组件，避免缺失骨骼逐帧刷日志及原生输入与本地输入重复响应。头像单元格绕开官方缩略图解析，由本地立绘加载器显示头像。

BrownDustX 替换共享资源前保存官方原始引用，P 项使用独立 `SkeletonDataAsset`，默认应用对应 `illust_datingN` 当前已启用的模组。缺少触点辅助骨骼时仅向私有实例补充官方辅助骨骼及父子关系，不修改源模组。

列表本地编号为 `1900000000 + N`，注入前检查冲突。插件针对当前 `SpineInteractionUI` 方法签名；签名不兼容则撤销钩子并记录错误。

## 原生条目复制验证（0.2.9）

测试开关位于游戏 `BepInEx/config/local.bd2.previewinteractions.cfg` 的 `[Diagnostics]`：

```ini
NativeEntryProbeEnabled = true
NativeEntryProbeSourceId = 32
```

默认关闭。启用且本地互动总开关开启时，在现有官方 `id=32 / Illust_dating1` 头像后加入一个相同头像。副本 ID 为 `1900100001`，仅修改完整克隆记录的 `Id`；`InteractionId`、服装、头像、资源、解锁关联等沿用原记录。它不属于 P 列表，不带 P 标记，头像、选择和互动都执行游戏原生方法，保留原生解锁检查。源记录必须已在当前官方列表中，完整官方表和本地条目均会检查 ID 冲突。

日志依次记录 `Native entry probe injected`、`Native entry probe selected` 和 `Native entry probe controller initialized`。最后一条包含原生阶段及触点数量，表示原生 `SetSpineInteraction` 已执行返回；画面、输入、音频及 Reset 仍需游戏内确认。副本沿用原互动的进度和原生行为，不是独立约会内容，也不证明缺失六项已解锁。

关闭开关后重新进入约会界面会移除副本，记住的副本选择会回落到源条目。临时记录仅保存在插件内存中。更换 DLL 需要重启游戏。

## 构建与检查

源码纳入 Manager 的 `plugins/BD2PreviewInteractions`，不再单独维护旧目录。

```powershell
# Manager 构建会先构建插件并嵌入 DLL
pnpm build
# 可通过 BD2_GAME_ROOT 指定包含游戏 exe 的目录
$env:BD2_GAME_ROOT = 'F:\Neowiz\Browndust2\Browndust2_10000001'
pnpm build:game-plugin
# 使用提取的六项官方 protobuf 检查原生列表、去重、启停和原数据保留
 dotnet build plugins/BD2PreviewInteractions/tests/NativeDatingEntryTests/NativeDatingEntryTests.csproj -c Release
 dotnet exec plugins/BD2PreviewInteractions/tests/NativeDatingEntryTests/bin/Release/net10.0/NativeDatingEntryTests.dll 'F:\Neowiz\releases\official-dating-six-20261112.json' 'F:\Neowiz\releases\official-dating-all-20261112.json'
# 检查双入口独立开关与旧配置迁移
 dotnet build plugins/BD2PreviewInteractions/tests/DatingSettingsTests/DatingSettingsTests.csproj -c Release
 dotnet exec plugins/BD2PreviewInteractions/tests/DatingSettingsTests/bin/Release/net10.0/DatingSettingsTests.dll
# 检查克隆保留全部 protobuf 字段、独立副本及编号冲突
 dotnet build plugins/BD2PreviewInteractions/tests/NativeEntryProbeTests/NativeEntryProbeTests.csproj -c Release
 dotnet exec plugins/BD2PreviewInteractions/tests/NativeEntryProbeTests/bin/Release/net10.0/NativeEntryProbeTests.dll
# 纯输入状态回归，不依赖运行中的 Unity
 dotnet build plugins/BD2PreviewInteractions/tests/GestureTests.csproj -c Release
 dotnet exec plugins/BD2PreviewInteractions/tests/bin/Release/net10.0/GestureTests.dll
# 使用游戏自带 Spine DLL 检查骨骼与动画状态的完整初始化
 dotnet build plugins/BD2PreviewInteractions/tests/SpineAssetTests/SpineAssetTests.csproj -c Release
 dotnet exec plugins/BD2PreviewInteractions/tests/SpineAssetTests/bin/Release/net10.0/SpineAssetTests.dll
# 使用本机格兰希特模组验证过渡中不会残留旧 pose（只读，不加载纹理）
 dotnet build plugins/BD2PreviewInteractions/tests/PoseTransitionTests/PoseTransitionTests.csproj -c Release
 dotnet exec plugins/BD2PreviewInteractions/tests/PoseTransitionTests/bin/Release/net10.0/PoseTransitionTests.dll 'F:\Neowiz\mods\7 Character Interactions 角色互动\Granhildr 葛兰西特\格兰希特 互动\illust_dating19.json'
```

插件目标为 .NET Standard 2.1。`.build-tools/csc.cmd` 在本机通过 dotnet 执行 Roslyn；其 SDK 路径随构建环境调整。不提交导出场景、音频、游戏资源或编译产物。
