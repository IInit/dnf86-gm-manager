using System.Text;

namespace DNF86GM;

/// <summary>
/// 台服 DNF 库的字符集是 latin1（MySQL 元数据），但实际存的是 UTF-8 字节。
/// 所以读出来要「按 latin1 取字节 → 按 UTF-8 解码」，写进去要反过来。
/// 这是之前导出物品表遇到「双重编码」的根因。
/// </summary>
public static class Enc
{
    public static string L2U(string s) =>
        string.IsNullOrEmpty(s) ? s : Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(s));

    public static string U2L(string s) =>
        string.IsNullOrEmpty(s) ? s : Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(s));

    /// <summary>容错：若 UTF-8 解码出现替换符，说明本来就是正常文本，直接返回原值</summary>
    public static string L2U_Safe(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var r = L2U(s);
        return r.Contains('\uFFFD') ? s : r;
    }
}
