# Brown Dust II 角色资源、技能音频与演出架构解包

本文记录从 Unity Addressables 缓存提取角色立绘、技能卡立绘、Spine 资源和角色技能音频的方法。结果基于本机当前游戏构建核对；Addressables 的 bundle hash 会随版本变化，每次更新都应先检查 `catalog.json`。

## 资源入口

游戏目录：

```text
F:\Neowiz\Browndust2\Browndust2_10000001\BrownDust II_Data
```

catalog 位于 `StreamingAssets\aa\catalog.json`。目标资源的 bundle 名称和内容 hash 可从 catalog 查找；下载到本机的 Addressables 缓存通常位于：

```text
Addressable\<cache-key>\<bundle-content-hash>\__data
```

当前构建中，第二层目录名与 bundle 名末尾的 hash 相同。不要把下表的 hash 固定用于其他版本；更新后应重新从 catalog 查找。

该 catalog 把 `m_KeyDataString` 编成 Base64。按 bundle 名片段搜索时先解码，再读到下一个 NUL；bundle 文件名里最后一个下划线后的 32 位十六进制串就是此构建的缓存内容 hash：

```python
import base64
import json
from pathlib import Path

catalog_path = Path(r"F:\Neowiz\Browndust2\Browndust2_10000001\BrownDust II_Data\StreamingAssets\aa\catalog.json")
catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
key_data = base64.b64decode(catalog["m_KeyDataString"])

def find_bundle_key(fragment: bytes) -> str:
    offset = key_data.find(fragment)
    if offset < 0:
        raise KeyError(fragment.decode("ascii"))
    end = key_data.find(b"\0", offset)
    return key_data[offset:end].decode("ascii")

key = find_bundle_key(b"character_skills.bytes_")
bundle_hash = key.rsplit("_", 1)[1].split(".bundle", 1)[0]
addressable_root = catalog_path.parents[2] / "Addressable"
candidates = list(addressable_root.resolve().glob(f"*/{bundle_hash}/__data"))
if len(candidates) != 1:
    raise RuntimeError(f"Expected one cache entry for {bundle_hash}, got {len(candidates)}")
print(key, candidates[0])
```

对其它 bundle，替换 `fragment`，例如 `b"common-illust_1_assets_all_"` 或 `b"common-illustskillcard_1_assets_all_"`。也可直接按已知 content hash 查找：`list(addressable_root.glob(f"*/{bundle_hash}/__data"))`。

| 资源 | catalog bundle | 当前缓存相对路径（不含游戏根目录） |
|---|---|---|
| 静态角色立绘 | `common-illust_1_assets_all_ba30456b0b17dd40fcf145d2fc213417.bundle` | `Addressable\7453dfa36c696c34b740c95e63b3fb52\ba30456b0b17dd40fcf145d2fc213417\__data` |
| 技能卡立绘 | `common-illustskillcard_1_assets_all_5654835c48bd7fc131e1a299e00f9b31.bundle` | `Addressable\4d4358296349a2fa26867045526c17c2\5654835c48bd7fc131e1a299e00f9b31\__data` |
| Spine 角色资源样本 | `common-illustspine-003701_assets_all_f8cc2999da51730a2d2eb22441c0b708.bundle` | `Addressable\349017d3a198df5e7016d66cfe69b5c1\f8cc2999da51730a2d2eb22441c0b708\__data` |
| 角色技能音频主包 | `common-sound_assets_sound/character_skills.bytes_0cdd032c2630bcee03f7519805cbab6f.bundle` | `Addressable\62c457e2814172fbae17623ad157a5ab\0cdd032c2630bcee03f7519805cbab6f\__data` |
| 角色技能音频组 00 | `common-sound_assets_sound/character_skills_group00.bytes_9a4b813b6c6f52a46dd49522d1988e67.bundle` | `Addressable\de45731b18c4fee7288d0663580929e0\9a4b813b6c6f52a46dd49522d1988e67\__data` |
| 角色技能音频组 01 | `common-sound_assets_sound/character_skills_group01.bytes_ecef306c741d873ee686fb1cb84c10fd.bundle` | `Addressable\8d2cfbe86e74ef2b3ecf190e5d996b9c\ecef306c741d873ee686fb1cb84c10fd\__data` |

## 提取 PNG 立绘

