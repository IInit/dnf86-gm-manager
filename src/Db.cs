using MySqlConnector;
using System.Text;

namespace DNF86GM;

public class CharRow
{
    public int CharacNo { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int Job { get; set; }
    public int GrowType { get; set; }
    public long Money { get; set; }
    public long Coin { get; set; }
    public long Cera { get; set; }
    public string Account { get; set; } = "";

    public string JobName => Jobs.Name(Job, GrowType);

    public override string ToString() =>
        $"{Name}  Lv.{Level}  {JobName}  ({Account})";
}

public static class Jobs
{
    static readonly string[] Main =
    {
        "鬼剑士", "格斗家", "神枪手", "魔法师", "圣职者", "暗夜使者",
        "缔造者", "男格斗", "男神枪手", "男魔法师", "女神枪手", "男圣职者",
        "女鬼剑士", "女圣职者", "守护者", "魔枪士", "枪剑士"
    };

    public static string Name(int job, int grow)
    {
        string m = (job >= 0 && job < Main.Length) ? Main[job] : $"未知({job})";
        return grow > 0 ? $"{m}/{GrowName(job, grow)}" : m;
    }

    static string GrowName(int job, int g)
    {
        string[] t = job switch
        {
            0 => new[] { "", "剑魂", "鬼泣", "狂战士", "阿修罗" },
            1 => new[] { "", "气功师", "散打", "街霸", "柔道家" },
            2 => new[] { "", "漫游枪手", "枪炮师", "机械师", "弹药专家" },
            3 => new[] { "", "元素师", "召唤师", "战斗法师", "魔道学者" },
            4 => new[] { "", "圣骑士", "蓝拳圣使", "驱魔师", "复仇者" },
            _ => null
        };
        if (t != null && g < t.Length && !string.IsNullOrEmpty(t[g])) return t[g];
        return $"转职{g}";
    }
}

public class Db
{
    public AppConfig Cfg { get; }

    public Db(AppConfig cfg) => Cfg = cfg;

    // ------------------------------------------------------------------
    // 台服库的 varchar 是「双重编码」：
    //   原始 UTF-8 字节 → 被按 cp1252 解码 → 再按 UTF-8 存进库。
    // 例：阿修罗 (E9 98 BF …) 在库里是 C3A9 CB9C C2BF …
    // 所以读要反着解两遍，写也要正着编两遍，否则游戏读到的是乱码。
    // ------------------------------------------------------------------
    static readonly Encoding Cp1252 = GetCp1252();

    static Encoding GetCp1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    static string Hex(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var inner = Encoding.UTF8.GetBytes(s);      // 真实字节
        var mid = Cp1252.GetString(inner);          // 模拟服务端的错误解码
        var stored = Encoding.UTF8.GetBytes(mid);   // 库里实际存的字节
        return Convert.ToHexString(stored);
    }

    static string UnHex(string h)
    {
        if (string.IsNullOrEmpty(h)) return "";
        var stored = Convert.FromHexString(h);
        var mid = Encoding.UTF8.GetString(stored);  // é˜¿ä¿®ç½—
        try
        {
            var inner = Cp1252.GetBytes(mid);       // E9 98 BF …
            return Encoding.UTF8.GetString(inner);  // 阿修罗
        }
        catch
        {
            return mid;                             // 纯 ASCII 等情况原样返回
        }
    }

    MySqlConnection Open()
    {
        var c = new MySqlConnection(Cfg.ConnStr());
        c.Open();
        return c;
    }

    public string TestConnection()
    {
        using var c = Open();
        using var cmd = new MySqlCommand("select version()", c);
        return "MySQL " + Convert.ToString(cmd.ExecuteScalar()) + " 已连接";
    }

    // ------------------------------------------------------------------
    // 角色
    // ------------------------------------------------------------------

