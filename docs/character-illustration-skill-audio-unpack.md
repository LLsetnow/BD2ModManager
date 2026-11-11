# Brown Dust II 角色立绘与技能音频解包

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