立绘 bundle 内是 Unity `Texture2D` 对象。用 UnityPy 读取对象后，通过 `Texture2D.image.save()` 导出 PNG：

```python
from pathlib import Path
import UnityPy

UnityPy.config.FALLBACK_UNITY_VERSION = "2022.3.62f2"

def export_texture(bundle_path: Path, texture_name: str, output_path: Path):
    env = UnityPy.load(str(bundle_path))
    for obj in env.objects:
        if obj.type.name != "Texture2D":
            continue
        texture = obj.read()
        if texture.m_Name == texture_name:
            output_path.parent.mkdir(parents=True, exist_ok=True)
            texture.image.save(output_path)
            return
    raise KeyError(f"Texture2D not found: {texture_name}")
```

本次核对样本：

- `common-illust_1` 中的 `illust_char000701_20`，导出为 2048×2048 PNG。
- `common-illustskillcard_1` 中的 `illust_skill_char000701_20`，导出为 256×256 PNG。

两张导出图与开源资源库 `ui\illust\illust_char`、`ui\illust\illust_skill_char` 中对应文件的像素内容一致。对应源文件：

- `F:\Neowiz\Brown-Dust-2-Asset-master\ui\illust\illust_char\illust_char000701_20.png`
- `F:\Neowiz\Brown-Dust-2-Asset-master\ui\illust\illust_skill_char\illust_skill_char000701_20.png`

### 提取 Spine 文件

Spine bundle 中 `.skel` 和 `.atlas` 是 `TextAsset`，贴图是 `Texture2D`。导出时 `.skel`/`.atlas` 应按字节保存，不能当普通 UTF-8 文本重写。当前 `char003701` 样本的 `.skel`、`.atlas` 与 `spine\char\char003701` 下的开源文件逐字节一致，贴图像素一致。

## 从 Addressables 声音包导出 FEV/FSB5

`character_skills*.bytes` bundle 各含一个 `TextAsset`，分别名为 `Character_Skills`、`Character_Skills_Group00`、`Character_Skills_Group01`。这些对象的载荷是 RIFF/FEV 音频库，内部各含一个 FSB5 声音库。游戏的 `audio\dating` OGG 索引不包含这些技能声音；技能声音应从游戏 Addressables 缓存提取。

二进制声音数据保存在 `TextAsset.m_Script`。UnityPy 会把它读为带 surrogate escape 的字符串，因此用 `encode("utf-8", "surrogateescape")` 可还原原始字节。找到 `FSB5` 头后，读取小端头字段并按头部长度切片：

```python
from pathlib import Path
import struct
import UnityPy

UnityPy.config.FALLBACK_UNITY_VERSION = "2022.3.62f2"

def textasset_bytes(bundle_path: Path, asset_name: str) -> bytes:
    env = UnityPy.load(str(bundle_path))
    for obj in env.objects:
        if obj.type.name != "TextAsset":
            continue
        asset = obj.read()
        if asset.m_Name != asset_name:
            continue
        payload = asset.m_Script
        return (payload.encode("utf-8", "surrogateescape")
                if isinstance(payload, str) else bytes(payload))
    raise KeyError(f"TextAsset not found: {asset_name}")

def extract_fsb5(fev_bytes: bytes, output_path: Path) -> int:
    offset = fev_bytes.find(b"FSB5")
    if offset < 0:
        raise ValueError("FSB5 header not found")

    # FSB5 header after the 4-byte signature:
    # version, sample count, sample-header bytes, name-table bytes,
    # sample-data bytes, mode, reserved, reserved.
    (version, sample_count, header_bytes, name_bytes, data_bytes,
     mode, _reserved1, _reserved2) = struct.unpack_from("<8I", fev_bytes, offset + 4)
    bank_size = 60 + header_bytes + name_bytes + data_bytes
    end = offset + bank_size
    if end > len(fev_bytes):
        raise ValueError("FSB5 size exceeds TextAsset payload")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_bytes(fev_bytes[offset:end])
    return sample_count
```

本次构建核对值：

| TextAsset | FSB5 在 FEV 中的偏移 | 子流数 | FSB5 模式 |
|---|---:|---:|---:|
| `Character_Skills` | 603,904 | 579 | 16 |
| `Character_Skills_Group00` | 87,232 | 90 | 16 |
| `Character_Skills_Group01` | 85,888 | 87 | 16 |

