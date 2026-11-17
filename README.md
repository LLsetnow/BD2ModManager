# Brown Dust 2 Mod Manager

**简体中文** | [English](README.en.md)

[![GitHub License](https://img.shields.io/github/license/LLsetnow/BD2ModManager)](https://github.com/LLsetnow/BD2ModManager/blob/main/LICENSE)
[![GitHub Release](https://img.shields.io/github/v/release/LLsetnow/BD2ModManager)](https://github.com/LLsetnow/BD2ModManager/releases)

> 🛠 轻松管理、预览和同步《棕色尘埃 2》的 Mod。
>
> 🎉 **下载最新版本：** [GitHub Releases](https://github.com/LLsetnow/BD2ModManager/releases)
>

作者：[@LLsetnow](https://github.com/LLsetnow)

如果您有改进建议或遇到问题，欢迎提交 Issue 或联系作者。

> [!WARNING]
> **卸载《棕色尘埃 2》前请注意**
>
> 如果使用**符号链接（Symlink）**同步方式，请先取消所有 Mod 的同步，再卸载游戏。否则，游戏卸载程序可能会删除暂存目录中的 Mod。

---

## 🛠️ 使用方法

1. 从 [GitHub Releases](https://github.com/LLsetnow/BD2ModManager/releases) 下载应用。
2. 选择《棕色尘埃 2》的游戏目录（其中包含 `BrownDust II.exe`）。
   - 示例：`F:/Neowiz/Browndust2/Browndust2_10000001`
3. 安装 BepInEx 和 BrownDustX。
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

#### 示例对比（383 个 Mod）

| 复制 | 符号链接 |
|------|---------|
| <img src="./screenshots/sync_copy_v405.gif" width="260" /> | <img src="./screenshots/sync_symlink_v405.gif" width="260" /> |

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

在 **设置 → 常规 → 目录 → 美术素材库目录** 中选择本地目录。打开约会互动设置时，管理器会检查 `audio/dating` 中的场景 JSON，并从 [LLsetnow/Brown-Dust-2-Asset/audio/dating](https://github.com/LLsetnow/Brown-Dust-2-Asset/tree/master/audio/dating) 下载缺失文件。ModPreview 访问缺失素材时，也会从该仓库按需下载并保存到相同的素材库根目录，不使用 AppData 作为素材缓存位置。

---

## 📸 截图

### Mod 页面（v4.0.1）
![Mod 页面](./screenshots/mods_page_v401.png)

### 角色页面（v4.0.1）
![角色页面](./screenshots/characters_page_v401.png)

### Mod 预览（BD2ModPreview v1.0.0）
![Mod 预览](./screenshots/bd2modpreview_v100v2.png)

---

## ❤️ 支持项目

如果您喜欢这款 Mod 管理器并希望支持后续开发，可以通过以下方式支持：

- Ko-fi：https://ko-fi.com/bruhnn

## ❓ 常见问题

### 从哪里获取 Mod？

可以在 BrownDustX Discord 社区获取 Mod：[https://discord.gg/B3Aqz6tDG2](https://discord.gg/B3Aqz6tDG2)

## ✨ Star History

<a href="https://www.star-history.com/?repos=bruhnn%2Fbd2modmanager&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=bruhnn/bd2modmanager&type=date&theme=dark&legend=top-left&sealed_token=VQzqGVDS8aADxliuGNidMHsOzgTt1UN6wielhchPghaRGoXLtIgp5QZVGbMvw2tQ4YaeKFQO7Jqs0gOIFhGoladBRnTqNSQbPrUnfZ8v2HtYHP_vHRZIkNA_zi3ya__awL_ybKHxWvTleXDD5UoC4QhfI0cdfol4BKEppmMlqM6TUCqgec9qcjzkYiZw" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=bruhnn/bd2modmanager&type=date&legend=top-left&sealed_token=VQzqGVDS8aADxliuGNidMHsOzgTt1UN6wielhchPghaRGoXLtIgp5QZVGbMvw2tQ4YaeKFQO7Jqs0gOIFhGoladBRnTqNSQbPrUnfZ8v2HtYHP_vHRZIkNA_zi3ya__awL_ybKHxWvTleXDD5UoC4QhfI0cdfol4BKEppmMlqM6TUCqgec9qcjzkYiZw" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=bruhnn/bd2modmanager&type=date&legend=top-left&sealed_token=VQzqGVDS8aADxliuGNidMHsOzgTt1UN6wielhchPghaRGoXLtIgp5QZVGbMvw2tQ4YaeKFQO7Jqs0gOIFhGoladBRnTqNSQbPrUnfZ8v2HtYHP_vHRZIkNA_zi3ya__awL_ybKHxWvTleXDD5UoC4QhfI0cdfol4BKEppmMlqM6TUCqgec9qcjzkYiZw" />
 </picture>
</a>

## 本地 Fork 版本

包含本地约会互动、服装外观映射以及对应 Preview 的 Windows x64 版本发布在 [LLsetnow/BD2ModManager Releases](https://github.com/LLsetnow/BD2ModManager/releases)。版本号中包含 `-local.` 的构建会检查此 Fork，并打开其 Releases 页面供手动更新。Preview 更新器也会读取 [LLsetnow/BD2ModPreview Releases](https://github.com/LLsetnow/BD2ModPreview/releases)。
