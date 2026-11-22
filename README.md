# Brown Dust 2 Mod Manager

**简体中文** | [English](README.en.md)

[![GitHub License](https://img.shields.io/github/license/LLsetnow/BD2ModManager)](https://github.com/LLsetnow/BD2ModManager/blob/main/LICENSE)
[![GitHub Release](https://img.shields.io/github/v/release/LLsetnow/BD2ModManager)](https://github.com/LLsetnow/BD2ModManager/releases)

> 🛠 轻松管理、预览和同步《棕色尘埃 2》的 Mod。
>
> 🎉 **下载最新版本：** [GitHub Releases](https://github.com/LLsetnow/BD2ModManager/releases)
>


如果您有改进建议或遇到问题，欢迎提交 Issue 或联系作者。

> [!WARNING]
> **卸载《棕色尘埃 2》前请注意**
>
> 如果使用**符号链接（Symlink）**同步方式，请先取消所有 Mod 的同步，再卸载游戏。否则，游戏卸载程序可能会删除暂存目录中的 Mod。

---

## 🛠️ 使用方法

1. 从 [GitHub Releases](https://github.com/LLsetnow/BD2ModManager/releases) 下载应用。
2. 首次启动时，确认引导中检测到的游戏目录，或手动选择包含 `BrownDust II.exe` 的目录。
   - 示例：`F:/Neowiz/Browndust2/Browndust2_10000001`
3. 继续前往「模组加载器」，依次安装 BepInEx、BrownDustX 和 Configuration Manager，然后点击「完成设置」。
   - 在「模组加载器」中从 GitHub 安装 BepInEx，或手动选择本地安装包。
   - 可从 BrownDustX Discord 下载：[discord.gg/B3Aqz6tDG2](https://discord.gg/B3Aqz6tDG2)

   - 手动安装步骤：

     **安装 BepInEx**

     将 BepInEx 压缩包解压到**游戏目录**（可在 Mod Manager 中打开），**不要**解压到启动器目录。

     解压后目录结构应类似：
     ```text
     Browndust2_10000001/
     ├─ BepInEx/
     ├─ winhttp.dll
     └─ BrownDust II.exe
     ```

     > BrownDustX Discord 提供的 BepInEx 压缩包已包含 ConfigurationManager。

     **安装 BrownDustX**

     将 `BrownDustX-[VERSION].zip` 中的 `BepInEx` 文件夹解压到同一个游戏目录。

     解压后目录结构应类似：
     ```text
     Browndust2_10000001/
     ├─ BepInEx/
     │  └─ plugins/
     │     └─ BrownDustX/
     └─ BrownDust II.exe
     ```
4. 验证安装。
   - 启动游戏。在加载界面右上角应能看到红色的游戏版本和 BrownDustX 版本。
5. 添加 Mod：将 Mod 放入 `mods/` 文件夹。
   - ⚠️ 这不是 BrownDustX 的 `mods` 文件夹，而是 Mod Manager 单独使用的目录。
6. 启用或禁用 Mod。
7. 同步 Mod 以应用更改。
   - 同步后，Mod Manager 会在 BrownDustX 的 mods 目录中创建 `BD2MM` 文件夹，并放入所有已启用的 Mod。

> ⚠️ 启用、禁用、删除或重命名 Mod 后，都必须重新同步，才能将更改应用到游戏目录。

### 同步方式：复制或符号链接

选择将 Mod 同步到 BrownDustX `mods` 目录的方式：

#### 📁 复制

将所有已启用的 Mod 复制到目标目录。

- ✅ 无需管理员权限
- ❌ 速度较慢
- ❌ 占用更多磁盘空间

#### 🔗 符号链接

创建指向 Mod 文件的链接，而不是复制文件。

- ✅ 速度更快
- ✅ 节省磁盘空间
- ❌ 需要管理员权限

同步后可点击窗口右上角的同步状态，查看每个 Mod 的同步结果。 [查看界面截图](#界面截图)

## 服装外观映射与约会互动插件：文件和安装

两个插件都在游戏中运行，并由 BepInEx 加载。Mod Manager 负责配置、安装和更新。安装时，Manager 会将内置 DLL 及对应的设置文件写入所选游戏目录。

| 插件 | 主 DLL | 设置文件 | 用途 |
| --- | --- | --- | --- |
| **服装外观映射** | `BD2CostumeSkins.dll` | `BepInEx/plugins/BD2CostumeSkins/settings.json` | 运行时将源服装的外观映射到目标皮肤，并处理游戏内的 Skin 按钮。 |
| **约会互动** | `BD2PreviewInteractions.dll` | `BepInEx/plugins/BD2PreviewInteractions/settings.json` | 管理 P 额外入口和游戏原生约会入口，并为 P 模式加载场景、互动点、音频和界面。 |

### 安装后的目录结构

以下路径均相对于包含 `BrownDust II.exe` 的游戏目录：

```text
游戏目录/
└── BepInEx/
    ├── plugins/
    │   ├── BD2CostumeSkins/
    │   │   ├── BD2CostumeSkins.dll
    │   │   └── settings.json
    │   └── BD2PreviewInteractions/
    │       ├── BD2PreviewInteractions.dll
    │       └── settings.json
    └── config/
        └── local.bd2.previewinteractions.cfg
```

请先安装 BepInEx。通过 Mod Manager 安装或更新任一插件前，请退出游戏。游戏启动时，BepInEx 会加载这些 DLL。约会互动插件加载时也会生成 `.cfg` 文件，用于保存额外的插件选项和诊断设置。

### 设置文件与外部资源

- **服装外观映射 `settings.json`**：保存总开关、源服装到目标服装的映射、各项映射开关和选中的皮肤。插件使用游戏现有资源，也可以通过 BrownDustX 使用已启用的资源替换 Mod。
- **约会互动 settings.json**：保存各场景的 P 入口和原生入口开关、鼠标左键按住时长与互动标记不透明度。约会配置和资源路径不再写入此设置文件。
- **外部约会资源**：约会 JSON、音频及 ModPreview 素材会按需从 [LLsetnow/Brown-Dust-2-Asset](https://github.com/LLsetnow/Brown-Dust-2-Asset) 下载到本地素材库。

每个插件的 `settings.json` 用于配置对应插件；`illust_dating*.json` 文件描述互动内容。Mod Manager 还会在应用数据目录保存名为 `costume-skins.json` 和 `local-interactions.json` 的配置副本。

### 构建产物

`npm run build:game-plugin` 会构建两个插件。每个项目的 `bin/Release/netstandard2.1/` 目录包含 DLL、`.pdb` 调试符号和 `.deps.json` 依赖元数据。构建脚本会将两个 DLL 复制到 `src-tauri/resources/`，并嵌入 Mod Manager 可执行文件，以供插件安装功能使用。当前安装流程会部署 DLL 和设置文件。

## 素材库目录

美术素材库默认位于 `BD2ModManager.exe` 同目录下的 `asset` 文件夹。可以在 **设置 → 常规 → 目录 → 美术素材库目录** 中选择其他目录。打开约会互动设置时，管理器会检查 `audio/dating` 中的场景 JSON 并下载缺失文件。ModPreview 也会将按需下载的素材保存到同一个素材库。 [约会互动素材](https://github.com/LLsetnow/Brown-Dust-2-Asset/tree/master/audio/dating)

---

## 界面截图

以下截图来自 BD2ModManager `v4.3.1-local.6` 源码构建的简体中文界面。模组条目、配置文件和目录使用演示数据。

### Mod 管理

查看启用状态和当前配置文件，按名称、角色或作者搜索，或按立绘、过场动画、场景、约会、NPC 和小游戏筛选。

![Mod 列表、搜索框、类型筛选和同步按钮](./screenshots/v4.3.1-local.6/mod-library.png)

<details>
<summary>导入、筛选、批量操作、冲突与同步</summary>

从「添加模组」菜单导入 ZIP 压缩包或已有文件夹。

![从 ZIP 或文件夹导入 Mod](./screenshots/v4.3.1-local.6/mod-import.png)

高级筛选可定位已启用、已禁用、有冲突或有错误的 Mod。

![Mod 列表的高级筛选菜单](./screenshots/v4.3.1-local.6/mod-filters.png)

右键单个 Mod，可预览、重命名、修改作者或复制并迁移角色 ID。

![单个 Mod 的右键菜单](./screenshots/v4.3.1-local.6/mod-actions.png)

多选后可批量启用、禁用、修改作者或删除。

![多选三个 NPC Mod 后的批量操作菜单](./screenshots/v4.3.1-local.6/mod-bulk-actions.png)

发现冲突时，可预览候选 Mod，选择保留项后应用并同步。

![同一服装的两个立绘 Mod 及冲突处理选项](./screenshots/v4.3.1-local.6/mod-conflicts.png)

点击标题栏的同步状态，可查看完成进度和每个 Mod 的处理结果。

![同步完成后的进度条与逐项日志](./screenshots/v4.3.1-local.6/mod-sync.png)

</details>

### 角色图鉴与 ModPreview

角色网格显示服装、Mod 数量和各类型 Mod 的状态，可按 Mod 状态、发布时间或联动属性筛选。

![仅显示已有 Mod 的角色网格及筛选条件](./screenshots/v4.3.1-local.6/character-gallery.png)

<details>
<summary>角色列表、服装详情、立绘对比和技能动画</summary>

列表视图按角色归拢服装，并列出过场动画、立绘和约会 Mod 的状态。分组标题跟随角色名称设置。

![按角色分组的服装列表](./screenshots/v4.3.1-local.6/character-list.png)

打开服装详情后，可按类型管理 Mod、逐项预览、比较同类 Mod，或前往皮肤映射。

![服装详情中的 Mod 分类、启用状态与比较入口](./screenshots/v4.3.1-local.6/character-costume.png)

从「比较全部」打开 ModPreview `v1.0.5-local.2`，可并排播放两套立绘，并将多个预览加入同一组联动控制。

![ModPreview 并排播放两个立绘 Mod](./screenshots/v4.3.1-local.6/preview-compare.png)

技能过场预览提供时间轴、游戏动态背景和技能音频。

![格雷的技能过场、日语音频状态和播放时间轴](./screenshots/v4.3.1-local.6/preview-skill.png)

</details>

### 皮肤映射与约会互动

约会互动分别管理游戏左侧列表的官方原生入口和本地 P 入口。每个角色都可独立开关，场景卡片会显示本地 JSON 的状态。在游戏约会界面点击「P 预览」打开本地角色列表。

![约会互动双入口总开关、角色开关与场景 JSON 状态](./screenshots/v4.3.1-local.6/dating-interactions.png)

<details>
<summary>服装皮肤映射与单角色约会 Mod</summary>

为同一角色添加目标皮肤，选择当前外观，并单独启用或停用各项映射。

![来源服装、目标皮肤和已配置的映射](./screenshots/v4.3.1-local.6/costume-skin-mapping.png)

从约会角色卡打开对应的 Mod 列表，可启用、禁用或预览该角色的约会 Mod。

![从约会页面打开的单角色约会 Mod 详情](./screenshots/v4.3.1-local.6/dating-mods.png)

</details>

### 配置文件与模组加载器

<details>
<summary>配置文件、模板和加载器组件</summary>

保存多套 Mod 组合，通过侧栏切换当前配置文件，并查看每套配置中启用的 Mod。

![配置文件列表与当前配置启用的 Mod](./screenshots/v4.3.1-local.6/profiles.png)

创建配置文件时，可使用现有配置作为模板。

![填写名称、描述并选择配置文件模板](./screenshots/v4.3.1-local.6/profile-create.png)

模组加载器显示 BepInEx、BrownDustX 和 Configuration Manager 的版本及安装状态，支持拖放 BepInEx 或 BrownDustX 安装包。

![模组加载器中的组件版本、安装状态和拖放区域](./screenshots/v4.3.1-local.6/mod-loader.png)

</details>

### 应用设置

<details>
<summary>目录、同步、主题、列表显示、约会操作、更新与迁移</summary>

常规设置集中管理语言、主题、游戏目录、Mod 暂存目录、美术素材库和同步方式。

![常规设置中的三个目录和同步选项](./screenshots/v4.3.1-local.6/settings-general.png)

主题编辑器可自定义界面颜色，包括强调色、边框、文本、Mod 类型和状态。

![创建主题时的颜色分类和色值](./screenshots/v4.3.1-local.6/theme-editor.png)

界面设置可选择 Mod 列表的显示列，并调整角色、类型图标和颜色。

![Mod 列表的列与样式设置](./screenshots/v4.3.1-local.6/settings-interface.png)

在约会互动设置中调整 P 额外入口的左键长按时间和触点透明度。

![约会互动的长按时间与触点透明度](./screenshots/v4.3.1-local.6/settings-dating.png)

更新页显示管理器和 ModPreview 版本，并提供版本检查、更新渠道和自动更新选项。

![管理器与 ModPreview 的版本及更新设置](./screenshots/v4.3.1-local.6/settings-updates.png)

实验性功能可从 v3.2.4 或更早版本导入配置文件和 Mod 作者。

![旧版配置文件及 Mod 作者导入入口](./screenshots/v4.3.1-local.6/settings-migration.png)

</details>

---

## ❓ 常见问题

### 从哪里获取 Mod？

可以在 BrownDustX Discord 社区获取 Mod：[https://discord.gg/B3Aqz6tDG2](https://discord.gg/B3Aqz6tDG2)

## 本地 Fork 版本

包含本地约会互动、服装外观映射以及对应 Preview 的 Windows x64 版本发布在 [LLsetnow/BD2ModManager Releases](https://github.com/LLsetnow/BD2ModManager/releases)。版本号中包含 `-local.` 的构建会检查此 Fork，并打开其 Releases 页面供手动更新。Preview 更新器也会读取 [LLsetnow/BD2ModPreview Releases](https://github.com/LLsetnow/BD2ModPreview/releases)。
