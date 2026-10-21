namespace DNF86GM;

/// <summary>
/// 维度里的一个可选值：显示名 + 命中判定。
/// 判定直接作用在 Item 上，因此「多个维度组合」天然是逐条 AND（见 <see cref="ItemDb.Search"/>）。
/// </summary>
public sealed class FacetOption
{
    public string Label { get; }
    public Func<Item, bool> Match { get; }

    public FacetOption(string label, Func<Item, bool> match)
    {
        Label = label;
        Match = match;
    }
}

/// <summary>一个子类型维度（筛选轴）。不选任何值 = 该维度不参与筛选。</summary>
public sealed class Facet
{
    public string Label { get; }
    public IReadOnlyList<FacetOption> Options { get; }

    public Facet(string label, params FacetOption[] options)
    {
        Label = label;
        Options = options;
    }
}

/// <summary>
/// 各物品类型对应的子类型维度表。
///
/// 设计原则：
///   1. 「全部」不展示任何维度——各类型的维度语义不同，跨类型没有公共轴；
///   2. 每个维度都对应 items.json 里的**真实字段**，不做名称猜测
///      （装备取 xlsx 的「子类型 / 等级 / 稀有度」，材料类取 PVF 的 [stackable type] 与目录）；
///   3. 维度值按业务顺序排列（职业系 / 材质 / 部位），不用字典序。
///
/// 码表必须与 tools\extract_items.py 里的 USE_* / SRC_* / RARITY 保持一致。
/// </summary>
public static class FacetTable
{
    public const string AllCategory = "全部";

    // ---- 材料/消耗品：用途码（PVF [stackable type] 归纳） ----
    const int UseMaterial = 1, UseConsumable = 2, UseRecipe = 3, UseQuest = 4;
    const int UsePackage = 5, UseEmblem = 6, UseThrow = 7, UseOther = 8;

    static readonly (int Code, string Label)[] UseMap =
    {
        (UseMaterial,   "材料"),
        (UseConsumable, "消耗品/道具"),
        (UseRecipe,     "设计图"),
        (UseQuest,      "任务物品"),
        (UsePackage,    "礼包·盒子"),
        (UseEmblem,     "徽章·附魔"),
        (UseThrow,      "投掷物"),
        (UseOther,      "其它")
    };

    // ---- 材料/消耗品：来源码（PVF 目录归纳） ----
    const int SrcCommon = 1, SrcCash = 2, SrcEvent = 3, SrcQuest = 4;
    const int SrcJob = 5, SrcCard = 6, SrcEmblem = 7;

    static readonly (int Code, string Label)[] SrcMap =
    {
        (SrcCommon, "通用/野外"),
        (SrcCash,   "商城"),
        (SrcEvent,  "活动"),
        (SrcQuest,  "任务"),
        (SrcJob,    "副职业"),
        (SrcCard,   "卡片"),
        (SrcEmblem, "徽章")
    };

    // ---- 武器种类（xlsx 子类型码，按职业系分组） ----
    static readonly (int Code, string Label)[] WeaponSub =
    {
        (10, "短剑"), (20, "太刀"), (30, "钝器"), (40, "巨剑"), (60, "光剑"),
        (70, "手套"), (80, "臂铠"), (90, "爪"), (100, "拳套"), (120, "东方棍"),
        (130, "左轮枪"), (140, "自动手枪"), (150, "步枪"), (160, "手炮"), (170, "手弩"),
        (180, "长矛"), (181, "长棍"), (182, "魔杖"), (183, "法杖"), (184, "扫把"),
        (190, "十字架"), (191, "念珠"), (192, "图腾"), (193, "镰刀"), (194, "战斧"),
        (200, "匕首"), (201, "双剑"), (203, "手杖")
    };

    // ---- 防具：材质 = 子类型码的十位（21 布甲 … 25 板甲）；部位 = 个位（1 上衣 … 5 腰带） ----
    static readonly (int Code, string Label)[] ArmorMat =
    {
        (21, "布甲"), (22, "皮甲"), (23, "轻甲"), (24, "重甲"), (25, "板甲")
    };

    static readonly (int Code, string Label)[] ArmorPart =
    {
        (1, "上衣"), (2, "护肩"), (3, "下装"), (4, "鞋"), (5, "腰带")
    };

    // ---- 首饰部位 ----
    static readonly (int Code, string Label)[] JewelPart =
    {
        (310, "项链"), (320, "手镯"), (330, "戒指"), (340, "称号")
    };

