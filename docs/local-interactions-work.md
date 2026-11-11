# Local interaction integration — 2026-11-11

## Current implementation

- Interaction plugin 0.2.8 and costume skin plugin 0.1.1 are integrated into ModManager. Separate master switches preserve individual settings; older settings default to enabled.
- Both object lists use responsive card grids. Interaction entries show costume portraits. The skin mapping form sits directly above its existing mapping grid, after master/plugin controls.
- The local profile has all 22 dating entries: the original six keep their settings and the other 16 start disabled. Future manual additions also start disabled. Local profiles, exported scenes, audio and game binaries are outside this repository.
- Skin edits save automatically through serialized per-operation updates against the latest game settings. This preserves other selections made by the game; failed changes are validated before writing and the manager resynchronizes saved state.
- The following sections record implementation and verification history.

## Authorized requirements

1. Fill all authorized dating JSON exports in `%LOCALAPPDATA%/com.bruhnn.BD2ModPreview/game-interaction-export`, without adding game entries for all of them.
2. Right mouse button alone pans. Left interaction dragging requires a hold threshold; suppress duplicate down/up events and accidental repeated charge/click actions.
3. Native-looking vertical affection gauge on the left and native tool icons.
4. Remove custom reset-view button. Intercept native Reset to restore pose 1, its start gauge, default tool, hidden/input/audio states. No hint mode implementation.
5. Remove black loading frames and main-thread stalls by staging visuals, background parsing/verification, and lazy audio loading.
6. Translucent Preview-style point markers, filtered by the active pose/tool/hidden/gauge state.
7. Manage local entries in ModManager and include plugin source/build/installation in this repository.
8. Fix invalid thumbnail requests and investigate crash.
9. Preview exports only the selected JSON with a user-selectable output path, preserving expiry metadata.
10. Respect distinct pose stages.
11. One physical tap produces one logical tap; charge success/fail and drag release each execute once.

## Established facts

- Plugin source moved from `F:/Neowiz/BD2PreviewInteractions` into `plugins/BD2PreviewInteractions`; old directory is now only a pointer README.
- Installed version is now 0.2.0; 0.1.6 and the previous Preview executable were backed up to `F:/Neowiz/mod_backups/BD2PreviewInteractions-0.1.6-20261111`. The game is exited.
- Existing six entries: dating 7, 9, 12, 18, 19, 21. Aquila 22 stays removed from the list, but its JSON must be present with all other authorized JSON.
- Full authorized scene cache can be decoded locally using the read-only LevelDB helpers in `/tmp/export_bd2_cached.py`; do not run that script wholesale because its old tail exports 18/22 plus audio. Reuse only the prefix before `cache = None`.
- Audio directory: `F:/Neowiz/Brown-Dust-2-Asset-master/audio/dating`.
- Unity game: `F:/Neowiz/Browndust2/Browndust2_10000001`.
- Build plugin with Windows dotnet, the project `.build-tools/csc.cmd` bypasses an incompatible compiler apphost by running csc.dll through dotnet (no OS policy changes).
- Windows node + corepack: `E:/Program Files/nodejs/node.exe`, `corepack.cmd`; WSL networking is unavailable, Windows networking works.
- Windows computer-use node_repl fails before initialization (`sandboxCwd is not a local file URI`), so use logs/static tests and user runtime feedback.

## Confirmed defects and implementation direction

- Synthetic rows overwrite `IconSpriteName` with incompatible `illust_loop_char...` names. Game reports Invalid TextureName and invalid Addressables keys. Preserve original cloned row data and bypass native local-cell thumbnail loading; populate local cell selection/id/avatar directly.
- Player.log is ~528 MB / 20 million lines of `Bone not found`. Spine `BoneFollower`, `BoneFollowerGraphic`, and `SkeletonUtilityBone` emit this text. Disable non-rendering prefab scripts for local scenes, not just `SpineInteractionPoint`; their updates also interfere with pose control.
- Current Open hides the previous visuals, synchronously verifies all audio, initializes/mod-parses active prefabs, and decodes every audio clip sequentially. Keep old scene visible while staging an inactive instance; parse pure Spine data and verify files off the main thread; lazy-load requested sound events.
- Input currently lets any non-left button pan and lets empty-space left pan. No pointer owner/dedup/hold threshold. Use a pure input state gate with meaningful standalone tests.
- Current click handling starts next pose synchronously before applying old-point scores/reveal, unlike Preview's async order. Apply old-stage effects first, then transition, and reset all input generation at stage boundaries.
- Existing native gauge component: `SpineInteractionGaugeController._gaugeUI` -> `SpineInteractionUI_Gauge`. Fields `_percentText` (TMP), `_gaugeImage` (Image), `_heratObject`, animation/effect objects. Clone visual root only, disable callbacks/controller, update visuals locally.
- Tool prefab: `SpineInteractionUI._orinInteractionToolbarItem` (`InteractionToolbarItem`), public `SetItem(int)`, `SetSelected(bool)`, `SetActiveUI_HintObject(bool)`. Uses official `SpineInteractionToolbarTable` and `UISprite.SetSprite` for exact tool icons. Clone per current tool and intercept native tool click for local controller.
- Native reset target is `SpineInteractionUI._resetButton`; intercept OnClickUI when local scene active. Hint controller has `_hintSettingButton` and public HideUI_HintSettingButton; hide it for local mode and restore official UI afterward.

