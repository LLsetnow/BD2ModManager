
# Brown Dust 2 Mod Manager

[![GitHub License](https://img.shields.io/github/license/bruhnn/BD2ModManager)](https://github.com/bruhnn/BD2ModManager/blob/main/LICENSE)
[![GitHub Release](https://img.shields.io/github/v/release/bruhnn/BD2ModManager)](https://github.com/bruhnn/BD2ModManager/releases)
[![Total Downloads](https://img.shields.io/endpoint?url=https://shy-waterfall-2797.bruhnn.workers.dev/downloads)](https://github.com/bruhnn/BD2ModManager/releases)


> 🛠 Easily manage, preview, and sync mods for Brown Dust 2.
> 
> 🎉 **Download the latest version:** [GitHub Releases](https://github.com/bruhnn/BD2ModManager/releases)
>

If you have suggestions or run into any problems with the app, feel free to open an issue or contact me.

> [!WARNING]
> **Before Uninstalling Brown Dust II !!!**
>
> If you use the **Symlink** sync method, unsync all mods before uninstalling the game. Otherwise, the uninstaller may delete your mods from the staging folder.

---

## 🛠️ How to Use
1. **Download** the app from [GitHub Releases](https://github.com/bruhnn/BD2ModManager/releases).
2. **Select your Brown Dust 2 directory** (where `BrownDust II.exe` is located)
   - Example: F:\Neowiz\Browndust2\Browndust2_10000001
3. **Install BepInEx and BrownDustX**
   - Download both from the BrownDustX Discord:
     [discord.gg/B3Aqz6tDG2](https://discord.gg/B3Aqz6tDG2)

   - Manual Steps:
        
      **Install BepInEx**
      
      Extract the contents of the BepInEx archive into the **game folder** (you can open the game folder from the mod manager)
      (**NOT** the launcher folder).
     
      Your folder should look like this:
      ```text
         Browndust2_10000001/
         ├─ BepInEx/
         ├─ winhttp.dll
         └─ BrownDust II.exe
      ```
  
      > The BepInEx archive from the BrownDustX Discord already includes ConfigurationManager.
   
      **Install BrownDustX**
     
      Extract the `BepInEx` folder from `BrownDustX-[VERSION].zip`
      into the same game folder.
     
      Your folder should look like this:
      ```text
         Browndust2_10000001/
         ├─ BepInEx/
         │  └─ plugins/
         │     └─ BrownDustX/
         └─ BrownDust II.exe
      ```
4. **Verify the installation**
   - Launch the game.
   - On the loading screen, the game version and BrownDustX version should appear in red at the top-right corner.
5. **Add your mods** by:
   - Moving them into the `mods/` folder  
     ⚠️ **Note:** This is *not* the BrownDustX `mods` directory. It's a separate folder used by this manager
6. **Enable or disable mods**.
7. **Sync your mods** to apply changes:
   - This will create a folder named `BD2MM` inside the `BrownDustX` mods folder with all your enabled mods.

> ⚠️ After making any changes (enable, disable, delete, rename), you **must sync** your mods to update the game folder.

### Sync Method: Copy vs Symlink

Choose how mods are synced to your BrownDust X `mods` folder:

#### 📁 Copy
Copies all enabled mods into the folder.  

- ✅ No admin rights needed
- ❌ Slower
- ❌ Uses more disk space

#### 🔗 Symlink  
Creates shortcuts instead of copying files.  

- ✅ Much faster
- ✅ Saves disk space
- ❌ Requires admin rights

#### Example Comparison (383 mods)

| Copy | Symlink |
|------|---------|
| <img src="./screenshots/sync_copy_v405.gif" width="260" /> | <img src="./screenshots/sync_symlink_v405.gif" width="260" /> |

## 皮肤映射与约会互动插件：文件产物

两个插件均由游戏内的 BepInEx 加载执行。Manager 负责编辑配置、安装和更新插件；安装时会将内置的 DLL 和对应配置写入所选游戏目录。

| 插件 | 核心 DLL | 配置文件 | 作用 |
| --- | --- | --- | --- |
| **皮肤映射** | `BD2CostumeSkins.dll` | `BepInEx/plugins/BD2CostumeSkins/settings.json` | 在游戏运行时应用来源服装到目标皮肤的外观映射，并处理游戏内的 Skin 按钮。 |
| **约会互动** | `BD2PreviewInteractions.dll` | `BepInEx/plugins/BD2PreviewInteractions/settings.json` | 管理 P／原生约会入口，并为 P 模式加载互动场景、触点、音频和界面。 |

### 安装后的目录结构

以下路径以包含 `BrownDust II.exe` 的游戏目录为起点：

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

请先安装 BepInEx，并在退出游戏后通过 Manager 对应页面安装或更新插件。游戏启动后由 BepInEx 加载 DLL；约会互动的 `.cfg` 文件由 BepInEx 在加载插件时生成，用于部分插件选项和诊断设置。

### 配置文件与外部资源

- **皮肤映射 `settings.json`**：保存总开关、来源服装与目标皮肤的映射、单项启停和当前皮肤选择。插件使用游戏已有的美术资源，也可配合 BrownDustX 使用已启用的资源替换模组。
- **约会互动 `settings.json`**：保存各场景的 P／原生入口开关、互动 JSON／音频／美术素材目录，以及左键长按时间、触点透明度等设置。
- **约会互动的外部资源**：P 模式使用的 `illust_dating*.json` 描述互动场景；约会音频和美术素材放在设置指定的本地目录中。这些内容独立于插件 DLL，获取位置见下方“约会互动：资源来源与目录设置”。

两个插件目录里的 `settings.json` 分别控制各自的插件，与描述互动内容的 `illust_dating*.json` 用途不同。Manager 还会在自己的应用数据目录保存 `costume-skins.json` 和 `local-interactions.json` 配置副本。

### 开发构建产物

`npm run build:game-plugin` 同时构建两个插件。各项目的 `bin/Release/netstandard2.1/` 下会生成 DLL，以及 `.pdb` 调试符号和 `.deps.json` 依赖说明。构建脚本将两个 DLL 复制到 `src-tauri/resources/`，随后嵌入 Manager 可执行文件，供“安装插件”功能使用；当前安装流程部署 DLL 和配置文件。

## 约会互动：资源来源与目录设置

打开 **设置 → 约会互动**，为以下三个设置项选择对应的本地文件夹，然后点击 **保存设置**。

| 设置项 | 资源来源 | 应选择的本地目录 |
| --- | --- | --- |
| **互动 JSON 目录** | [LLsetnow/neowiz：dating-interaction-configs](https://github.com/LLsetnow/neowiz/tree/main/mods/dating-interaction-configs) | 下载或克隆后选择 `mods/dating-interaction-configs` 文件夹，其中直接包含 `illust_dating1.json` 至 `illust_dating22.json`。 |
| **约会音频目录** | [bruhnn/Brown-Dust-2-Asset：audio/dating](https://github.com/bruhnn/Brown-Dust-2-Asset/tree/master/audio/dating) | 素材库内的 `audio/dating` 子目录，Windows 路径为 `audio\dating`，例如 `F:\Neowiz\Brown-Dust-2-Asset-master\audio\dating`。 |
| **美术素材库目录** | [bruhnn/Brown-Dust-2-Asset](https://github.com/bruhnn/Brown-Dust-2-Asset/tree/master) | 下载或克隆后的素材库根目录，即包含 `spine`、`ui`、`header` 等目录的文件夹，例如 `F:\Neowiz\Brown-Dust-2-Asset-master`。 |

约会音频与美术素材均可从 [bruhnn/Brown-Dust-2-Asset](https://github.com/bruhnn/Brown-Dust-2-Asset/tree/master) 获取。下载或克隆同一份素材库后，“约会音频目录”选择其中的 `audio/dating`（Windows 路径为 `audio\dating`）子目录，“美术素材库目录”选择素材库根目录。

---

## 📸 Screenshots

### Mods Page (v4.0.1)
![Mods Page](./screenshots/mods_page_v401.png)

### Characters Page (v4.0.1)
![Characters Page](./screenshots/characters_page_v401.png)

### Mod Preview (BD2ModPreview v1.0.0)
![Mod Preview](./screenshots/bd2modpreview_v100v2.png)

---

## ❤️ Support the Project

If you enjoy the mod manager and want to support development, you can support me here:

- Ko-Fi: https://ko-fi.com/bruhnn
- Afdian (🇨🇳): https://afdian.com/a/bruhnn

## ❓ FAQ

### Where can I get mods?
You can find mods on the BrownDustX Discord server: [https://discord.gg/B3Aqz6tDG2](https://discord.gg/B3Aqz6tDG2)

## 🤝 Credits & Thanks

- Character assets by [myssal/Brown-Dust-2-Asset](https://github.com/myssal/Brown-Dust-2-Asset)
- Thanks to **Synae** for *BrownDustX*
- Thanks to everyone who contributed by reporting bugs and suggesting features.  
   ![Alt](https://repobeats.axiom.co/api/embed/ed16651a70e0b4d1937e8e346f394c6b78c9a231.svg "Repobeats analytics image")


## ✨ Star History

<a href="https://www.star-history.com/?repos=bruhnn%2Fbd2modmanager&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=bruhnn/bd2modmanager&type=date&theme=dark&legend=top-left&sealed_token=VQzqGVDS8aADxliuGNidMHsOzgTt1UN6wielhchPghaRGoXLtIgp5QZVGbMvw2tQ4YaeKFQO7Jqs0gOIFhGoladBRnTqNSQbPrUnfZ8v2HtYHP_vHRZIkNA_zi3ya__awL_ybKHxWvTleXDD5UoC4QhfI0cdfol4BKEppmMlqM6TUCqgec9qcjzkYiZw" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=bruhnn/bd2modmanager&type=date&legend=top-left&sealed_token=VQzqGVDS8aADxliuGNidMHsOzgTt1UN6wielhchPghaRGoXLtIgp5QZVGbMvw2tQ4YaeKFQO7Jqs0gOIFhGoladBRnTqNSQbPrUnfZ8v2HtYHP_vHRZIkNA_zi3ya__awL_ybKHxWvTleXDD5UoC4QhfI0cdfol4BKEppmMlqM6TUCqgec9qcjzkYiZw" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=bruhnn/bd2modmanager&type=date&legend=top-left&sealed_token=VQzqGVDS8aADxliuGNidMHsOzgTt1UN6wielhchPghaRGoXLtIgp5QZVGbMvw2tQ4YaeKFQO7Jqs0gOIFhGoladBRnTqNSQbPrUnfZ8v2HtYHP_vHRZIkNA_zi3ya__awL_ybKHxWvTleXDD5UoC4QhfI0cdfol4BKEppmMlqM6TUCqgec9qcjzkYiZw" />
 </picture>
</a>

## Local fork releases

Windows x64 builds with local interactions, costume skin mappings and the matching Preview are published at [LLsetnow/BD2ModManager Releases](https://github.com/LLsetnow/BD2ModManager/releases). Versions containing `-local.` check this fork and open its release page for manual updates. The Preview updater also reads [LLsetnow/BD2ModPreview Releases](https://github.com/LLsetnow/BD2ModPreview/releases).
