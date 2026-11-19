
# Brown Dust 2 Mod Manager

[简体中文](README.md) | **English**

[![GitHub License](https://img.shields.io/github/license/LLsetnow/BD2ModManager)](https://github.com/LLsetnow/BD2ModManager/blob/main/LICENSE)
[![GitHub Release](https://img.shields.io/github/v/release/LLsetnow/BD2ModManager)](https://github.com/LLsetnow/BD2ModManager/releases)


> 🛠 Easily manage, preview, and sync mods for Brown Dust 2.
> 
> 🎉 **Download the latest version:** [GitHub Releases](https://github.com/LLsetnow/BD2ModManager/releases)
>

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

After syncing, click the sync status in the top-right corner to see the result for each mod. [View screenshots](#screenshots)

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
- **Dating interaction `settings.json`** stores each scene's P and native entry switches, the left mouse button hold duration, and interaction marker opacity.
- **External dating resources** include scene JSON, audio, and ModPreview assets. The manager and ModPreview download the files they need to the configured asset library. [Brown Dust 2 Asset Library](https://github.com/LLsetnow/Brown-Dust-2-Asset)

Each plugin's `settings.json` controls that plugin. The `illust_dating*.json` files describe interaction content. Manager also saves configuration copies named `costume-skins.json` and `local-interactions.json` in its application data directory.

### Build outputs

`npm run build:game-plugin` builds both plugins. Each project's `bin/Release/netstandard2.1/` directory contains its DLL, `.pdb` debug symbols, and `.deps.json` dependency metadata. The build script copies both DLLs to `src-tauri/resources/`, where they are embedded into the Manager executable for the plugin installation feature. The current installation process deploys the DLLs and settings files.

<a id="dating-interactions-resource-sources-and-directory-settings"></a>

## Asset Library

Select a local root folder under **Settings → General → Directories → Art Asset Library Directory**. When you open Dating Interactions, the manager checks for scene JSON under `audio/dating` and downloads missing files. ModPreview also saves assets it downloads to this library root. [Brown Dust 2 dating scene assets](https://github.com/LLsetnow/Brown-Dust-2-Asset/tree/master/audio/dating)

---

## Screenshots

These screenshots show a BD2ModManager `v4.3.1-local.6` source build in Simplified Chinese with example mods, profiles, and directory paths.

### Mod Management

See which mods are enabled in the active profile. Search by mod name, character, or author, and filter by standing portrait, cutscene, scene, dating, NPC, or minigame.

![Mod library with search, type filters, and sync controls](./screenshots/v4.3.1-local.6/mod-library.png)

<details>
<summary>Importing, filtering, bulk actions, conflicts, and sync</summary>

Use **Add Mod** to import a ZIP archive or an existing folder.

![Add Mod menu with ZIP and folder import options](./screenshots/v4.3.1-local.6/mod-import.png)

Use advanced filters to find enabled or disabled mods, conflicts, and errors.

![Advanced filters for the mod list](./screenshots/v4.3.1-local.6/mod-filters.png)

Right-click a mod to preview it, rename it, edit its author, or copy it to another character ID.

![Context menu for a single mod](./screenshots/v4.3.1-local.6/mod-actions.png)

Select several mods to enable, disable, edit their authors, or delete them together.

![Bulk action menu for three selected NPC mods](./screenshots/v4.3.1-local.6/mod-bulk-actions.png)

When the manager detects a conflict, preview the candidates and select which mod to keep before syncing.

![Conflict resolution for two standing portrait mods targeting one costume](./screenshots/v4.3.1-local.6/mod-conflicts.png)

Click the sync status in the title bar to see progress and the result for each mod.

![Completed sync with a progress bar and per-mod log](./screenshots/v4.3.1-local.6/mod-sync.png)

</details>

### Character Gallery and ModPreview

The character grid shows costumes, mod counts, and mod status by type. Filters include mod status, release dates, and collaboration characters.

![Character grid filtered to costumes with mods](./screenshots/v4.3.1-local.6/character-gallery.png)

<details>
<summary>Character list, costume details, portrait comparison, and skill animation</summary>

The list groups costumes by character and shows the status of cutscene, standing portrait, and dating mods. Group headings follow the character name setting.

![Costumes grouped under localized character names in list view](./screenshots/v4.3.1-local.6/character-list.png)

Open a costume to manage mods by type, preview one mod, compare mods of the same type, or open Skin Mapping.

![Costume details with mod tabs, enable switches, and comparison controls](./screenshots/v4.3.1-local.6/character-costume.png)

**Compare all** opens the selected mod type in ModPreview `v1.0.5-local.2`. Play standing portraits side by side and put previews in the same group to control them together.

![Two standing portrait mods playing side by side in ModPreview](./screenshots/v4.3.1-local.6/preview-compare.png)

Skill cutscene previews include a timeline, animated in-game backgrounds, and skill audio.

![Gray's skill cutscene with an audio status indicator and playback timeline](./screenshots/v4.3.1-local.6/preview-skill.png)

</details>

### Skin Mapping and Dating Interactions

Dating Interactions manages the official native entry and the extra P entry separately. Each character has independent switches, and the scene card shows the status of its local JSON file.

![Dating interaction controls for native and P entries and per-character scene status](./screenshots/v4.3.1-local.6/dating-interactions.png)

<details>
<summary>Costume skin mapping and per-character dating mods</summary>

Add target skins for the same character, select the current appearance, and enable or disable individual mappings.

![Source costume, target skin, and saved costume mappings](./screenshots/v4.3.1-local.6/costume-skin-mapping.png)

Open the mod list from a dating character card to enable, disable, or preview that character's dating mods.

![Dating mod details opened from a character's scene card](./screenshots/v4.3.1-local.6/dating-mods.png)

</details>

### Profiles and Mod Loader

<details>
<summary>Profiles, templates, and loader components</summary>

Save multiple mod setups, switch the active profile from the sidebar, and see which mods each profile enables.

![Profile list and the mods enabled in the selected profile](./screenshots/v4.3.1-local.6/profiles.png)

Use an existing profile as a template when you create a new one.

![New profile form with a name, description, and template](./screenshots/v4.3.1-local.6/profile-create.png)

The Mod Loader shows the versions and installation status of BepInEx, BrownDustX, and Configuration Manager. Drop a BepInEx or BrownDustX archive onto the page to install it.

![Mod Loader component versions, installation status, and drop area](./screenshots/v4.3.1-local.6/mod-loader.png)

</details>

### Application Settings

<details>
<summary>Directories, sync, themes, list display, dating controls, updates, and migration</summary>

General settings include language, theme, game and staging directories, the artwork library, and the sync method.

![General settings with game, staging, and artwork library directories](./screenshots/v4.3.1-local.6/settings-general.png)

Use the theme editor to customize accent colors, borders, text, mod types, and status colors.

![Theme editor with color categories and values](./screenshots/v4.3.1-local.6/theme-editor.png)

Choose which columns appear in the mod list and how character names, mod type icons, and colors are displayed.

![Settings for mod list columns and appearance](./screenshots/v4.3.1-local.6/settings-interface.png)

Adjust how long to hold the left mouse button before dragging and set the interaction marker opacity for the extra P entry.

![Dating interaction hold duration and interaction marker opacity](./screenshots/v4.3.1-local.6/settings-dating.png)

See the installed Manager and ModPreview versions. The Updates tab also provides manual checks, an update channel, and automatic update options.

![Manager and ModPreview version and update settings](./screenshots/v4.3.1-local.6/settings-updates.png)

Import profiles and mod authors from v3.2.4 or earlier under **Experimental Features**.

![Import options for legacy profiles and mod authors](./screenshots/v4.3.1-local.6/settings-migration.png)

</details>

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