    // ---- 时装部位 ----
    static readonly (int Code, string Label)[] AvatarPart =
    {
        (1010, "头部"), (1020, "发型"), (1030, "脸部"), (1040, "胸部"),
        (1050, "上衣"), (1060, "下装"), (1080, "鞋"), (1090, "皮肤")
    };

    // ---- 稀有度（装备通用） ----
    static readonly (int Code, string Label)[] RarityMap =
    {
        (0, "普通"), (1, "高级"), (2, "稀有"), (3, "神器"), (4, "史诗"), (5, "传说")
    };

    // ---- 等级段（装备通用；86 版最高 80 级） ----
    static readonly (int Lo, int Hi, string Label)[] LevelBands =
    {
        (0, 20, "1-20 级"), (21, 40, "21-40 级"), (41, 60, "41-60 级"),
        (61, 70, "61-70 级"), (71, 80, "71-80 级")
    };

    static readonly Dictionary<string, Facet[]> Table = Build();

    public static IReadOnlyList<Facet> For(string category)
        => category != null && Table.TryGetValue(category, out var f) ? f : Array.Empty<Facet>();

    static Dictionary<string, Facet[]> Build()
    {
        // 精确码匹配（装备子类型）
        static Facet ByCode(string label, (int Code, string Label)[] map, Func<Item, int> get)
            => new(label, map.Select(m => new FacetOption(m.Label, x => get(x) == m.Code)).ToArray());

        // 同码表但要除以 10 再比（防具材质）
        static Facet ByCodeDiv10(string label, (int Code, string Label)[] map, Func<Item, int> get)
            => new(label, map.Select(m => new FacetOption(m.Label, x => get(x) / 10 == m.Code)).ToArray());

        // 同码表但只比个位（防具部位）
        static Facet ByCodeMod10(string label, (int Code, string Label)[] map, Func<Item, int> get)
            => new(label, map.Select(m => new FacetOption(m.Label, x => get(x) % 10 == m.Code)).ToArray());

        var level = new Facet("等级段", LevelBands
            .Select(b => new FacetOption(b.Label, x => x.Level >= b.Lo && x.Level <= b.Hi)).ToArray());

        return new Dictionary<string, Facet[]>
        {
            ["材料/消耗品"] = new[]
            {
                ByCode("用途", UseMap, x => x.Sub),
                ByCode("来源", SrcMap, x => x.From)
            },
            ["武器"] = new[]
            {
                ByCode("武器种类", WeaponSub, x => x.Sub),
                ByCode("稀有度", RarityMap, x => x.Rarity),
                level
            },
            ["防具"] = new[]
            {
                ByCodeDiv10("材质", ArmorMat, x => x.Sub),
                ByCodeMod10("部位", ArmorPart, x => x.Sub),
                ByCode("稀有度", RarityMap, x => x.Rarity)
            },
            ["首饰"] = new[]
            {
                ByCode("部位", JewelPart, x => x.Sub),
                ByCode("稀有度", RarityMap, x => x.Rarity),
                level
            },
            ["时装/装扮"] = new[]
            {
                ByCode("部位", AvatarPart, x => x.Sub),
                ByCode("稀有度", RarityMap, x => x.Rarity)
            }
        };
    }

    /// <summary>列表行尾的维度摘要，让用户一眼看到这条命中在哪几个维度上。</summary>
    public static string Describe(Item it)
    {
        if (it == null) return "";

        switch (it.Cat)
        {
            case "材料/消耗品":
                return $"{Label(UseMap, it.Sub)} · {Label(SrcMap, it.From)}";

            case "武器":
                return $"{Label(WeaponSub, it.Sub)} · {Label(RarityMap, it.Rarity)}{Level(it)}";

            case "防具":
                return $"{Label(ArmorMat, it.Sub / 10)} · {Label(ArmorPart, it.Sub % 10)} · {Label(RarityMap, it.Rarity)}";

            case "首饰":
                return $"{Label(JewelPart, it.Sub)} · {Label(RarityMap, it.Rarity)}{Level(it)}";

            case "时装/装扮":
                return $"{Label(AvatarPart, it.Sub)} · {Label(RarityMap, it.Rarity)}";

            default:
                return it.Cat;
        }
    }

    static string Level(Item it) => it.Level > 0 ? $" · Lv.{it.Level}" : "";

    static string Label((int Code, string Label)[] map, int code)
    {
        foreach (var m in map)
            if (m.Code == code) return m.Label;
        return "未知";
    }
}
