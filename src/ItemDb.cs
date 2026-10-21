using System.Text.Json;

namespace DNF86GM;

public class Item
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Cat { get; set; } = "";

    /// <summary>
    /// 子类型码，语义随类型不同（见 <see cref="FacetTable"/>）：
    /// 装备 = xlsx 子类型枚举（10 短剑 / 211 布甲上衣 / 310 项链 / 1010 头部）；
    /// 材料/消耗品 = 用途码（1 材料 / 2 消耗品 / 3 设计图 …）。
    /// </summary>
    public int Sub { get; set; }

    /// <summary>稀有度码：0 普通 / 1 高级 / 2 稀有 / 3 神器 / 4 史诗 / 5 传说；-1 = 无此字段。</summary>
    public int Rarity { get; set; } = -1;

    /// <summary>装备等级（材料/消耗品无意义）。</summary>
    public int Level { get; set; }

    /// <summary>材料/消耗品的来源码（1 通用/野外 / 2 商城 / 3 活动 …）；装备为 0。</summary>
    public int From { get; set; }
}

/// <summary>
/// 物品库：由 86 版 Script.pvf 直接提取（消耗品 12,529 条）
/// + 服务器 dnf_item_info 导出的装备（9,688 条）合并而成。
/// 每条记录除代码/名称/类型外，还带子类型维度字段（Sub / Rarity / Level / From）。
/// </summary>
public class ItemDb
{
    public readonly List<Item> All = new();
    public readonly List<string> Categories = new();

    public void Load(string path)
    {
        All.Clear();
        Categories.Clear();
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        using var doc = JsonDocument.Parse(fs);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            All.Add(new Item
            {
                Id = e.GetProperty("i").GetInt32(),
                Name = e.GetProperty("n").GetString() ?? "",
                Cat = e.GetProperty("c").GetString() ?? "其它",
                Sub = Num(e, "s"),
                Rarity = Num(e, "r", -1),
                Level = Num(e, "l"),
                From = Num(e, "d")
            });
        }

        Categories.Add(FacetTable.AllCategory);
        Categories.AddRange(All.Select(x => x.Cat).Distinct().OrderBy(x => x));
    }

    /// <summary>缺失字段返回默认值——旧版 items.json（无维度字段）也能正常加载。</summary>
    static int Num(JsonElement e, string key, int def = 0)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : def;

    /// <summary>
    /// 联想搜索：类型过滤 + 维度过滤 + 关键词子串匹配，前缀命中优先、名称短的优先。
    /// 输入纯数字时按物品代码精确/前缀匹配。
    ///
    /// 多个维度之间是 AND：<paramref name="facets"/> 里每个取值都必须命中。
    /// 传 null 或空集合表示不按维度筛选（「全部」类型下就是这样）。
    /// </summary>
    public List<Item> Search(string keyword, string category, int max = 200,
                             IReadOnlyList<FacetOption> facets = null)
    {
        keyword = (keyword ?? "").Trim();
        IEnumerable<Item> src = All;

        if (!string.IsNullOrEmpty(category) && category != FacetTable.AllCategory)
            src = src.Where(x => x.Cat == category);

        if (facets != null)
            foreach (var f in facets)
            {
                if (f == null) continue;
                src = src.Where(f.Match);
            }

        if (keyword.Length == 0)
            return src.Take(max).ToList();

        bool numeric = keyword.All(char.IsDigit);

        if (numeric)
        {
            var exact = src.Where(x => x.Id.ToString() == keyword).ToList();
            if (exact.Count > 0) return exact;
            return src.Where(x => x.Id.ToString().StartsWith(keyword))
                      .OrderBy(x => x.Id).Take(max).ToList();
        }

        var res = src.Where(x => x.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(x => x.Name.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
                     .ThenBy(x => x.Name.Length)
                     .ThenBy(x => x.Id)
                     .Take(max)
                     .ToList();
        return res;
    }

    public Item FindById(int id) => All.FirstOrDefault(x => x.Id == id);
}
