
# Brown Dust 2 Mod Manager

[简体中文](README.md) | **English**

[![GitHub License](https://img.shields.io/github/license/LLsetnow/BD2ModManager)](https://github.com/LLsetnow/BD2ModManager/blob/main/LICENSE)
[![GitHub Release](https://img.shields.io/github/v/release/LLsetnow/BD2ModManager)](https://github.com/LLsetnow/BD2ModManager/releases)


> 🛠 Easily manage, preview, and sync mods for Brown Dust 2.
> 
> 🎉 **Download the latest version:** [GitHub Releases](https://github.com/LLsetnow/BD2ModManager/releases)
>

Author: [@LLsetnow](https://github.com/LLsetnow)

If you have suggestions or run into any problems with the app, feel free to open an issue or contact me.

> [!WARNING]
> **Before Uninstalling Brown Dust II !!!**
>
> If you use the **Symlink** sync method, unsync all mods before uninstalling the game. Otherwise, the uninstaller may delete your mods from the staging folder.

---

## 🛠️ How to Use
1. **Download** the app from [GitHub Releases](https://github.com/LLsetnow/BD2ModManager/releases).
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

## Skin Mapping and Dating Interaction Plugins: Files and Installation

Both plugins run inside the game and are loaded by BepInEx. Manager handles configuration, installation, and updates. During installation, it writes the bundled DLL and the corresponding settings file to the selected game directory.

| Plugin | Main DLL | Settings file | Purpose |
| --- | --- | --- | --- |
| **Skin mapping** | `BD2CostumeSkins.dll` | `BepInEx/plugins/BD2CostumeSkins/settings.json` | Applies appearance mappings from source costumes to target skins at runtime and handles the in-game Skin button. |
| **Dating interactions** | `BD2PreviewInteractions.dll` | `BepInEx/plugins/BD2PreviewInteractions/settings.json` | Manages P and native dating entries, and loads scenes, interaction points, audio, and UI for P mode. |

### Installed directory structure

The paths below start at the game directory containing `BrownDust II.exe`:

```text
Game directory/
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

Install BepInEx first, then exit the game before installing or updating either plugin through its Manager page. BepInEx loads the DLLs when the game starts. It also generates the dating interaction plugin's `.cfg` file when loading the plugin; this file stores additional plugin options and diagnostic settings.

### Settings files and external resources

- **Skin mapping `settings.json`** stores the master switch, source-to-target costume mappings, individual mapping switches, and the selected skins. The plugin uses existing game assets and can also use enabled asset replacement mods through BrownDustX.
- **Dating interaction `settings.json`** stores each scene's P and native entry switches, the interaction JSON, audio, and asset directories, and options such as the left-button hold duration and interaction marker opacity.
- **External dating resources:** P mode uses `illust_dating*.json` files to describe interaction scenes. Dating audio and artwork are stored in the local directories selected in settings. These resources are separate from the plugin DLL. Download locations are listed in [Dating interaction resources and directory settings](#dating-interactions-resource-sources-and-directory-settings) below.

Each plugin's `settings.json` controls that plugin. The `illust_dating*.json` files describe interaction content. Manager also saves configuration copies named `costume-skins.json` and `local-interactions.json` in its application data directory.

### Build outputs

`npm run build:game-plugin` builds both plugins. Each project's `bin/Release/netstandard2.1/` directory contains its DLL, `.pdb` debug symbols, and `.deps.json` dependency metadata. The build script copies both DLLs to `src-tauri/resources/`, where they are embedded into the Manager executable for the plugin installation feature. The current installation process deploys the DLLs and settings files.

<a id="dating-interactions-resource-sources-and-directory-settings"></a>

## Dating Interactions: Resource Sources and Directory Settings

Open **Settings → Dating interactions**, select the appropriate local folder for each of the following settings, then click **Save settings**.

| Setting | Resource source | Local folder to select |
| --- | --- | --- |
| **Interaction JSON directory** | [LLsetnow/neowiz: dating-interaction-configs](https://github.com/LLsetnow/neowiz/tree/main/mods/dating-interaction-configs) | After downloading or cloning the repository, select `mods/dating-interaction-configs`, which directly contains `illust_dating1.json` through `illust_dating22.json`. |
| **Dating audio directory** | [bruhnn/Brown-Dust-2-Asset: audio/dating](https://github.com/bruhnn/Brown-Dust-2-Asset/tree/master/audio/dating) | Select the asset library's `audio/dating` subdirectory (`audio\dating` on Windows), for example `F:\Neowiz\Brown-Dust-2-Asset-master\audio\dating`. |
| **Artwork asset library directory** | [bruhnn/Brown-Dust-2-Asset](https://github.com/bruhnn/Brown-Dust-2-Asset/tree/master) | Select the root of the downloaded or cloned asset library, containing folders such as `spine`, `ui`, and `header`, for example `F:\Neowiz\Brown-Dust-2-Asset-master`. |

Both dating audio and artwork are available from [bruhnn/Brown-Dust-2-Asset](https://github.com/bruhnn/Brown-Dust-2-Asset/tree/master), a library based on assets by [myssal](https://github.com/myssal/Brown-Dust-2-Asset). After downloading or cloning this asset library, set **Dating audio directory** to its `audio/dating` subdirectory (`audio\dating` on Windows), and set **Artwork asset library directory** to the asset library's root.

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

## ❓ FAQ

### Where can I get mods?
You can find mods on the BrownDustX Discord server: [https://discord.gg/B3Aqz6tDG2](https://discord.gg/B3Aqz6tDG2)

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
