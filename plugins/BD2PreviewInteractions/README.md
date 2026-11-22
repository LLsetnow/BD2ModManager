# BD2 Preview Interactions

BD2ModManager 的约会互动插件 0.5.1。Manager 页面为 22 个场景提供独立的 **官方原生入口** 和 **P 额外入口** 设置。

## 在游戏中使用

- **官方原生入口**按设置更新游戏左侧的约会列表，开启的场景会补入对应的官方入口。头像、选角、互动和章节检查由游戏处理。账号已有入口继续走游戏的奖励流程。
- **P 额外入口**在游戏约会界面的「P 预览」面板里显示。它使用本地场景 JSON、音频和当前已启用的约会模组。点击「退出预览」返回原界面。
- 关闭管理总开关后，重新打开约会界面即可恢复游戏原始列表。只关闭原生入口时，仍可打开 P 预览。两种入口都关闭后，游戏会提示并返回。

设置沿用 schemaVersion 2，保存在游戏的 `BepInEx/plugins/BD2PreviewInteractions/settings.json`。保存后重新进入约会界面生效，更换 DLL 后重启游戏。旧版 cfg 的 `UseNativeDatingEntries` 用于读取 schema v1 时迁移 P 入口设置。

## 在约会互动页面配置模组

Manager 在每张角色卡片上提供「配置模组」，直接打开与「角色」页面共用的完整配置窗口，并默认选中「约会」标签。窗口保留立绘、过场动画、约会三个标签，以及模组启停、单项预览、比较全部、删除模组和管理皮肤映射等原有操作。按场景编号定位角色，`illust_dating22_1`、`illust_dating22_2` 等部件会一起列在该角色的约会标签中。

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
- 预览内的 Reset 恢复首个 pose、初始好感度、默认道具和隐藏触点状态，保留当前视角；不实现 Hint Mode。
- 左侧好感度条和道具按钮复用游戏原生 UI 素材，不调用账号好感度/道具逻辑。
- 触点使用 Preview 风格圆点、圆环和拖动箭头，默认透明度 32%。只显示当前 pose、当前道具、好感度范围和隐藏条件允许的点。

道具按钮按列放在原生 Reset 下方，「显示触点」按钮接在道具列下方；位置根据 Reset 的实际屏幕坐标和窗口尺寸更新。长按门槛和触点透明度可在 Manager 调整。P 预览列表使用服装立绘缩略图。

## P 模式加载与隔离

新场景在隐藏的实例中准备，完成后一次切换，加载时保留原画面。JSON 检查、Spine 骨骼解析和首次音频哈希校验在工作线程进行；声音按事件加载，缓存最多 24 个片段，语音与音效同步起播。过期、缺失或错误资源会显示提示并保留或恢复官方 UI。

加载本地预制体时关闭原生互动、骨骼跟随和动画控制脚本，只保留渲染及视觉布局组件，避免缺失骨骼逐帧刷日志及原生输入与本地输入重复响应。P 预览面板直接加载本地角色头像。

BrownDustX 替换共享资源前保存官方原始引用，P 项使用独立 `SkeletonDataAsset`，默认应用对应 `illust_datingN` 当前已启用的模组。缺少触点辅助骨骼时仅向私有实例补充官方辅助骨骼及父子关系，不修改源模组。

## 构建与检查

源码纳入 Manager 的 `plugins/BD2PreviewInteractions`，不再单独维护旧目录。

```powershell
# Manager 构建会先构建插件并嵌入 DLL
pnpm build
# 可通过 BD2_GAME_ROOT 指定包含游戏 exe 的目录
$env:BD2_GAME_ROOT = 'F:\Neowiz\Browndust2\Browndust2_10000001'
pnpm build:game-plugin
# 检查原生列表补入、筛选、顺序和账号列表保留
dotnet run --project plugins/BD2PreviewInteractions/tests/NativeDatingEntryTests/NativeDatingEntryTests.csproj -c Release
# 检查双入口独立开关与旧配置迁移
 dotnet build plugins/BD2PreviewInteractions/tests/DatingSettingsTests/DatingSettingsTests.csproj -c Release
 dotnet exec plugins/BD2PreviewInteractions/tests/DatingSettingsTests/bin/Release/net10.0/DatingSettingsTests.dll
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
