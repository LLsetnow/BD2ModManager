# BD2 Preview Interactions

BD2ModManager 的本地 BepInEx 插件，默认在约会头像栏加入六项 P 互动：dating 7、9、12、18、19、21。阿奇拉 22 不在默认条目中。22 个导出 JSON 可以共存，只有 Manager 中启用的条目才加入头像栏。

## 使用

1. 在已授权的 Preview 中打开需要的互动，点击「导出互动 JSON…」，选择保存位置。只导出当前 `illust_datingN.json`，保留授权有效期，不下载或复制音频。
2. 在 Manager 的「本地互动」页面选择 JSON 目录、素材库的 `audio/dating` 音频目录，管理条目的添加、移除、启停及是否应用已启用的模组。
3. 退出游戏后，在该页安装或更新插件。保存配置后重新进入游戏约会界面生效。

「本地互动总开关」关闭后，重新进入约会界面不再加入 P 项。各条目的启停和资源模式会保留，重新开启后恢复；没有总开关字段的旧配置默认开启。管理列表显示对应服装的立绘缩略图。

默认 JSON 目录是 `F:\Neowiz\mods\dating-interaction-configs`，可以在 Manager 中更改。配置写入游戏 `BepInEx/plugins/BD2PreviewInteractions/settings.json`，Manager 另存于自己的 AppData；游戏无需 Preview 同时运行。

本地互动只读取场景和音频；不写入官方账号进度、录制、奖励。导出到期后重新通过已授权 Preview 导出。

## 操作与显示

- 滚轮缩放；只有右键拖动移动画面。
- 左键点按触点；拖动互动触点需先长按，默认 0.35 秒，再移动至少 5 屏幕像素。短按或空白左键不移动画面。
- 长按蓄力按配置时长结算，一次按下仅结算一次；松开、切换道具、pose 或退出场景会清理对应输入状态。
- 原生右上角 Reset 恢复首个 pose、初始好感度、默认道具和隐藏触点状态，保留当前视角；不实现 Hint Mode。
- 左侧好感度条和道具按钮复用游戏原生 UI 素材，不调用账号好感度/道具逻辑。
- 触点使用 Preview 风格圆点、圆环和拖动箭头，默认透明度 32%。只显示当前 pose、当前道具、好感度范围和隐藏条件允许的点。

道具按钮按列放在原生 Reset 下方，「显示触点」按钮接在道具列下方；位置根据 Reset 的实际屏幕坐标和窗口尺寸更新。长按门槛和触点透明度可在 Manager 调整。头像使用服装立绘的完整取景，保留 P 标记。

## 加载与隔离

新场景在隐藏的实例中准备，完成后一次切换，加载时保留原画面。JSON 检查、Spine 骨骼解析和首次音频哈希校验在工作线程进行；声音按事件加载，缓存最多 24 个片段，语音与音效同步起播。过期、缺失或错误资源会显示提示并保留或恢复官方 UI。

加载本地预制体时关闭原生互动、骨骼跟随和动画控制脚本，只保留渲染及视觉布局组件，避免缺失骨骼逐帧刷日志及原生输入与本地输入重复响应。头像单元格绕开官方缩略图解析，由本地立绘加载器显示头像。

BrownDustX 替换共享资源前保存官方原始引用，P 项使用独立 `SkeletonDataAsset`。默认应用对应 `illust_datingN` 当前已启用的模组；在 Manager 取消该条目的「使用已启用模组」可改用原版，不全局停用其他模组。缺少触点辅助骨骼时仅向私有实例补充官方辅助骨骼及父子关系，不修改源模组。

列表本地编号为 `1900000000 + N`，注入前检查冲突。插件针对当前 `SpineInteractionUI` 方法签名；签名不兼容则撤销钩子并记录错误。

## 构建与检查

源码纳入 Manager 的 `plugins/BD2PreviewInteractions`，不再单独维护旧目录。

```powershell
# Manager 构建会先构建插件并嵌入 DLL
pnpm build
# 可通过 BD2_GAME_ROOT 指定包含游戏 exe 的目录
$env:BD2_GAME_ROOT = 'F:\Neowiz\Browndust2\Browndust2_10000001'
pnpm build:game-plugin
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
