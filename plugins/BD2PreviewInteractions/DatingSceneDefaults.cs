using System.Collections.Generic;

namespace BD2PreviewInteractions;

internal static class DatingSceneDefaults
{
    internal static bool WasNativeDefault(int number) => number == 7 || number == 9 || number == 12 || number == 18 || number == 19 || number == 21;
    internal static Dictionary<int, SceneSpec> Create() => new()
    {
        [1_900_000_018] = new(1_900_000_018, "illust_dating18", 396, "", "IllustInventory_1/illust_inven_char000396_202.png", "莎赫拉查德 · 从容的王牌"),
        [1_900_000_021] = new(1_900_000_021, "illust_dating21", 65193, "", "IllustInventory_1/illust_inven_char065193_224.png", "尤里 · 永远的吉祥物"),
        [1_900_000_019] = new(1_900_000_019, "illust_dating19", 67104, "", "IllustInventory_1/illust_inven_char067104_206.png", "格兰希特 · 战地医疗兵"),
        [1_900_000_009] = new(1_900_000_009, "illust_dating9", 1197, "", "IllustInventory_1/illust_inven_char001197_179.png", "泰瑞丝 · 奶牛比基尼"),
        [1_900_000_012] = new(1_900_000_012, "illust_dating12", 61492, "", "IllustInventory_1/illust_inven_char061492_188.png", "杰尼斯 · 神秘面纱兔女郎"),
        [1_900_000_007] = new(1_900_000_007, "illust_dating7", 296, "", "IllustInventory_1/illust_inven_char000296_174.png", "悠丝缇亚 · 仲夏之梦"),
    };
}

internal sealed class SceneSpec
{
    internal SceneSpec(int localId, string sceneId, int costumeId, string iconName, string avatarAddress, string title, bool useMods = true)
    {
        LocalId = localId; SceneId = sceneId; CostumeId = costumeId; IconName = iconName;
        AvatarAddress = avatarAddress; Title = title; UseMods = useMods;
    }
    internal int LocalId { get; }
    internal string SceneId { get; }
    internal int CostumeId { get; }
    internal string IconName { get; }
    internal string AvatarAddress { get; }
    internal string Title { get; }
    internal bool UseMods { get; }
}
