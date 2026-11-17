# 约会数据解包与原生入口插件实现

记录日期：2026-11-12。第 1—11 节记录已实测的 `BD2PreviewInteractions 0.3.0`；0.4.0 双入口管理见第 12 节，0.4.1 默认使用已启用模组的变更见第 13 节。

阅读顺序：

- 重现官方表解包：第 2—5 节，包含源文件位置、密钥派生、逐页解码、内存 SQLite 查询与 protobuf 解析示例。
- 核对缺失六项：第 6 节，包含真实入口 ID、InteractionId、服装及章节条件。
- 查阅全部约会 ID：第 14 节，完整列出 22 项原生 / P 入口、5 条未关联记录、名称文本与头像标识，并附全部官方解析字段。
- 理解原生入口验证：第 7—10 节，记录复制实验、六项正式注入及已完成的游戏内验证。
- 维护当前版本：第 12 节，说明全部 22 项的 P / 原生独立开关、列表恢复、设置页面、构建和安装步骤。实现当前行为时以本节为准。
- 每项素材选项：第 13 节记录移除「P 入口素材」后对旧配置的兼容处理。
- 在约会页面配置模组：第 15 节说明角色卡片的模组列表、启停、预览及同步流程。
- 共用角色配置窗口：第 16 节记录当前版本直接打开角色页同一窗口、默认选中约会标签的实现。
- pose 切换错误 154907：第 17 节记录原生触点奖励请求的原因与 0.4.2 的本地入口处理。

## 1. 已确认的结果与数据层次

本机游戏为 Unity `2022.3.62f2`，BepInEx `5.4.22.0`，游戏目录为 `F:\Neowiz\Browndust2\Browndust2_10000001`。本次通过本地设计数据库取得了六项完整官方记录，并在客户端约会列表补入真实行 ID。用户已确认六项均出现且能够进入原生互动。

需要区分四类编号：

| 编号 | 来源 | 用途 |
|---|---|---|
| `Illust_datingN` 的 N | 资源名 | 定位场景 prefab；不能直接充当表级 ID |
| `LobbySettingItemTable.Id` | 官方设计表 | 约会列表 `List<int>` 中保存的入口行 ID |
| `LobbySettingItemTable.InteractionId` | 官方设计表 | 关联 `SpineInteractionTable.Id`，用于原生互动配置 |
| 点的 `(group,id,tool)` | prefab 的组件字段 | 定位场景内触点；不是列表行或表级互动 ID |

日志最初捕获的 16 项来自传给 `SpineInteractionUI.SetUI(int,List<int>)` 的账号当前列表，并非完整官方设计表。

## 2. 本地数据位置

| 内容 | 本机位置 |
|---|---|
| 游戏程序集 | `F:\Neowiz\Browndust2\Browndust2_10000001\BrownDust II_Data\Managed\Assembly-CSharp.dll` |
| 游戏 protobuf 库 | 同一 Managed 目录中的 `Google.Protobuf.dll` |
| common 设计缓存 | `%USERPROFILE%\AppData\LocalLow\Gamfs\BrownDust II\Data\t\9F251C63BC72551C681EE75D328FA090D56E444B` |
| 六项完整提取结果 | `F:\Neowiz\releases\official-dating-six-20261112.json` |
| 全部 dating 表记录 | `F:\Neowiz\releases\official-dating-all-20261112.json`，27 条资源记录，其中 22 条有有效互动关联 |
| 0.3.0 DLL 与验证记录 | `F:\Neowiz\releases\BD2PreviewInteractions-0.3.0\` |
| 当前 Manager / 插件交付目录 | `F:\Neowiz\releases\BD2ModManager-4.3.1-local.6\` |

common 文件名是 `SHA1(UTF8("common_v1"))` 的大写十六进制表示。`StreamingAssets/DefaultDB.db` 是初始化数据库，本次所需完整设计记录位于 common 缓存。无需读取账号的 ClientLocal 数据库。

本次 common 文件 SHA256 为 `7cbb7275a9fc137592b01383113381f18d209903d34963bdd70c9ad0d5b666ba`；Assembly-CSharp SHA256 为 `35d67a3f7c420d8975a49a84ab08b35ff055e91ccaec20c797d9e5eb7c4afe50`。这些值用于识别本次构建，更新游戏后应重新核对。

## 3. 解码方式的定位

查看本机 Assembly-CSharp 的 IL，沿以下路径追踪：

1. `RawDataManager.GetLobbySettingItemTableList()` 查询 `LobbySettingItemTable`。
2. 数据库连接包装类设置解码参数，调用游戏内的托管 SQLite codec。
3. 当前混淆类 `ὩὦὩὨὥὤὫὪὠὨὭ` 的 `ὨὡὠὮὥὭὦὫὤὯὬ()` 产生口令字符串；`ὯὬὩὪὮὧὪὬὯὣὢ(byte[])` 使用 SHA1。
4. codec 的密钥派生处调用 `Rfc2898DeriveBytes`，迭代次数为 2010，派生 32 字节密钥；页解码使用 AES-CBC。

本构建参数如下。它们来自游戏内的资源数据库代码，不是账号凭据：

```text
seed              = UTF8("spdhdnlwmrpavmtm")
password          = UTF8(UPPER_HEX(SHA1(seed)))
salt / IV         = ASCII("SQLite format 3\0")，共 16 字节
key               = PBKDF2-HMAC-SHA1(password, salt, iterations=2010, length=32)
cipher            = AES-256-CBC
padding           = None
page size         = 4096 字节，以解码后的 SQLite 文件头核对
```

每页独立解码并重置 IV。连续对整个文件执行一次 CBC 会破坏后续页。不要对每页执行 PKCS7 去填充，也不要将加密文件最前面的 16 字节当作随机 salt。游戏更新后应重新沿代码确认参数，不能仅凭文件名复用。

## 4. 只读导出设计表 protobuf

以下 Python 示例在 WSL 运行，需要 `cryptography` 以及支持 `Connection.deserialize` 的 Python SQLite。只从源文件读取，解码后使用内存数据库，并把两张设计表的 protobuf BLOB 导出到仓库外。

```python
from pathlib import Path
import base64
import hashlib
import json
import sqlite3
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

data_root = Path("/mnt/c/Users/admin/AppData/LocalLow/Gamfs/BrownDust II/Data/t")
source = data_root / hashlib.sha1(b"common_v1").hexdigest().upper()
work = Path("/mnt/f/Neowiz/releases/dating-table-work")
work.mkdir(parents=True, exist_ok=True)

encrypted = source.read_bytes()
source_hash = hashlib.sha256(encrypted).hexdigest()
iv = b"SQLite format 3\0"
password = hashlib.sha1(b"spdhdnlwmrpavmtm").hexdigest().upper().encode("utf-8")
key = hashlib.pbkdf2_hmac("sha1", password, iv, 2010, 32)

def decrypt_page(page):
    decoder = Cipher(algorithms.AES(key), modes.CBC(iv)).decryptor()
    return decoder.update(page) + decoder.finalize()

header = decrypt_page(encrypted[:32])
assert header[:16] == iv, "Decoded SQLite header does not match"
page_size = int.from_bytes(header[16:18], "big")
assert page_size == 4096, "Recheck codec parameters for this game build"
assert len(encrypted) % page_size == 0
plain = b"".join(decrypt_page(encrypted[i:i + page_size])
                 for i in range(0, len(encrypted), page_size))

db = sqlite3.connect(":memory:")
db.deserialize(plain)
db.execute("PRAGMA query_only = ON")
assert db.execute("PRAGMA quick_check").fetchone()[0] == "ok"
result = {}
for name, table in [("lobby", "LobbySettingItemTable"),
                    ("interaction", "SpineInteractionTable")]:
    result[name] = [
        {"id": row_id, "protobuf": base64.b64encode(blob).decode("ascii")}
        for row_id, blob in db.execute(f'SELECT id, ProtoBuf FROM "{table}"')
    ]