## Verification / progress

- [x] All JSON exported (no added game entries)
- [x] Input gate + click/charge/pose tests
- [x] Safe synthetic cells / native reset
- [x] Inactive staged visuals / script isolation / background parsing / lazy audio
- [x] Native gauge/tools + translucent markers
- [x] Dynamic settings/catalog shared with Manager
- [x] Manager page/backend/build/install
- [x] Selectable JSON export path in Preview
- [ ] Builds, installation, startup logs, user runtime pass

### Verification completed

- Plugin 0.2.0 release build: 0 errors, 0 warnings.
- Pure C# gesture regression: 18 checks passed (duplicate press/release, ownership, left hold threshold, right-only pan ownership, charge completion once, pose/reset cancellation).
- Manager release build, Vue/TypeScript check, and Vite production build passed.
- Manager Rust tests: 2 passed (22-item catalog, exact six default entries without dating22, duplicate/invalid entry rejection).
- Preview release build and Vue/TypeScript check passed; Rust export-ID validation test passed.
- 22 local export envelopes verified; only six enabled game entries persisted.
- No Windows Application Error / WER entry for BrownDust II found in the last two days. Player.log ends with normal Unity shutdown; the actual reported crash cause remains unconfirmed.
- Native UI layout, pose/hold behavior and timing still require an in-game runtime pass.
- Current elevated Manager process (debug executable) rejects Stop-Process; user was asked to exit before starting the new release with copied existing config.

### Installed / startup

- Installed final plugin DLL matches the Manager resource and plugin release output. Updated Manager's installed Preview tool and bundled resource with the JSON-only export build.
- Preserved original debug Manager config in the new release folder. Desktop Manager shortcut now targets the new release; previous shortcut is backed up.
- Game started and BepInEx reports `Loading [BD2 Preview Interactions 0.2.0]`, capture hook installed, avatar entries ready.
- Startup Player.log is ~7 KB with no missing-bone flood or invalid thumbnail key; this is startup coverage only, before entering a P scene.
- Archived previous ~528 MB Player.log as `Player-before-0.2.0.log.gz` in the backup folder (~9.5 MB compressed).
- Manager restart still waits for the user to exit the existing elevated debug process. Runtime scene acceptance (HUD location, reset, drag/hold, pose gating, audio cleanup and loading timing) remains outstanding.

### Manager restart confirmed

- After user exit, the existing cargo development runner launched the newly compiled debug Manager (PID 36028, 2026-11-11 16:53 CST). Launching the release focused this instance through single-instance handling.
- Confirmed the running executable path and verified that its binary contains both the new `get_local_interactions` command and the exact current plugin DLL bytes. This is the updated Manager, not the previous build.
- Manager window responds normally and loads the existing debug config, Default profile, game path and staging path. Existing update endpoint check reports an HTTP error; it does not prevent startup.
- Native scene acceptance still awaits user interaction; do not report loading latency, input accuracy or crash resolution as runtime-verified from startup alone.

### 0.2.1: shared scene-entry regression

- All attempted local scenes failed at `SkeletonGraphic.Initialize` with `AnimationState` throwing `ArgumentNullException: data cannot be null`.
- Background loader assigned only private `skeletonData`, bypassing Spine's paired `stateData` initialization. `GetAnimationStateData()` then called `GetSkeletonData`, which returned the cached skeleton without creating the missing animation state.
- Replaced direct field assignment with Spine's own `InitializeWithData(SkeletonData)` initializer (private in this game build, invoked via cached MethodInfo). This creates both caches and applies default/per-animation mix settings.
- Seven checks passed against the actual game Spine DLLs: animation cache present, shared SkeletonData identity, default and explicit mixes preserved, first pose starts, and rebinding replaces stale state.
- Existing startup and pure input tests did not cover this Unity/Spine asset lifecycle; in-game acceptance remains required after installing 0.2.1.

- 0.2.1 plugin and Manager release build passed; updated embedded DLL matches the installed game DLL (SHA256 dce88618e77a1a3542ce8f680f5953cda5e8aa5627b67b788774346d9f788368).
- Game restarted and BepInEx confirmed `Loading [BD2 Preview Interactions 0.2.1]`, avatar hooks ready, no startup plugin exception. Actual P-scene entry still awaits user test.

### 0.2.2: local HUD visibility

- User confirmed normal entry on 0.2.1. Logs show all six local scenes opening successfully with their enabled mods and no local-scene exceptions.
- Removed the two permanent phase/operation GUI labels requested in screenshots. Retained the point visibility toggle.
- Local HUD now has an explicit nested UI Canvas and GraphicRaycaster, sorted above character canvases and using the game's UI camera/layer. Dynamic marker, avatar, badge and click surfaces inherit UI layers.
- Native gauge/tool clones restore visual alpha and enabled graphics, activate the icon/gauge paths, and retain UISprite visual loading. Disabled gameplay scripts remain disabled.
- Single-tool groups now display their hand/tool icon. Tool selection and gauge availability continue to follow the current pose's JSON; of the default six scenes, dating19 has the configured affection gauge.
- Added one-time canvas/layer/tool/gauge diagnostics and marker-count logging when points are shown.
- Plugin and Manager release build passed, installed 0.2.2. UI visibility and placement still require runtime verification; do not infer visual success from compilation alone.