FSB5 头中的 `sample count` 用于核对预期数量。名称表开头有 `sample count` 个小端 `uint32` 偏移，每个偏移指向一个以 NUL 结尾的内部音频名；例如 `Char000701_Skill_Attack_01`。这也可用来生成 manifest 或核查解码输出名称。

## FSB5 转 WAV

本次已在以下位置安装 vgmstream 命令行程序：

```text
F:\Neowiz\tools\vgmstream-win64\vgmstream-cli.exe
F:\Neowiz\tools\vgmstream-linux\vgmstream-cli
```

Windows ZIP 内的 DLL 应与 `vgmstream-cli.exe` 放在同一目录。实际解码使用 WSL/Linux 静态构建。官方 CLI 可用 `-S 0` 导出所有子流；输出模板中的 `?03s` 是三位子流序号，`?n` 是 FSB 内部名称。[官方 CLI 子流说明](https://github.com/vgmstream/vgmstream/blob/master/doc/USAGE.md)。本次构建下载自 vgmstream 项目的 [Linux Actions 构建](https://github.com/vgmstream/vgmstream/actions/runs/42895735845) 和 [Windows Actions 构建](https://github.com/vgmstream/vgmstream/actions/runs/42895735843)。

每个 FSB 单独转换并加上包名前缀，避免不同组的名称相同时覆盖：

```sh
/mnt/f/Neowiz/tools/vgmstream-linux/vgmstream-cli -S 0 -o "Character_Skills_?03s_?n.wav" Character_Skills.fsb
/mnt/f/Neowiz/tools/vgmstream-linux/vgmstream-cli -S 0 -o "Character_Skills_Group00_?03s_?n.wav" Character_Skills_Group00.fsb
/mnt/f/Neowiz/tools/vgmstream-linux/vgmstream-cli -S 0 -o "Character_Skills_Group01_?03s_?n.wav" Character_Skills_Group01.fsb
```

本次输出位于 `F:\Neowiz\Character_Skills_WAV`，共 756 个 PCM WAV：主包 579 个、Group00 90 个、Group01 87 个，合计约 396 MiB。全部文件均可由 WAV 解析器读取。样本 `Char000701_Skill_Attack_01` 解码为 48 kHz、16 位、单声道 WAV；其余音频按 FSB 中的声道和采样率写出。

## 快速核验

```sh
vgmstream-cli -m Character_Skills.fsb
ffprobe -v error -show_entries format=duration -show_entries stream=codec_name,sample_rate,channels,bits_per_sample -of default=nw=1 sample.wav
```

`vgmstream-cli -m` 应显示 FSB 子流总数及当前流名；`ffprobe` 应识别导出文件为 PCM WAV。系统 FFmpeg 的 FSB demuxer 在本次环境中返回 `Not yet implemented`，用于 WAV 校验可以，用来替代 vgmstream 读取此 FSB5 则不行。


---

## 技能演出时间轴与游戏音频架构

本节记录 2026-11-17 本机游戏缓存的核对结果。前文提到的 756 个技能 FSB 子流是可复用的原始声音素材；游戏实际播放的“技能演出声音”由 Unity Timeline 触发 FMOD 事件，有些事件会混合多个子流、延迟、音量和效果。要还原演出，应保留 Timeline 与 FMOD 的关联。

### 资源分层

音频相关资源分为三层：

1. **Unity Addressables 目录与缓存**  
   当前安装目录为 F:\Neowiz\Browndust2\Browndust2_10000001\Brown Dust II_Data。游戏启动后使用的 Addressables catalog 还可见于 %USERPROFILE%\AppData\LocalLow\Gamfs\BrownDust II\com.unity.addressables\catalog_alpha.json。catalog 将逻辑 bundle ID、m_BundleName、内容 hash 和缓存文件关联起来；缓存数据位于 Addressable\<m_BundleName>\<m_Hash>\__data。Bundle 通常是压缩 UnityFS，应先解包，再用 UnityPy 查看对象。游戏更新后目录名和 hash 会变化，不能把本文的 hash 当成永久地址。

2. **角色技能源声音（FSB5）**  
   common-sound_assets_sound/character_skills.bytes、character_skills_group00.bytes 和 character_skills_group01.bytes 三个 TextAsset 各包含一个 FSB5。FSB 名称表中的 Char<资源编号>_Skill_* 是基础素材名。三个 FSB 合计 756 个子流。可用 vgmstream 按 FSB 子流编号单独解码 WAV。一个素材可能被多个 FMOD 事件使用；素材名中的 CutScene 也不保证其本身就是游戏听到的完整演出混音。

3. **FMOD 事件与语言语音**  
   游戏安装目录的 Brown Dust II_Data\Plugins\x86_64\fmodstudio.dll 是 FMOD Studio 2.03.12。SoundMaster 和 Sound 银行包含事件描述及声音资源；日语角色银行可在 SoundData\Patched\LocalVoice_JP 找到，韩语 local_common_*.bytes.bundle 位于本地化 Addressables 缓存。Voice_Common 的事件路径经常是语言无关的，实际取哪种语言由已加载的语言银行和 programmer sound 回调决定。

### 从 Timeline 找到应播放的声音

角色技能演出不是一个孤立的 Spine 动画。Addressables 中的 isolated-cutscene<编号>-group_assets_all_*.bundle 通常同时包含演出用 Spine 资源、Unity Timeline、PlayableDirector 和视觉对象。以 000708 为例，Timeline 中可见 Char000708_Set_Skill 与 Char000708 两个 PlayableAsset；后者包含 Spine 动画状态轨道及 FMOD Event Track。

解析步骤如下：

1. 从当前 catalog 找到 isolated-cutscene<编号>-group_assets_all bundle，并读取 UnityFS。
2. 遍历 UnityPy 对象，找到 PlayableDirector，沿 m_PlayableAsset 读取 Timeline。
3. 递归遍历 Timeline 的 m_Tracks 和子轨道 m_Children；从音频轨道的 m_Clips 读取 m_Start、m_Duration、m_ClipIn、m_TimeScale、音量、淡入淡出及引用的 Playable 对象。
4. 从 FMODEventPlayable 或 FMODVoiceEventPlayable 读取 EventReference.Guid、EventLength、StopType、参数与类型。用游戏版本匹配的 FMOD Studio API 按 GUID 调用 FMOD_Studio_System_GetEventByID，再用 EventDescription 的路径查询接口得到 event:/... 路径。
5. 对语音事件注册 FMOD_STUDIO_EVENT_CALLBACK_CREATE_PROGRAMMER_SOUND，从回调取得真正被选择的 FSB 子流名。最终映射关系是“Timeline clip → FMOD GUID / event path → 语言银行中的 programmer sample”，不能只按人物编号或 clip 显示名猜测。

m_Start 是声音事件相对于 Timeline 的触发时刻；m_ClipIn 和 m_TimeScale 会改变源音频在片段中的位置或播放速率。Timeline 片段时长、FMOD 的 EventLength 和解码后 WAV 时长含义不同，应分别保留。实现预览时还应处理片段音量、并行轨道和 StopType。

### 提取语音 WAV

FSB5 样本名称表可用于精确选出上一步回调得到的 programmer sample。按样本在 FSB 内的序号调用 vgmstream，输出到按语言和角色资源编号分层的目录，例如：

    F:\Neowiz\Brown-Dust-2-Asset-master\audio\skills\ja-JP\000708\Char000708_BattleSkill_3.wav
    F:\Neowiz\Brown-Dust-2-Asset-master\audio\skills\ko-KR\000708\Char000708_BattleSkill_3.wav

原始日语银行可直接查找 FSB5。韩语 Addressables bundle 需要先解压 UnityFS，再在其 TextAsset 数据中查找 FSB5。对每个目标 sample 记录语言、银行源路径、银行 SHA-256、FSB 序号、内部名称及导出 WAV 的 SHA-256；这样 catalog 更新后能重新校验来源，而不用依赖缓存目录名。

示例命令（-s 使用从 1 开始的 FSB 子流序号）：

    F:\Neowiz\tools\vgmstream-win64\vgmstream-cli.exe -i -s 17 -o sample.wav voice-bank.fsb

解码后检查 WAV 头、PCM 格式、采样率、声道数、帧数和非静音峰值。vgmstream-cli -m voice-bank.fsb 可核对 FSB 子流名称及总数。

本机缓存核对到 358 个不同的技能演出语音样本名：日语银行找到 357 个，韩语银行找到 307 个。缺少的银行样本仅表示本次扫描的本机缓存没有找到，不能据此断言游戏从未发布该语音；更新 catalog 或下载新的语言 bundle 后应重新扫描。

### 提取完整 FMOD 技能音效

对 Timeline 引用的 SFX，优先渲染 FMOD Event，而不是把同名 FSB 子流直接当作最终结果。离线渲染使用与游戏匹配的 fmodstudio.dll：

1. 创建 Studio system 时指定与 DLL 兼容的头文件版本；本机 DLL 使用 FMOD_STUDIO_INIT_VERSION = 0x20300。
2. 设置 Core 输出为 WAVWRITER_NRT，并固定输出采样率、声道布局和 DSP buffer 大小。
3. 加载当前构建的 SoundMaster、Sound 与需要的语言银行。按 Timeline GUID 取得事件，先加载 sample data，再启动 event instance。
4. 调用 FMOD_Studio_System_Update 推进无实时设备的渲染；使用 master channel group 的 DSP clock 记录起止帧。
5. 从离线生成的 PCM WAV 按帧切出事件音频，同时记录 event path、Timeline GUID、源 bank hash、音量、帧数和 WAV hash。

本机按 Timeline GUID 唯一事件核对到 173 个 SFX 事件，事件均能在本机 FMOD 银行中解析，离线渲染后的 PCM WAV 均非静音。这个数字是本次本地资源快照，不是今后游戏版本的固定总数。

### 与 ModPreview 的时间控制同步

ModPreview 现有立绘类预览加载单个 Spine skeleton；“游戏过程动画预览”则通过 useGameCutscene 读取 src/data/cutscenes/<编号>.json，重建 Timeline 里的骨骼轨道和画面图层。Scene 的动画时间由 useGameCutscene.update(delta, loop) 推进，seek(value) 重置并重放动画轨道到目标时间。当前 Scene schema 有 nodes、tracks、curves、activations、assets、textures 和可选 baked 图层，尚未内嵌 FMOD 音频轨道。

接入声音时，音频事件应沿用同一 Scene 时间轴：开始演出时从 0 建立调度；运行时按 Timeline clip 时间触发；暂停时暂停或冻结调度；拖动进度时停止旧事件并根据目标时间恢复；重播、切换资源、停止预览和组件销毁时清理所有 AudioBufferSource 或 scheduled node。循环回到 0 时重新调度。拖动应重建当前时间点仍有效的事件并跳过已经结束的事件，避免把被跳过的台词和音效补播出来。

声音清单至少要保存：

- Scene / cutscene ID、源 bundle hash 和 Timeline revision；
- clip 起点、片段时长、clipIn、timeScale、音量与停止规则；
- FMOD GUID、事件路径、SFX/语音类型；
- SFX WAV 路径，或每种语言的语音 sample 路径；
- WAV 时长、采样格式和 hash。

语言语音使用 ja-JP / ko-KR 映射。若当前选择语言没有对应文件，可按已存在的语音设置回退到另一种语言，并显示实际播放的语言。某个声音文件缺失不应阻止画面时间轴运行。

### 本地分析工具与更新注意事项

本机常用工具：

- F:\Neowiz\tools\unity_voice_bundles.mjs：解压 UnityFS LZ4 bundle，解析 Addressables cache 元数据。
- F:\Neowiz\tools\extract_standing_audio.mjs：包含 FSB5 名称表、子流与 PCM WAV 检查函数。
- F:\Neowiz\tools\vgmstream-win64\vgmstream-cli.exe：从 FSB5 导出单个 sample。
- F:\Neowiz\tools\extract_skill_timelines.py：扫描已缓存的 isolated-cutscene Timeline。
- F:\Neowiz\tools\extract_skill_voices.mjs：按事件回调得到的样本名提取两种语言语音。
- F:\Neowiz\tools\render_skill_events.py：调用游戏 FMOD DLL 离线渲染完整 SFX event。
- 游戏 managed assemblies 位于 Brown Dust II_Data\Managed，可用来核对 FMOD Unity Timeline playable 字段和 StopType 枚举。

更新游戏后，应从最新 catalog 重建 bundle 映射，确认 bundle hash、GUID、bank 版本和资源是否齐全；再对至少一个已知角色比较 Timeline 时间戳、事件路径、渲染 WAV 时长和播放片段边界。不要用旧版本 GUID 或缓存 hash 默默回退到另一构建的音频。