db.close()
assert hashlib.sha256(source.read_bytes()).hexdigest() == source_hash
(work / "common-table-blobs.json").write_text(
    json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print("Source SHA256:", source_hash)
print("Rows:", {name: len(rows) for name, rows in result.items()})
```

本次两张表的 SQL 结构与数量：

```sql
CREATE TABLE LobbySettingItemTable
  ("id" INTEGER, "type" INTEGER, "itemPath" TEXT, "ProtoBuf" BLOB,
   PRIMARY KEY("id"));
-- 563 行

CREATE TABLE SpineInteractionTable
  ("id" INTEGER, "ProtoBuf" BLOB, PRIMARY KEY("id"));
-- 22 行
```

## 5. 使用游戏自己的类型解析

在独立 .NET 控制台程序中引用同一游戏构建的 `Assembly-CSharp.dll` 和 `Google.Protobuf.dll`。本机辅助程序使用 .NET 10；插件本身的目标框架仍是 .NET Standard 2.1。

核心解析方式：

```csharp
using Google.Protobuf;
using Proto.Design.common;

var lobby = LobbySettingItemTable.Parser.ParseFrom(lobbyBlobBytes);
var interaction = SpineInteractionTable.Parser.ParseFrom(interactionBlobBytes);
var formatter = new JsonFormatter(new JsonFormatter.Settings(true));
Console.WriteLine(formatter.Format(lobby));
Console.WriteLine(formatter.Format(interaction));
```

`Settings(true)` 输出默认值，避免把省略的 0 或空列表误判为缺失字段。应核对 SQL 的 id 与消息 `Id` 一致，再按 `lobby.InteractionId == interaction.Id` 关联。重新序列化消息可能规范化字段顺序；语义校验应比较字段和值，如需逐字节留存则直接保留 SQL BLOB。

遍历全部 Lobby 行并筛选 `InteractionResourceName` 后，本次得到 27 条 dating 资源记录。其中 22 条具有有效 Interaction 关联，另有 5 条的 `InteractionId=0`。本次解析结果与账号日志中的 16 项行 ID、服装和资源名全部一致，也与复制实验捕获的源行 32 的完整已输出字段一致。

## 6. 六项真实记录

| 场景 | Lobby ID | InteractionId | CostumeId | PrestigeSkinId | 解锁组 / 章节 ID | 同资源、无互动关联的行 |
|---|---:|---:|---:|---:|---|---|
| dating7 | 77 | 1000296 | 296 | 296 | 0 / 0 | 218 |
| dating9 | 99 | 1001197 | 1197 | 1197 | 0 / 0 | 217 |
| dating12 | 121 | 1061492 | 61402 | 61492 | 0 / 0 | 161 |
| dating18 | 207 | 1000396 | 396 | 396 | 0 / 0 | 208 |
| dating19 | 225 | 14 | 67104 | 0 | 14 / 10 | 无 |
| dating21 | 274 | 1065193 | 65193 | 65193 | 0 / 0 | 275 |

dating12 的 `CostumeId` 必须读官方值 61402；本地 P 目录此前使用的 61492 是该记录的 `PrestigeSkinId`。选择记录时应检查完整关联，避免拿到只共用资源名的无互动行。

例如 dating7 的主要字段为：

```json
{
  "id": 77,
  "type": 2,
  "costumeId": 296,
  "prestigeSkinId": 296,
  "interactionId": 1000296,
  "interactionResourceName": "Illust_dating7",
  "iconSpriteName": ["thumb_specialIllust_77"],
  "itemNameTextId": 6000077,
  "itemPath": ["SpecialIllust77"]
}
```

全部 22 项有效入口、5 条未关联记录及 P 对象的 ID 对照见第 14 节；完整音频银行、BGM、环境音及默认字段也已收录在第 14.6 节的解析 JSON 中。运行插件不依赖这份导出文件；它是定位和校验记录的证据。

## 7. 先验证复制原生条目：0.2.9

实验代码为 [NativeEntryProbe.cs](../plugins/BD2PreviewInteractions/NativeEntryProbe.cs)。复制当前官方列表中的行 32（dating1），调用 protobuf `Clone()`，只把 `Id` 改为 `1900100001`，保留 `InteractionId=1` 及其他字段。通过行查询前缀返回该临时记录，并把临时 ID 插入列表。

关键是让副本保持在 P 目录 `Specs` 之外：原生头像绘制与 `SetCharSelect` 正常执行，本地控制器不接管。日志确认原生 `SetSpineInteraction` 返回，3 个阶段、68 个触点；用户确认副本出现并能进入。这验证了列表注入与原生加载链路。

实验行只存在于插件内存，未写入官方设计数据库。正式 0.3.0 安装已关闭实验开关。

## 8. 正式六项注入：0.3.0

代码入口：

| 文件 | 职责 |
|---|---|
| [Plugin.cs](../plugins/BD2PreviewInteractions/Plugin.cs) | 安装 Harmony 钩子、读取配置、重建列表、路由和诊断 |
| [NativeDatingEntries.cs](../plugins/BD2PreviewInteractions/NativeDatingEntries.cs) | 六项绑定校验、原生入口集合、添加与去重 |
| [LocalInteractionSettings.cs](../plugins/BD2PreviewInteractions/LocalInteractionSettings.cs) | 读取 Manager 维护的总开关及场景设置 |
| [interactions.rs](../src-tauri/src/interactions.rs) | Manager 目录、配置写入、插件资源安装及版本信息 |

正式实现使用游戏已有的真实官方行 ID。原始 Lobby 和 Interaction 对象只读，不再复制或改写它们的字段。

### 8.1 取得完整表

当前游戏混淆了部分属性名，但保留对应 `get_*` 方法名。通过反射取得 RawDataManager 单例，再读取完整设计表：

```csharp
var manager = AccessTools.Method(typeof(RawDataManager).BaseType, "get_Instance")
    ?.Invoke(null, null) as RawDataManager;
var rows = manager?.GetLobbySettingItemTableList();
var interaction = manager?.GetSpineInteractionTable(interactionId);
```

不要假定 C# 源码能直接访问 `RawDataManager.Instance` 或原生控制器的所有属性。本机编译已验证上述访问方式。

### 8.2 SetUI 前缀的顺序

`SpineInteractionUI.SetUI(int,List<int>)` 前缀执行以下操作：

1. 关闭仍在运行的本地 P 场景，记住旧选择的资源、服装和入口来源。
2. 复制传入列表，处理上次注入及旧实验 ID，并记录当前官方列表。
3. 重新读取 Manager 配置，获得本次启用的场景。
4. 校验六项官方行的 ID、资源名、`Type=2` 和 InteractionId，继续核对关联互动表。
5. 对列表中尚无的有效行追加真实 ID，记录本次实际添加的 ID。
6. 0.3.0 会将成功使用原生入口的场景移出 P 用的 `Specs`，避免同场景同时出现两个入口；其余场景继续走既有 P 路线。
7. 处理已移除入口的旧选择，记录本次装饰后的列表引用，交给原始 SetUI 执行。

官方记录或关联失效时会输出警告，保留对应场景的 P 备用路径。

### 8.3 保留原生行为

原生 ID 不属于 P 用的 `Specs`，因此：

- 原始头像单元格 `SpineInteractionLoopScrollItem.SetUI` 继续运行，不显示 P 标记。
- `SelectPrefix` 关闭 P 场景后返回 `true`，原始 `SetCharSelect` 继续运行。
- 原生 Reset、Hint、录制、回放和互动控制器继续走游戏逻辑。
- `NativeControllerPostfix` 只记录原生控制器初始化结果，不替换阶段或触点。

原生模式保留游戏的章节检查和进度行为。dating19 的解锁组/章节仍为 14/10。入口注入不会使服务器账号永久获得对应内容。

### 8.4 去重与账号后来获得的条目

`NativeDatingEntries.CopyOfficialIds` 区分新的账号列表与上次处理过的列表：

- 同一个装饰后的 `List<int>` 再次传入时，先去掉上次实际添加的 ID，再按当前开关重建。
- 新的调用方列表可能已包含账号真正获得的行，必须保留这些行。
- `AddedIds` 只记录本次原列表没有的行；关闭新增功能时不能误删账号已有入口。

旧 P 选择按资源名迁移到原生记录，避免 dating12 的 CostumeId/PrestigeSkinId 差异造成错误匹配；无可用对应项时选择有效的现有入口。

## 9. 配置、构建与安装

0.3.0 的配置：

```ini
# BepInEx/config/local.bd2.previewinteractions.cfg
[Scenes]
UseNativeDatingEntries = true

[Diagnostics]
NativeEntryProbeEnabled = false
```

Manager 的场景配置写在游戏 `BepInEx/plugins/BD2PreviewInteractions/settings.json`，总开关及逐项 `enabled` 控制添加。0.3.0 的 `UseNativeDatingEntries=false` 恢复 P 路线。原生模式使用 BrownDustX 的正常模组替换；P 专用的 JSON、音频路径、自定义输入和 HUD 设置不参与原生加载。

在 Windows PowerShell 的仓库根目录构建：

```powershell
dotnet build plugins/BD2PreviewInteractions/BD2PreviewInteractions.csproj -c Release
pnpm build:game-plugin
```

第一条直接生成插件；第二条使用仓库脚本构建并同步 Manager 内嵌插件资源。项目目标是 `netstandard2.1`，引用来自本机 BepInEx 和 Managed 目录，`.build-tools/csc.cmd` 负责通过 dotnet 执行 Roslyn。游戏目录可通过 `-p:GameRoot=...` 或构建脚本支持的 `BD2_GAME_ROOT` 调整。

输出 DLL 位于 `plugins/BD2PreviewInteractions/bin/Release/netstandard2.1/BD2PreviewInteractions.dll`。安装位置为游戏 `BepInEx/plugins/BD2PreviewInteractions/BD2PreviewInteractions.dll`。替换前备份原 DLL 与 cfg，通过 Windows 检查文件占用，复制后核对 SHA256，重启游戏加载新版本。

## 10. 验证结果与范围

本次通过 27 项真实 protobuf/列表生命周期检查及 16 项原生复制回归检查：

```powershell
dotnet build plugins/BD2PreviewInteractions/tests/NativeDatingEntryTests/NativeDatingEntryTests.csproj -c Release
dotnet exec plugins/BD2PreviewInteractions/tests/NativeDatingEntryTests/bin/Release/net10.0/NativeDatingEntryTests.dll 'F:\Neowiz\releases\official-dating-six-20261112.json'
dotnet build plugins/BD2PreviewInteractions/tests/NativeEntryProbeTests/NativeEntryProbeTests.csproj -c Release
dotnet exec plugins/BD2PreviewInteractions/tests/NativeEntryProbeTests/bin/Release/net10.0/NativeEntryProbeTests.dll
```

运行日志确认 `Native dating list ready: added=6, active=6, entries=22`，以及以下六项原生控制器初始化：

| 场景 | 阶段数 | 点组件数 |
|---|---:|---:|
| dating7 | 2 | 63 |
| dating9 | 2 | 77 |
| dating12 | 3 | 87 |
| dating18 | 7 | 92 |
| dating19 | 2 | 58 |
| dating21 | 3 | 54 |

用户已明确确认“六项均出现，并能进入”。本轮未发现无效缩略图、无效 Addressables 键、缺失骨骼或空引用消息。所有输入组合、音频事件、录制回放及 Reset 未逐项单独验收，不应由入口加载成功推断全部已测。

0.3.0 DLL SHA256：`682a8c83e6eec0952b128c8a9a3725af18459cf83f9d0aa50e4a28ad78aa813d`。该版本备份目录为 `F:\Neowiz\mod_backups\BD2PreviewInteractions-native-six-20261112-014937`。

## 11. 后续更新与相关文档

更新游戏后重新确认 common 缓存、程序集哈希、数据解码参数、原生 UI 方法签名和资源关联；不要从旧编号插值生成新记录。六项之外的场景应按完整表发现，保留有效关联与重复资源的区别。

- prefab/触点几何解包：[dating-prefab-unpack.md](./dating-prefab-unpack.md)
- 项目演进与验证记录：[local-interactions-work.md](./local-interactions-work.md)
- 插件使用说明：[README.md](../plugins/BD2PreviewInteractions/README.md)

## 12. 0.4.0：全部约会角色的双入口管理

Manager 页面和侧栏改为「约会互动」。当前目录有 22 个场景，每项均可独立开启 P 对象或官方原生对象，允许两者同时存在；两种方式各有总开关和批量开关。用户已明确选择：关闭某角色的原生开关时，也隐藏账号已拥有的该角色原生入口，只影响列表显示。

### 12.1 配置格式

```json
{
  "schemaVersion": 2,
  "enabled": true,
  "pEntriesEnabled": true,
  "nativeEntriesEnabled": true,
  "scenes": [
    {
      "sceneId": "illust_dating7",
      "costumeId": 296,
      "avatarAddress": "IllustInventory_1/illust_inven_char000296_174.png",
      "title": "悠丝缇亚 · 仲夏之梦",
      "enabled": true,
      "nativeEnabled": true,
      "useMods": true
    }
  ]
}
```

示例省略了目录、输入参数和其他场景。场景级 `enabled` 是 P 开关，顶层 `enabled` 是整个管理功能的开关；原生开关单独保存。Manager 后端会补齐目录，缺省的新场景开关不自动启用。v1 配置按原 cfg 的旧模式迁移，保留当前六项原生设置及其他原有官方入口。

### 12.2 插件路线变化

`LocalInteractionSettings` 分别产生 P 用的 `Scenes` 和原生用的 `NativeScenes`。`Specs` 只接收启用的 P 场景；原生场景不再从中删除同场景 P 项。因此开启两种方式时，真实官方 ID 与 `1900000000 + N` 可以并存。

`NativeDatingEntries` 从当前完整表按资源名、`Type=2`、正 InteractionId 发现场景，不再限定六个硬编码绑定。有且只有一个有效候选且关联互动表存在时使用该官方行。仍排除五条无互动关联的资源同名记录；将来更新若出现多个有效候选，会提示歧义而不是任取一条。

原生关闭时过滤该资源对应的列表行。为了支持重新开启，保存的是进入过滤前的官方列表快照；同一个装饰后列表再次使用时，先恢复快照再重建。新的账号列表直接复制，保留本次真正获得的记录。

P 数据行从完整官方表准备，优先按资源名选模板，不依赖当前可见原生列表。因此关闭全部原生入口后，P 对象仍可独立使用。两个模式全部关闭且列表为空时，跳过要求有效行 ID 的原始 SetUI，下一帧关闭界面并显示提示。

### 12.3 对第 8—9 节的替代关系

0.3.0 的“原生成功则自动隐藏对应 P”及 cfg 模式切换被 v2 独立开关替代。关闭管理总开关恢复游戏原列表；保持管理开启而关闭原生方式，则隐藏原生入口。原生记录异常不会自动打开 P，P 状态始终由用户的独立开关决定。

新增测试覆盖全部 22 项记录发现、已拥有原生入口隐藏/恢复、原生全部关闭，以及 C# 和 Rust 两侧的旧配置迁移与开关保留。UI 类型检查、生产构建和 Manager 可执行文件构建也属于交付检查。

### 12.4 设置页面与交付

截图选中的五项配置已移到「设置 → 约会互动」：互动 JSON 目录、约会音频目录、美术素材库目录、左键长按时间和触点透明度。侧栏「约会互动」保留角色列表、P / 原生入口开关及插件安装功能。

两个页面通过 `src/stores/datingInteractions.ts` 共用一份草稿。切换页面不会重新读取并覆盖未保存的修改；任意一页保存时都提交完整草稿，避免资源配置覆盖另一页刚修改的角色开关。独立检查已确认跨页编辑、目录和长按参数保存后仍保留角色选择。

本次版本为 Manager `4.3.1-local.2` 和插件 `0.4.0`，交付目录为 `F:\Neowiz\releases\BD2ModManager-4.3.1-local.2`。C# 原生记录检查 31 项、配置解析检查 8 项、Rust 配置检查 5 项均通过；Vue 类型检查、前端生产构建和 Rust release 构建通过。0.4.0 的完整双入口组合仍需游戏内验收，不能用 0.3.0 六项原生进入成功代替。

已替换游戏插件并启动 `F:\Neowiz\BD2ModManager\src-tauri\target\release\BD2ModManager.exe`，进程创建窗口且保持响应。交付 EXE、构建 EXE 以及嵌入、交付和游戏安装的插件均已核对一致。原游戏 JSON、cfg 和 Manager 配置未改写，备份位于 `F:\Neowiz\mod_backups\BD2-dating-0.4.0-20261112-023313`；文件哈希记录在交付目录的 `build-info.json`。

### 12.5 当前版本的复现与安装流程

在已配置 MSVC x64 的 Windows 开发者 PowerShell 中运行，Node.js、dotnet 和 Cargo 应位于 PATH；使用仓库现有依赖，不需要下载游戏资源：

```powershell
Set-Location 'F:\Neowiz\BD2ModManager'
$env:BD2_GAME_ROOT = 'F:\Neowiz\Browndust2\Browndust2_10000001'

# 构建两个游戏插件、同步内嵌 DLL、执行 Vue 类型检查并生成前端。
npm run build

# 构建包含生产前端及插件资源的 Manager。
Push-Location src-tauri
cargo build --offline --release --features tauri/custom-protocol
Pop-Location

# 以实际导出的六项和完整 dating 记录检查列表发现、隐藏及恢复。
dotnet build plugins/BD2PreviewInteractions/tests/NativeDatingEntryTests/NativeDatingEntryTests.csproj -c Release
dotnet exec plugins/BD2PreviewInteractions/tests/NativeDatingEntryTests/bin/Release/net10.0/NativeDatingEntryTests.dll 'F:\Neowiz\releases\official-dating-six-20261112.json' 'F:\Neowiz\releases\official-dating-all-20261112.json'

# 检查 C# 侧独立开关及旧配置迁移。
dotnet build plugins/BD2PreviewInteractions/tests/DatingSettingsTests/DatingSettingsTests.csproj -c Release
dotnet exec plugins/BD2PreviewInteractions/tests/DatingSettingsTests/bin/Release/net10.0/DatingSettingsTests.dll

# 检查 Rust 侧配置校验与迁移。
Push-Location src-tauri
cargo test --offline interactions::tests --lib
Pop-Location
```

插件直接构建产物与 `src-tauri/resources/BD2PreviewInteractions.dll` 应一致；Manager 的 release EXE 位于 `src-tauri/target/release/BD2ModManager.exe`。如果仅执行 `dotnet build`，还应运行 `npm run build:game-plugin` 同步内嵌 DLL，然后重新构建 Manager，避免安装按钮仍写入旧版插件。

安装时正常退出游戏；替换正在运行的 Manager 前也需退出 Manager。备份旧 DLL、`settings.json`、cfg 及 Manager 配置，保留现有目录和逐角色选择。启动新版后，在「约会互动」页安装/更新插件并保存配置；「设置 → 约会互动」维护五项资源与操作参数。替换 DLL 后重启游戏；仅改开关或参数时保存并重新进入约会界面。

验收应分别检查：只开原生、只开 P、同一场景两者同时开启、隐藏已拥有原生入口后重新开启、全部关闭，以及关闭管理总开关恢复账号原列表。P 路线仍需有效的本地 JSON 和音频；原生路线使用当前官方表和游戏资源。游戏更新后首先重验表关联及 Harmony 方法签名，再复用入口逻辑。

## 13. 0.4.1：移除逐角色素材选择

Manager `4.3.1-local.3` 移除每张约会角色卡片底部的「P 入口素材」下拉框。P 场景默认应用该 `illust_datingN` 当前已启用的 BrownDustX 模组，没有对应启用模组时沿用游戏素材。

为兼容旧文件，Manager 仍保留 `useMods` 字段，读取和保存时统一为 `true`。插件 `0.4.1` 解析旧配置时也不再让 `useMods=false` 覆盖此默认行为；不改变场景的 P / 原生开关、资源目录、长按时间和触点透明度。原有 cfg 的 `Scenes.ApplyEnabledDatingMods` 是默认开启的高级全局开关，仍保留。

对应代码位于 `InteractionsTab.vue`、`interactions.rs` 和 `LocalInteractionSettings.cs`。既有 C# 配置检查已改为验证旧版原版素材选择不再阻止应用模组，Rust 的开关往返检查同时确认规范化后仍保留独立的入口选择。

本次 C# 配置检查 8 项、Rust 配置检查 5 项、Vue 类型检查、前端生产构建及 Windows release 构建均通过。新版 Manager 已替换并重新启动，插件 DLL 已通过独占文件占用检查后替换，交付、内嵌与安装文件已核对一致；保留原游戏 JSON 和 cfg。备份目录为 `F:\Neowiz\mod_backups\BD2-dating-0.4.1-20261112-024332`，版本和哈希见当前交付目录的 `build-info.json`。替换时游戏仍在运行，当前进程使用原已加载版本，插件 0.4.1 在下次重启游戏时加载。

## 14. 全部约会 ID 数据（2026-11-12 快照）

本节记录本次解包得到的全部 27 条 dating 资源 Lobby 记录、22 条关联 Interaction 记录，以及 Manager 的 22 个 P 对象编号。官方数值直接取自已解析的设计表；角色中文名、Manager 角色 ID、P 服装编号及立绘地址取自仓库的 `src-tauri/data/characters.json`。

数据来源为 `F:\Neowiz\releases\official-dating-all-20261112.json`，该导出文件 SHA256 为 `4262629bdc9b8b6ca723bb5a74f40c4b67a70f0348b267c833cb689a82ee528d`。六项缺失入口已与 `official-dating-six-20261112.json` 的 Lobby / Interaction 字段逐项核对一致。完整解析字段保存在第 14.6 节，查阅这些 ID 无需另开仓库外文件。

### 14.1 全部 22 项有效原生入口

`Lobby ID` 是游戏原生列表中的入口编号；`InteractionId` 与关联 `SpineInteractionTable.Id` 相等。解锁组和章节分别对应 `unlockDatinggroupId` 与 `unlockDatingId`。表内的 `0` 保留官方默认值。

| 官方资源名 | 角色 / 服装 | Lobby ID | InteractionId | CostumeId | PrestigeSkinId | 解锁组 / 章节 ID |
|---|---|---|---|---|---|---|
| Illust_dating1 | 内布利斯 · 新进员工 | 32 | 1 | 3303 | 0 | 1 / 10 |
| Illust_dating2 | 墨菲亚 · 白日梦兔女郎 | 50 | 2 | 3402 | 0 | 2 / 10 |
| Illust_dating3 | 罗安 · 名人兔女郎 | 53 | 3 | 3203 | 0 | 3 / 10 |
| Illust_dating4 | 泰瑞丝 · 保健部 | 36 | 4 | 1106 | 0 | 4 / 10 |
| Illust_dating5 | 爱丽洁 · 代号O | 59 | 5 | 60802 | 0 | 5 / 10 |
| Illust_dating6 | 威廉明娜 · 水上乐园女王 | 73 | 6 | 67603 | 0 | 6 / 10 |
| Illust_dating7 | 悠丝缇亚 · 仲夏之梦 | 77 | 1000296 | 296 | 296 | 0 / 0 |
| Illust_dating8 | 席比雅 · 比基尼特工 | 94 | 7 | 1006 | 0 | 7 / 10 |
| Illust_dating9 | 泰瑞丝 · 奶牛比基尼 | 99 | 1001197 | 1197 | 1197 | 0 / 0 |
| Illust_dating10 | 安洁莉卡 · 霓虹救星 | 105 | 8 | 66403 | 0 | 8 / 10 |
| Illust_dating11 | 伊柯利普斯 · 恶梦兔女郎 | 114 | 9 | 706 | 0 | 9 / 10 |
| Illust_dating12 | 杰尼斯 · 神秘面纱兔女郎 | 121 | 1061492 | 61402 | 61492 | 0 / 0 |
| Illust_dating13 | 提尔 · 澄心无邪兔女郎 | 127 | 10 | 4102 | 0 | 10 / 10 |
| Illust_dating14 | 黎维塔 · 享乐主义者 | 135 | 1003892 | 3892 | 3892 | 0 / 0 |
| Illust_dating15 | 班塔纳 · 温泉修行者 | 138 | 11 | 67004 | 0 | 11 / 10 |
| Illust_dating16 | 奥利维耶 · 传奇退役 | 153 | 12 | 3604 | 0 | 12 / 10 |
| Illust_dating17 | 帕莱特 · 奇迹紫罗兰 | 184 | 13 | 4202 | 0 | 13 / 10 |
| Illust_dating18 | 莎赫拉查德 · 从容的王牌 | 207 | 1000396 | 396 | 396 | 0 / 0 |
| Illust_dating19 | 格兰希特 · 战地医疗兵 | 225 | 14 | 67104 | 0 | 14 / 10 |
| Illust_dating20 | 达非 · 明月 | 261 | 15 | 61302 | 0 | 15 / 10 |
| Illust_dating21 | 尤里 · 永远的吉祥物 | 274 | 1065193 | 65193 | 65193 | 0 / 0 |
| Illust_dating22 | 阿奇拉 · 被封印的欲望 | 326 | 16 | 67902 | 0 | 16 / 10 |

全部 27 条 Lobby 记录的 `Type=2`、`PackId=0`、`ProvideType=0`。22 条关联 Interaction 记录的 `hasSoundVoiceJP=0`、`hasSoundVoiceKR=1`；音频银行、BGM 和环境音事件字符串也保留在第 14.6 节。

### 14.2 同资源但未关联互动的 5 条记录

以下记录的 `InteractionId=0`，关联 `interaction` 为 `null`。原生入口发现逻辑将其排除。

| 官方资源名 | Lobby ID | CostumeId | PrestigeSkinId | InteractionId |
|---|---|---|---|---|
| Illust_dating7 | 218 | 296 | 296 | 0 |
| Illust_dating9 | 217 | 1197 | 1197 | 0 |
| Illust_dating12 | 161 | 61402 | 61492 | 0 |
| Illust_dating18 | 208 | 396 | 396 | 0 |
| Illust_dating21 | 275 | 65193 | 65193 | 0 |

### 14.3 全部 27 条名称文本与头像资源标识

`ItemNameTextId`、`IconSpriteName` 和 `ItemPath` 均按解包字段原值记录，包含未关联互动的 5 条记录。部分早期行的缩略图及文本后缀与 Lobby ID 不同，使用时应直接查表。

| Lobby ID | 官方资源名 | ItemNameTextId | IconSpriteName | ItemPath |
|---|---|---|---|---|
| 32 | Illust_dating1 | 6000032 | thumb_specialIllust_32 | SpecialIllust32 |
| 36 | Illust_dating4 | 6000048 | thumb_specialIllust_55 | SpecialIllust55 |
| 50 | Illust_dating2 | 6000040 | thumb_specialIllust_37 | SpecialIllust37 |
| 53 | Illust_dating3 | 6000043 | thumb_specialIllust_49 | SpecialIllust49 |
| 59 | Illust_dating5 | 6000059 | thumb_specialIllust_59 | SpecialIllust59 |
| 73 | Illust_dating6 | 6000073 | thumb_specialIllust_73 | SpecialIllust73 |
| 77 | Illust_dating7 | 6000077 | thumb_specialIllust_77 | SpecialIllust77 |
| 94 | Illust_dating8 | 6000094 | thumb_specialIllust_94 | SpecialIllust94 |
| 99 | Illust_dating9 | 6000099 | thumb_specialIllust_99 | SpecialIllust99 |
| 105 | Illust_dating10 | 6000105 | thumb_specialIllust_105 | SpecialIllust105 |
| 114 | Illust_dating11 | 6000114 | thumb_specialIllust_114 | SpecialIllust114 |
| 121 | Illust_dating12 | 6000121 | thumb_specialIllust_121 | SpecialIllust121 |
| 127 | Illust_dating13 | 6000127 | thumb_specialIllust_127 | SpecialIllust127 |
| 135 | Illust_dating14 | 6000135 | thumb_specialIllust_135 | SpecialIllust135 |
| 138 | Illust_dating15 | 6000138 | thumb_specialIllust_138 | SpecialIllust138 |
| 153 | Illust_dating16 | 6000153 | thumb_specialIllust_153 | SpecialIllust153 |
| 161 | Illust_dating12 | 6000161 | thumb_specialIllust_161 | SpecialIllust161 |
| 184 | Illust_dating17 | 6000184 | thumb_specialIllust_184 | SpecialIllust184 |
| 207 | Illust_dating18 | 6000207 | thumb_specialIllust_207 | SpecialIllust207 |
| 208 | Illust_dating18 | 6000208 | thumb_specialIllust_208 | SpecialIllust208 |
| 217 | Illust_dating9 | 6000217 | thumb_specialIllust_217 | SpecialIllust217 |
| 218 | Illust_dating7 | 6000218 | thumb_specialIllust_218 | SpecialIllust218 |
| 225 | Illust_dating19 | 6000225 | thumb_specialIllust_225 | SpecialIllust225 |
| 261 | Illust_dating20 | 6000261 | thumb_specialIllust_261 | SpecialIllust261 |
| 274 | Illust_dating21 | 6000274 | thumb_specialIllust_274 | SpecialIllust274 |
| 275 | Illust_dating21 | 6000275 | thumb_specialIllust_275 | SpecialIllust275 |
| 326 | Illust_dating22 | 6000326 | thumb_specialIllust_326 | SpecialIllust326 |

### 14.4 全部 22 项 P 对象与 Manager 角色编号

P 对象的 `LocalId` 由插件按 `1900000000 + N` 生成，`SceneId` 使用 `illust_datingN`。下表的 Manager 角色 ID 对应目录中的 `character_id`；P 服装编号保留目录中的六位格式，实际 `SceneSpec.CostumeId` 按整数读取。

| SceneId | P LocalId | Manager 角色 ID | P 服装编号 | P AvatarAddress |
|---|---|---|---|---|
| illust_dating1 | 1900000001 | 33 | 003303 | IllustInventory_1/illust_inven_char003303_149.png |
| illust_dating2 | 1900000002 | 34 | 003402 | IllustInventory_1/illust_inven_char003402_152.png |
| illust_dating3 | 1900000003 | 32 | 003203 | IllustInventory_1/illust_inven_char003203_153.png |
| illust_dating4 | 1900000004 | 11 | 001106 | IllustInventory_1/illust_inven_char001106_117.png |
| illust_dating5 | 1900000005 | 608 | 060802 | IllustInventory_1/illust_inven_char060802_50.png |
| illust_dating6 | 1900000006 | 676 | 067603 | IllustInventory_1/illust_inven_char067603_171.png |
| illust_dating7 | 1900000007 | 2 | 000296 | IllustInventory_1/illust_inven_char000296_174.png |
| illust_dating8 | 1900000008 | 10 | 001006 | IllustInventory_1/illust_inven_char001006_177.png |
| illust_dating9 | 1900000009 | 11 | 001197 | IllustInventory_1/illust_inven_char001197_179.png |
| illust_dating10 | 1900000010 | 664 | 066403 | IllustInventory_1/illust_inven_char066403_96.png |
| illust_dating11 | 1900000011 | 7 | 000706 | IllustInventory_1/illust_inven_char000706_106.png |
| illust_dating12 | 1900000012 | 614 | 061492 | IllustInventory_1/illust_inven_char061492_188.png |
| illust_dating13 | 1900000013 | 41 | 004102 | IllustInventory_1/illust_inven_char004102_186.png |
| illust_dating14 | 1900000014 | 38 | 003892 | IllustInventory_1/illust_inven_char003892_190.png |
| illust_dating15 | 1900000015 | 670 | 067004 | IllustInventory_1/illust_inven_char067004_157.png |
| illust_dating16 | 1900000016 | 36 | 003604 | IllustInventory_1/illust_inven_char003604_196.png |
| illust_dating17 | 1900000017 | 42 | 004202 | IllustInventory_1/illust_inven_char004202_200.png |
| illust_dating18 | 1900000018 | 3 | 000396 | IllustInventory_1/illust_inven_char000396_202.png |
| illust_dating19 | 1900000019 | 671 | 067104 | IllustInventory_1/illust_inven_char067104_206.png |
| illust_dating20 | 1900000020 | 613 | 061302 | IllustInventory_1/illust_inven_char061302_104.png |
| illust_dating21 | 1900000021 | 651 | 065193 | IllustInventory_1/illust_inven_char065193_224.png |
| illust_dating22 | 1900000022 | 679 | 067902 | IllustInventory_1/illust_inven_char067902_226.png |

dating12 的原生 `CostumeId=61402`、`PrestigeSkinId=61492`，P 目录使用的服装编号为 `061492`。这三列保留各自来源，查找原生记录时按资源名和有效互动关联匹配。

### 14.5 原生复制实验的临时编号

| 用途 | 列表行 ID | InteractionId | 资源名 |
|---|---|---|---|
| 复制实验的官方源行 | 32 | 1 | Illust_dating1 |
| 复制实验的临时副本 | 1900100001 | 1 | Illust_dating1 |

临时副本只在开启 `NativeEntryProbeEnabled` 时创建，完整克隆源行并修改 `Id`；默认关闭。副本的 CostumeId、章节条件及其他关联字段沿用源行 32。P 对象的全部 22 个编号与该实验编号均已列出。

### 14.6 全部 27 条官方解析记录

以下 JSON 保留全部已解析的 `lobby` 字段和关联 `interaction` 字段，包括所有 ID、默认值、空数组和音频资源标识；按资源序号及 Lobby ID 排序。

<details>
<summary>展开全部 27 条官方记录（22 条有效关联、5 条未关联）</summary>

```json
[
  {
    "lobby": {
      "costumeId": 3303,
      "iconSpriteName": [
        "thumb_specialIllust_32"
      ],
      "id": 32,
      "interactionId": 1,
      "interactionResourceName": "Illust_dating1",
      "itemNameTextId": 6000032,
      "itemPath": [
        "SpecialIllust32"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 1,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Night_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V04_Interaction",
      "soundVoiceBankName": "Interaction_Char003303",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 1
    }
  },
  {
    "lobby": {
      "costumeId": 3402,
      "iconSpriteName": [
        "thumb_specialIllust_37"
      ],
      "id": 50,
      "interactionId": 2,
      "interactionResourceName": "Illust_dating2",
      "itemNameTextId": 6000040,
      "itemPath": [
        "SpecialIllust37"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 2,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V04_Interaction",
      "soundVoiceBankName": "Interaction_Char003402",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 2
    }
  },
  {
    "lobby": {
      "costumeId": 3203,
      "iconSpriteName": [
        "thumb_specialIllust_49"
      ],
      "id": 53,
      "interactionId": 3,
      "interactionResourceName": "Illust_dating3",
      "itemNameTextId": 6000043,
      "itemPath": [
        "SpecialIllust49"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 3,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V05",
      "soundVoiceBankName": "Interaction_Char003203",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 3
    }
  },
  {
    "lobby": {
      "costumeId": 1106,
      "iconSpriteName": [
        "thumb_specialIllust_55"
      ],
      "id": 36,
      "interactionId": 4,
      "interactionResourceName": "Illust_dating4",
      "itemNameTextId": 6000048,
      "itemPath": [
        "SpecialIllust55"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 4,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Night_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V04_Interaction",
      "soundVoiceBankName": "Interaction_Char001106",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 4
    }
  },
  {
    "lobby": {
      "costumeId": 60802,
      "iconSpriteName": [
        "thumb_specialIllust_59"
      ],
      "id": 59,
      "interactionId": 5,
      "interactionResourceName": "Illust_dating5",
      "itemNameTextId": 6000059,
      "itemPath": [
        "SpecialIllust59"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 5,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V05",
      "soundVoiceBankName": "Interaction_Char060802",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 5
    }
  },
  {
    "lobby": {
      "costumeId": 67603,
      "iconSpriteName": [
        "thumb_specialIllust_73"
      ],
      "id": 73,
      "interactionId": 6,
      "interactionResourceName": "Illust_dating6",
      "itemNameTextId": 6000073,
      "itemPath": [
        "SpecialIllust73"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 6,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V05",
      "soundVoiceBankName": "Interaction_Char067603",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 6
    }
  },
  {
    "lobby": {
      "costumeId": 296,
      "iconSpriteName": [
        "thumb_specialIllust_77"
      ],
      "id": 77,
      "interactionId": 1000296,
      "interactionResourceName": "Illust_dating7",
      "itemNameTextId": 6000077,
      "itemPath": [
        "SpecialIllust77"
      ],
      "packId": 0,
      "prestigeSkinId": 296,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 1000296,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V03_Normal",
      "soundVoiceBankName": "Interaction_Char000296",
      "unlockDatingId": 0,
      "unlockDatinggroupId": 0
    }
  },
  {
    "lobby": {
      "costumeId": 296,
      "iconSpriteName": [
        "thumb_specialIllust_218"
      ],
      "id": 218,
      "interactionId": 0,
      "interactionResourceName": "Illust_dating7",
      "itemNameTextId": 6000218,
      "itemPath": [
        "SpecialIllust218"
      ],
      "packId": 0,
      "prestigeSkinId": 296,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": null
  },
  {
    "lobby": {
      "costumeId": 1006,
      "iconSpriteName": [
        "thumb_specialIllust_94"
      ],
      "id": 94,
      "interactionId": 7,
      "interactionResourceName": "Illust_dating8",
      "itemNameTextId": 6000094,
      "itemPath": [
        "SpecialIllust94"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 7,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V05",
      "soundVoiceBankName": "Interaction_Char001006",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 7
    }
  },
  {
    "lobby": {
      "costumeId": 1197,
      "iconSpriteName": [
        "thumb_specialIllust_99"
      ],
      "id": 99,
      "interactionId": 1001197,
      "interactionResourceName": "Illust_dating9",
      "itemNameTextId": 6000099,
      "itemPath": [
        "SpecialIllust99"
      ],
      "packId": 0,
      "prestigeSkinId": 1197,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 1001197,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_Common_Beach_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V01_Normal",
      "soundVoiceBankName": "Interaction_Char001197",
      "unlockDatingId": 0,
      "unlockDatinggroupId": 0
    }
  },
  {
    "lobby": {
      "costumeId": 1197,
      "iconSpriteName": [
        "thumb_specialIllust_217"
      ],
      "id": 217,
      "interactionId": 0,
      "interactionResourceName": "Illust_dating9",
      "itemNameTextId": 6000217,
      "itemPath": [
        "SpecialIllust217"
      ],
      "packId": 0,
      "prestigeSkinId": 1197,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": null
  },
  {
    "lobby": {
      "costumeId": 66403,
      "iconSpriteName": [
        "thumb_specialIllust_105"
      ],
      "id": 105,
      "interactionId": 8,
      "interactionResourceName": "Illust_dating10",
      "itemNameTextId": 6000105,
      "itemPath": [
        "SpecialIllust105"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 8,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V05",
      "soundVoiceBankName": "Interaction_Char066403",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 8
    }
  },
  {
    "lobby": {
      "costumeId": 706,
      "iconSpriteName": [
        "thumb_specialIllust_114"
      ],
      "id": 114,
      "interactionId": 9,
      "interactionResourceName": "Illust_dating11",
      "itemNameTextId": 6000114,
      "itemPath": [
        "SpecialIllust114"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 9,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V05",
      "soundVoiceBankName": "Interaction_Char000706",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 9
    }
  },
  {
    "lobby": {
      "costumeId": 61402,
      "iconSpriteName": [
        "thumb_specialIllust_121"
      ],
      "id": 121,
      "interactionId": 1061492,
      "interactionResourceName": "Illust_dating12",
      "itemNameTextId": 6000121,
      "itemPath": [
        "SpecialIllust121"
      ],
      "packId": 0,
      "prestigeSkinId": 61492,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 1061492,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_Common_Snow_01",
      "soundBGMName": "event:/BGM/Visual_Interaction/BGM_Interaction_061492",
      "soundVoiceBankName": "Interaction_Char061492",
      "unlockDatingId": 0,
      "unlockDatinggroupId": 0
    }
  },
  {
    "lobby": {
      "costumeId": 61402,
      "iconSpriteName": [
        "thumb_specialIllust_161"
      ],
      "id": 161,
      "interactionId": 0,
      "interactionResourceName": "Illust_dating12",
      "itemNameTextId": 6000161,
      "itemPath": [
        "SpecialIllust161"
      ],
      "packId": 0,
      "prestigeSkinId": 61492,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": null
  },
  {
    "lobby": {
      "costumeId": 4102,
      "iconSpriteName": [
        "thumb_specialIllust_127"
      ],
      "id": 127,
      "interactionId": 10,
      "interactionResourceName": "Illust_dating13",
      "itemNameTextId": 6000127,
      "itemPath": [
        "SpecialIllust127"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 10,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V05",
      "soundVoiceBankName": "Interaction_Char004102",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 10
    }
  },
  {
    "lobby": {
      "costumeId": 3892,
      "iconSpriteName": [
        "thumb_specialIllust_135"
      ],
      "id": 135,
      "interactionId": 1003892,
      "interactionResourceName": "Illust_dating14",
      "itemNameTextId": 6000135,
      "itemPath": [
        "SpecialIllust135"
      ],
      "packId": 0,
      "prestigeSkinId": 3892,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 1003892,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Interaction/BGM_Interaction_003892",
      "soundVoiceBankName": "Interaction_Char003892",
      "unlockDatingId": 0,
      "unlockDatinggroupId": 0
    }
  },
  {
    "lobby": {
      "costumeId": 67004,
      "iconSpriteName": [
        "thumb_specialIllust_138"
      ],
      "id": 138,
      "interactionId": 11,
      "interactionResourceName": "Illust_dating15",
      "itemNameTextId": 6000138,
      "itemPath": [
        "SpecialIllust138"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 11,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V06_Sad",
      "soundVoiceBankName": "Interaction_Char067004",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 11
    }
  },
  {
    "lobby": {
      "costumeId": 3604,
      "iconSpriteName": [
        "thumb_specialIllust_153"
      ],
      "id": 153,
      "interactionId": 12,
      "interactionResourceName": "Illust_dating16",
      "itemNameTextId": 6000153,
      "itemPath": [
        "SpecialIllust153"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 12,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V04_Interaction",
      "soundVoiceBankName": "Interaction_Char003604",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 12
    }
  },
  {
    "lobby": {
      "costumeId": 4202,
      "iconSpriteName": [
        "thumb_specialIllust_184"
      ],
      "id": 184,
      "interactionId": 13,
      "interactionResourceName": "Illust_dating17",
      "itemNameTextId": 6000184,
      "itemPath": [
        "SpecialIllust184"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 13,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V04_Interaction",
      "soundVoiceBankName": "Interaction_Char004202",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 13
    }
  },
  {
    "lobby": {
      "costumeId": 396,
      "iconSpriteName": [
        "thumb_specialIllust_207"
      ],
      "id": 207,
      "interactionId": 1000396,
      "interactionResourceName": "Illust_dating18",
      "itemNameTextId": 6000207,
      "itemPath": [
        "SpecialIllust207"
      ],
      "packId": 0,
      "prestigeSkinId": 396,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 1000396,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Interaction/BGM_Interaction_000396",
      "soundVoiceBankName": "Interaction_Char000396",
      "unlockDatingId": 0,
      "unlockDatinggroupId": 0
    }
  },
  {
    "lobby": {
      "costumeId": 396,
      "iconSpriteName": [
        "thumb_specialIllust_208"
      ],
      "id": 208,
      "interactionId": 0,
      "interactionResourceName": "Illust_dating18",
      "itemNameTextId": 6000208,
      "itemPath": [
        "SpecialIllust208"
      ],
      "packId": 0,
      "prestigeSkinId": 396,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": null
  },
  {
    "lobby": {
      "costumeId": 67104,
      "iconSpriteName": [
        "thumb_specialIllust_225"
      ],
      "id": 225,
      "interactionId": 14,
      "interactionResourceName": "Illust_dating19",
      "itemNameTextId": 6000225,
      "itemPath": [
        "SpecialIllust225"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 14,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V04_Interaction",
      "soundVoiceBankName": "Interaction_Char067104",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 14
    }
  },
  {
    "lobby": {
      "costumeId": 61302,
      "iconSpriteName": [
        "thumb_specialIllust_261"
      ],
      "id": 261,
      "interactionId": 15,
      "interactionResourceName": "Illust_dating20",
      "itemNameTextId": 6000261,
      "itemPath": [
        "SpecialIllust261"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 15,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V04_Interaction",
      "soundVoiceBankName": "Interaction_Char061302",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 15
    }
  },
  {
    "lobby": {
      "costumeId": 65193,
      "iconSpriteName": [
        "thumb_specialIllust_274"
      ],
      "id": 274,
      "interactionId": 1065193,
      "interactionResourceName": "Illust_dating21",
      "itemNameTextId": 6000274,
      "itemPath": [
        "SpecialIllust274"
      ],
      "packId": 0,
      "prestigeSkinId": 65193,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 1065193,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_P1007_Interior_01",
      "soundBGMName": "event:/BGM/Visual_Interaction/BGM_Interaction_065193",
      "soundVoiceBankName": "Interaction_Char065193",
      "unlockDatingId": 0,
      "unlockDatinggroupId": 0
    }
  },
  {
    "lobby": {
      "costumeId": 65193,
      "iconSpriteName": [
        "thumb_specialIllust_275"
      ],
      "id": 275,
      "interactionId": 0,
      "interactionResourceName": "Illust_dating21",
      "itemNameTextId": 6000275,
      "itemPath": [
        "SpecialIllust275"
      ],
      "packId": 0,
      "prestigeSkinId": 65193,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": null
  },
  {
    "lobby": {
      "costumeId": 67902,
      "iconSpriteName": [
        "thumb_specialIllust_326"
      ],
      "id": 326,
      "interactionId": 16,
      "interactionResourceName": "Illust_dating22",
      "itemNameTextId": 6000326,
      "itemPath": [
        "SpecialIllust326"
      ],
      "packId": 0,
      "prestigeSkinId": 0,
      "provideType": 0,
      "type": 2,
      "wallPaperBGLoadType": [],
      "wallpaperBGFullPath": [],
      "wallpaperPath": []
    },
    "interaction": {
      "hasSoundVoiceJP": 0,
      "hasSoundVoiceKR": 1,
      "id": 16,
      "soundAmbName": "event:/Ambiences/Visual_Novel/AMB_Common_Dungeon_01",
      "soundBGMName": "event:/BGM/Visual_Novel/BGM_V08_Interaction",
      "soundVoiceBankName": "Interaction_Char067902",
      "unlockDatingId": 10,
      "unlockDatinggroupId": 16
    }
  }
]
```

</details>


## 15. Manager 4.3.1-local.4：在约会互动页面配置模组

本节记录 local.4 的独立弹窗；当前版本已改为第 16 节的共用角色配置窗口。

每张约会角色卡片新增「配置模组」按钮，显示该场景的已启用 / 已安装数量。弹窗显示当前配置文件名，提供搜索、逐项启停、预览、打开文件夹和刷新模组库。弹窗和页面底部均可执行「同步模组」，应用当前配置的模组变更。

### 15.1 场景匹配与状态来源

`InteractionsTab.vue` 从现有 `useModsStore().extendedMods` 读取模组，仅按 `modType.type === "Dating"` 和场景编号匹配。例如 `modType.id="22"` 对应 `illust_dating22`；不同 `real_id`（如 `22_1`、`22_2`）作为同一场景的独立部件全部列出。其他场景编号和立绘、技能等类型不会混入。

启停调用现有 `enableMods` / `disableMods`，选择直接保存到当前 Profile，并和「模组」页面实时共用同一份状态。原生及 P 入口使用同一套已启用的约会模组，角色的两个入口开关继续独立保存。模组刷新和同步均不会调用约会配置的保存或重新加载接口，因此保留该页面尚未保存的入口和资源参数。

### 15.2 操作与冲突处理

`DatingModsModal.vue` 只负责展示和发送用户操作；页面统一处理操作期间的禁用状态和失败消息。带阻断性文件错误的未启用模组不能开启；已启用的错误模组仍可关闭。`UnmanagedButInStaging` 沿用现有模组页面的警告规则。已启用的冲突关系显示在对应模组旁。

预览调用 `previewMods`，文件夹按钮调用现有 `openPath`。同步复用 `useModSync()`，沿用现有冲突处理、同步结果提示及自动同步设置；点击同步时先关闭角色模组弹窗，使冲突界面可以正常接续。手动同步针对整个当前 Profile，与「模组」页面一致。

### 15.3 本次检查

通过只读 IPC 快照核对本地 103 个 Dating 模组覆盖全部 22 个约会场景，其中包括多部件编号。隔离浏览器使用实际 Vue 页面与弹窗、上述分类快照和模拟操作接口完成 10 组检查：全部卡片计数和场景匹配、多部件显示及无关模组排除、无效文件禁用、启停参数、操作失败提示、预览和目录参数、搜索空结果、刷新保留草稿、同步接续保留草稿，以及空模组库提示。浏览器没有页面脚本错误，1000 像素窗口下弹窗无横向溢出。检查未改写用户的模组选择，也未执行真实游戏同步。

Vue 类型检查、前端生产构建和 Windows release 构建通过。插件保持 `0.4.1`，本次功能复用 Manager 的现有模组接口。交付目录为 `F:\Neowiz\releases\BD2ModManager-4.3.1-local.4`，版本、功能及文件哈希记录在 `build-info.json`。原 Manager EXE 与配置备份在 `F:\Neowiz\mod_backups\BD2Manager-dating-mods-20261112-030728`；现有 Profile、游戏约会配置和已安装插件均保留。


## 16. Manager 4.3.1-local.5：直接打开角色页的配置窗口

「约会互动」卡片的「配置模组」直接使用 `src/pages/characters/modals/CharacterModal.vue`，与角色页面共用同一组件。local.4 的独立 `DatingModsModal.vue` 已移除。共用窗口保留角色头像、服装信息、模组统计、立绘 / 过场动画 / 约会标签，以及单项预览、比较全部、删除模组和管理皮肤映射的原有操作。

点击卡片时，以 `illust_datingN` 的 N 调用角色 Store 的 `getCharacterByDatingId` 取得完整角色记录，沿用目录的服装 / 约会关联。这样 dating12 仍定位到目录中的尊爵服装 `061492`；原生表记录的 `CostumeId=61402` 不影响角色配置定位。

`CharacterModal` 新增可选 `initialTab`，约会页面传入 `Dating`，原有角色页面不传时仍默认显示 `Standing`。窗口内部按角色及初始标签重建标签组，从约会卡片再次打开时默认回到「约会」。可选 `busy` 用于在启停处理期间禁用重复操作，不改变角色页默认行为。

约会页面的模组启停仍使用现有 Store，操作错误通过通知展示；底部继续提供标准模组同步。打开、关闭或操作共用窗口不保存或重新读取约会配置，因此未保存的原生 / P 开关仍保留。

隔离浏览器的 8 组检查通过：22 个场景的共用窗口跳转与约会标签、三个模组标签切换、启停参数、单项及全部预览、未保存约会草稿保留、皮肤映射服装参数、原角色入口默认标签，以及重开窗口回到约会标签。检查使用真实组件与角色目录、模拟操作接口；未修改用户 Profile 或执行真实删除。1000 像素宽度无横向溢出，也未发现页面脚本错误。Vue 类型检查和前端生产构建通过。

Windows release 编译及链接已生成 `4.3.1-local.5`，交付目录为 `F:\Neowiz\releases\BD2ModManager-4.3.1-local.5`。已核对交付 EXE 与链接输出一致，且内嵌当前 `0.4.1` 插件。构建最后复制回原 EXE 时，仍运行的旧 Manager 占用该文件，Windows 返回 `os error 5`；此时交付文件已完整生成，原路径替换及启动待旧 Manager 退出后完成。备份目录和文件哈希见交付目录的 `build-info.json`。


## 17. 插件 0.4.2：pose 触点奖励导致 154907

用户截图显示 `Home screen not owned., error:154907`，并明确说明发生在约会中切换 pose 时。该次 `Player.log` 在新增原生角色控制器初始化之后记录了两次 `Recv(Error Packet[SpineInteractionReward] = 7) : SpineInteractionReward`。数字 154907 来自弹窗，日志中的 7 是该协议的错误结果；两者分别保留，避免混用。

### 17.1 调用链与原因

本机构建的 `SpineInteractionActionController.ὬὣὥὯὠὩὧὦὦὠὡ(SpineInteractionPoint)` 是独立的触点奖励辅助方法。它读取控制器的 Interaction ID、触点 Group ID 和触点 ID，查找 `SpineInteractionPointTable.RewardGroupId`，检查是否已领取，然后调用奖励管理器发送 `Proto.Net.SpineInteractionRewardRequest`。

该方法内部不切换 pose，也不写入阶段状态；姿态与互动逻辑位于调用者。新增记录能够正常初始化原生控制器，不表示服务器账号已持有对应主页。结合弹窗及协议日志，此次报错关联到未拥有入口的触点奖励申请，而非资源加载失败。

### 17.2 修复范围

`NativeDatingEntries.IsLocalOnlyInteraction` 只匹配同时存在于当前有效原生 Entries 和 AddedIds 的 Interaction ID。AddedIds 来自进入列表前的官方账号列表与插件添加结果之差；账号原有入口不会被视为本地添加。新的官方列表包含某条已获得记录后，再次重建时该记录不再属于 AddedIds。

`Plugin.NativeRewardPrefix` 对上述独立触点奖励方法安装 Harmony 前缀，读取控制器的 `get_CurInteractionGroupId()`。仅匹配本地补入且未拥有的 Interaction ID 时跳过奖励辅助方法，保留其调用者的原生 pose、输入与动画流程。没有发送奖励请求、伪造成功回调或写入已领取奖励 / 账号数据；账号拥有的角色仍执行原始奖励流程。第一次跳过会记录 `Local-only native dating: skipped point reward request ...`，每次进入约会列表后重新计次。

### 17.3 检查与交付

插件构建零警告、零错误，使用真实六项及全部 27 条 protobuf 的原生记录检查共 36 项通过，覆盖六项新增入口、16 项真实拥有入口、清空管理状态、账号后来获得角色后恢复正常奖励，以及原有隐藏 / 恢复行为。另核对了当前游戏 IL：钩子签名是接收 SpineInteractionPoint 的 void 方法，所读 ID 字段与 getter 一致，方法没有 pose / 状态写入，最终发送的协议是 SpineInteractionRewardRequest。

Manager 版本为 `4.3.1-local.6`，同时包含第 16 节的共用角色配置窗口，内嵌插件 `0.4.2`。更换 DLL 后需要重启游戏；运行中的 0.4.1 不会自动载入新 DLL。自动检查未执行真实奖励请求，重启后的游戏内验收见第 17.4 节。

插件 0.4.2 已安装到游戏目录，安装、内嵌和交付 DLL 哈希一致；现有 settings.json 保留。用户退出旧进程后，Manager 原路径 EXE 已完成替换并启动，版本为 `4.3.1-local.6`；游戏已重启，BepInEx 确认加载 `BD2 Preview Interactions 0.4.2` 且钩子初始化完成，无启动钩子异常。交付目录为 `F:\Neowiz\releases\BD2ModManager-4.3.1-local.6`，备份位于 `F:\Neowiz\mod_backups\BD2-dating-reward-0.4.2-20261112-043046`。交付目录的 `build-info.json` 记录文件哈希和部署状态，`reward-error-verification.json` 记录错误现象、实际协议和检查范围。

### 17.4 游戏内验收通过（2026-11-12）

用户在加载 0.4.2 后明确确认「切换正常，不再报错」。本轮日志记录了全部六项新增入口的原生控制器初始化，并实际命中奖励跳过逻辑：dating18 的 InteractionId=1000396、dating7=1000296、dating21=1065193。本次运行未再记录 SpineInteractionReward 错误，也未出现奖励识别或钩子初始化异常。

这确认了用户报告的 pose 切换 / 154907 回归已通过游戏内验收。其他输入组合、音频、录制和回放仍沿用各自已有的验证范围；不由本次结果扩大验收结论。对应日志证据和用户确认已保存到交付目录的 `reward-error-verification.json`。