### 0.2.3: duplicate gauge and Reset-relative controls

- User screenshots show the native frozen 50% gauge above the working local gauge; the native gauge also remains visible in non-gauge local scenes.
- Explicitly save/hide the native gauge GameObject as well as disabling its controller. Keep all saved native controls hidden during the local scene; restore their prior states on leaving it. The cloned local gauge still follows the active group's IsOn flag.
- Position the local tool column using the native Reset button's actual screen bounds. Put the point visibility button directly beneath the tool column. Each frame maps screen coordinates back through the HUD camera, preserving alignment across resizing and UI scaling.
- Local control hit testing uses the same computed bounds; removed the obsolete fixed right-edge exclusion region.
- Plugin 0.2.3 and Manager release build passed (plugin: 0 warnings/errors; Vue/TypeScript and production build passed). Installed synchronized plugin; runtime layout acceptance remains pending.

### 0.2.4: visible Reset and gauge at avatar/list

- User confirmed 0.2.3 duplicate-gauge removal and tool placement, then reported the native Reset had disappeared and requested moving the effective gauge upward to the list area.
- Local HUD now displays the original Reset artwork as an isolated local control, with its own click surface calling the guarded local pose/gauge reset. The original game Reset is saved/hidden/restored along with other native controls; any Reset inside the gauge visual copy is suppressed to avoid duplicates.
- Gauge artwork now matches the original native gauge's screen position and scale below the avatar/list rather than the previous middle-left offset. Position is updated with screen/UI layout; only configured gauge stages show it.
- Reset, tools and show-points share the same projected screen layout and hit-test bounds.
- Plugin 0.2.4 and Manager release builds passed. Installed resource and game DLL match; runtime confirmation of Reset behavior and the requested gauge position is still pending.

### 0.2.5: Reset normal/hover appearance

- User screenshot shows the Reset hover glow permanently visible; expected ordinary state is the native icon and text without that glow.
- Root cause: Reset restoration activated every graphic path and raised transparent graphics to full alpha. Reset now preserves inactive layers, image colors/enabled state, runtime CanvasRenderer alpha, and nested group opacity instead of treating hover effects as always-visible art.
- Native Reset Selectables regain their visual transitions, with original click callbacks cleared. The local transparent click surface retains the guarded local reset action. Animation transitions enable their local UI animators; mouse navigation selection is disabled so clicking cannot pin a highlighted selection state.
- Added Unity AnimationModule reference and qualified Spine.AnimationState to avoid its Unity name collision. Plugin and Manager release builds passed; game DLL and embedded resource match.
- Installed 0.2.5 and restarting game. Visual normal/hover/after-click acceptance still requires user confirmation.

### 0.2.6: Reset click routing

- User confirmed 0.2.5 normal Reset appearance, but clicking did not reset the interaction.
- Reset action now uses a transparent IMGUI hit region at the same screen bounds used to place its visual. It calls guarded RequestReset directly, independently of native CanvasGroups/raycast filters or the emptied visual Button callbacks.
- Removed the competing nested local Button from the Reset visual. A non-Button, non-culled hover graphic remains solely for native hover/press effects; native Button callbacks remain empty so one physical click cannot invoke reset twice.
- Added `Local Reset applied` diagnostic after successful reset, including pose and gauge. Tool click surfaces and the accepted normal Reset appearance remain as before.
- Plugin and Manager release builds passed. Installed synchronized 0.2.6 DLL and restarting game; physical click acceptance still pending user test.

### 0.2.7: Granhildr pose-transition overlap

- User reports both poses visible while Granhildr transitions from pose1 to pose2. Preview clears reaction track 1 before the transition; the plugin's motion branch omitted this and allowed a completed touch reaction to keep applying the last pose1 attachment frame above the motion on track 0.
- New pure-Spine transition helper clears reaction/hold tracks 1 and 2, restores the current base pose, starts the motion without mixing, and applies it immediately. New group entry now also applies its animation/world transforms in the same frame to avoid drawing setup-pose attachments until the following SkeletonGraphic update.
- Added regression tests using the actual local dating19 mod JSON/atlas and the game's Spine DLL, without loading textures or copying assets into the repository. Reproduced 3,242 old-state differences, including 1,496 frames with a stale visible pose1 leg.
- Corrected playback passed 3,247 slot/alpha snapshots across 17 pose1 reactions against clean transition playback. Checks also cover no simultaneous pose1/pose2 leg attachments in the clean motion, a prior hold overlay, immediate pose2 entry, and return to pose1.
- Plugin and Manager release builds passed. Installed synchronized 0.2.7 DLL and restarting game; actual in-game transition appearance still awaits user verification.
