using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace DNF86GM;

/// <summary>程序配置，保存在 exe 同目录 config.json</summary>
public class AppConfig
{
    // ---- SSH 隧道（本机没有直连数据库时使用）----
    // 仓库里不保存任何真实服务器地址，首次运行请在「连接设置」里填写自己的主机。
    public bool UseTunnel { get; set; } = true;
    public string SshHost { get; set; } = "";
    public int SshPort { get; set; } = 22;
    public string SshUser { get; set; } = "root";
    public string SshKeyPath { get; set; } = "";
    public int LocalPort { get; set; } = 3307;
    public int RemotePort { get; set; } = 3000;   // 容器 MySQL 映射到宿主机的端口

    // ---- 数据库（默认连本地隧道）----
    public string DbHost { get; set; } = "127.0.0.1";
    public int DbPort { get; set; } = 3307;
    public string DbUser { get; set; } = "root";
    // 口令不写进代码，首次运行请在「连接设置」里填自己的库密码（只存到本地 config.json）。
    public string DbPassword { get; set; } = "";

    // ---- 其它 ----
    public string SenderName { get; set; } = "GM";
    public string LetterText { get; set; } = "GM 赠礼，请查收。";

    /// <summary>「连接设置」的访问密码（SHA-256）；空 = 用默认密码 dnf86。</summary>
    public string SettingsPasswordHash { get; set; } = "";

    static readonly string PathFile =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

    static readonly JsonSerializerOptions JsonOpt = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(PathFile))
            {
                var txt = File.ReadAllText(PathFile, Encoding.UTF8);
                var cfg = JsonSerializer.Deserialize<AppConfig>(txt, JsonOpt);
                if (cfg != null) return cfg;
            }
        }
        catch { /* 配置损坏时回落到默认值 */ }
        var def = new AppConfig();
        def.SshKeyPath = GuessKeyPath();
        def.Save();
        return def;
    }

    public void Save()
    {
        // 不带 BOM：别的机器上用记事本/编辑器改这个文件时更省事
        try
        {
            File.WriteAllText(PathFile, JsonSerializer.Serialize(this, JsonOpt),
                new UTF8Encoding(false));
        }
        catch { }
    }

    /// <summary>自动找可用私钥（程序目录优先，其次用户 .ssh）。</summary>
    public static string GuessKeyPath() => KeyStore.Resolve(new AppConfig()).Path;

    /// <summary>深拷贝：设置对话框上改的是副本，点「保存」才回写。</summary>
    public AppConfig Clone()
    {
        var json = JsonSerializer.Serialize(this, JsonOpt);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOpt) ?? new AppConfig();
    }

    public void CopyFrom(AppConfig o)
    {
        UseTunnel = o.UseTunnel;
        SshHost = o.SshHost;
        SshPort = o.SshPort;
        SshUser = o.SshUser;
        SshKeyPath = o.SshKeyPath;
        LocalPort = o.LocalPort;
        RemotePort = o.RemotePort;
        DbHost = o.DbHost;
        DbPort = o.DbPort;
        DbUser = o.DbUser;
        DbPassword = o.DbPassword;
        SenderName = o.SenderName;
        LetterText = o.LetterText;
        SettingsPasswordHash = o.SettingsPasswordHash;
    }

    public string ConnStr(int? port = null) =>
        $"Server={DbHost};Port={port ?? DbPort};User ID={DbUser};Password={DbPassword};" +
        "Connection Timeout=8;Allow User Variables=true;" +
        "Pooling=false;Treat Tiny As Boolean=false;";
}