    public List<CharRow> SearchChars(string keyword, int limit = 300)
    {
        var list = new List<CharRow>();
        using var c = Open();

        var sql = $@"
SELECT c.charac_no, HEX(c.charac_name), c.lev, c.job, c.grow_type,
       IFNULL(i.money,0), IFNULL(i.coin,0), HEX(a.accountname), IFNULL(b.cera,0)
FROM taiwan_cain.charac_info c
JOIN d_taiwan.accounts a ON a.UID = c.m_id
LEFT JOIN taiwan_cain_2nd.inventory i ON i.charac_no = c.charac_no
LEFT JOIN taiwan_billing.cash_cera b ON b.account = a.accountname
WHERE (@kw = '' OR HEX(c.charac_name) LIKE @like OR HEX(a.accountname) LIKE @like)
ORDER BY c.charac_no
LIMIT {limit}";

        using var cmd = new MySqlCommand(sql, c);
        var hexKw = Hex(keyword.Trim()).ToUpperInvariant();
        cmd.Parameters.AddWithValue("@kw", hexKw);
        // UTF-8 字节的十六进制串做 LIKE：%E5%A4%8D%E6%B4%BB%
        cmd.Parameters.AddWithValue("@like", "%" + hexKw + "%");

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new CharRow
            {
                CharacNo = r.GetInt32(0),
                Name = UnHex(r.GetString(1)),
                Level = r.GetInt32(2),
                Job = r.GetInt32(3),
                GrowType = r.GetInt32(4),
                Money = r.GetInt64(5),
                Coin = r.GetInt64(6),
                Account = UnHex(r.GetString(7)),
                Cera = r.GetInt64(8)
            });
        }
        return list;
    }

    public void UpdateMoney(int characNo, long money)
    {
        using var c = Open();
        using var cmd = new MySqlCommand(
            "update taiwan_cain_2nd.inventory set money=@v where charac_no=@c", c);
        cmd.Parameters.AddWithValue("@v", money);
        cmd.Parameters.AddWithValue("@c", characNo);
        if (cmd.ExecuteNonQuery() == 0)
        {
            using var ins = new MySqlCommand(
                "insert into taiwan_cain_2nd.inventory(charac_no,money,coin,inventory,equipslot) " +
                "values(@c,@v,0,'','')", c);
            ins.Parameters.AddWithValue("@v", money);
            ins.Parameters.AddWithValue("@c", characNo);
            ins.ExecuteNonQuery();
        }
    }

    public void UpdateCoin(int characNo, long coin)
    {
        using var c = Open();
        using var cmd = new MySqlCommand(
            "update taiwan_cain_2nd.inventory set coin=@v where charac_no=@c", c);
        cmd.Parameters.AddWithValue("@v", coin);
        cmd.Parameters.AddWithValue("@c", characNo);
        cmd.ExecuteNonQuery();
    }

    public void UpdateCera(string account, long cera)
    {
        using var c = Open();
        using var cmd = new MySqlCommand(
            "update taiwan_billing.cash_cera set cera=@v, mod_date=NOW() " +
            "where account=UNHEX(@a)", c);
        cmd.Parameters.AddWithValue("@v", cera);
        cmd.Parameters.AddWithValue("@a", Hex(account));
        if (cmd.ExecuteNonQuery() == 0)
        {
            using var ins = new MySqlCommand(
                "insert into taiwan_billing.cash_cera(account,cera,mod_tran,mod_date,reg_date) " +
                "values(UNHEX(@a),@v,0,NOW(),NOW())", c);
            ins.Parameters.AddWithValue("@v", cera);
            ins.Parameters.AddWithValue("@a", Hex(account));
            ins.ExecuteNonQuery();
        }
    }

    public void UpdateLevel(int characNo, int level)
    {
        using var c = Open();
        using var cmd = new MySqlCommand(
            "update taiwan_cain.charac_info set lev=@v where charac_no=@c", c);
        cmd.Parameters.AddWithValue("@v", Math.Clamp(level, 1, 86));
        cmd.Parameters.AddWithValue("@c", characNo);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------
    // 邮件发物品
    // ------------------------------------------------------------------

    /// <summary>
    /// 写 letter + postal。数量放 add_info，锻造放 seperate_upgrade，
    /// 红字属性放 amplify_option / amplify_value。
    /// </summary>
    /// <summary>
    /// 发一封邮件。itemId 传 0 表示「只发金币」，此时 count 会被忽略。
    /// gold 写入 postal.gold，游戏里收到就是随信附带的游戏币。
    /// </summary>
    public int SendItem(int characNo, int itemId, int count,
                        int strengthen, int forge, int redOption, int redValue,
                        long gold, string sender, string letterText)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();

        int letterId;
        using (var cmd = new MySqlCommand(
            "insert into taiwan_cain_2nd.letter(charac_no,send_charac_no,send_charac_name,letter_text,reg_date,stat) " +
            "values(@c,0,UNHEX(@s),UNHEX(@t),NOW(),1); select last_insert_id();", c, tx))
        {
            cmd.Parameters.AddWithValue("@c", characNo);
            cmd.Parameters.AddWithValue("@s", Hex(string.IsNullOrWhiteSpace(sender) ? "GM" : sender.Trim()));
            cmd.Parameters.AddWithValue("@t", Hex(letterText ?? ""));
            letterId = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // gold 列是 int unsigned，超范围会被 MySQL 截断，这里先夹紧
        long g = Math.Clamp(gold, 0, uint.MaxValue);
        // 纯金币邮件没有物品，add_info 必须是 0（写 1 会让客户端去找 item_id=0 的物品）
        int cnt = itemId > 0 ? Math.Max(1, count) : 0;

        using (var cmd = new MySqlCommand(
            "insert into taiwan_cain_2nd.postal(occ_time,send_charac_no,send_charac_name,receive_charac_no," +
            "item_id,add_info,upgrade,amplify_option,amplify_value,letter_id,seperate_upgrade,seal_flag,gold) " +
            "values(NOW(),0,UNHEX(@s),@c,@item,@cnt,@up,@ropt,@rval,@lid,@forge,0,@gold);", c, tx))
        {
            cmd.Parameters.AddWithValue("@s", Hex(string.IsNullOrWhiteSpace(sender) ? "GM" : sender.Trim()));
            cmd.Parameters.AddWithValue("@c", characNo);
            cmd.Parameters.AddWithValue("@item", itemId);
            cmd.Parameters.AddWithValue("@cnt", cnt);
            cmd.Parameters.AddWithValue("@up", strengthen);
            cmd.Parameters.AddWithValue("@ropt", redOption);
            cmd.Parameters.AddWithValue("@rval", redValue);
            cmd.Parameters.AddWithValue("@lid", letterId);
            cmd.Parameters.AddWithValue("@forge", forge);
            cmd.Parameters.AddWithValue("@gold", g);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return letterId;
    }
}
