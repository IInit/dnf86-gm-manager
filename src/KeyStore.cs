using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;

namespace DNF86GM;

/// <summary>
/// SSH 私钥解析：让程序在「换台电脑」后也能自己找到可用密钥。
/// 查找顺序：
///   1. config.json 里指定的路径（存在才用）
///   2. exe 同目录的 key.txt
///   3. exe 同目录的任意 id_* / *.key 文件
///   4. 用户目录 ~/.ssh/id_ed25519、id_ed25519_sk、id_rsa
///   5. 编译进程序集的内嵌密钥（如果打了 /p:EmbedKey=true）
/// </summary>
public static class KeyStore
{
    static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

    public sealed record KeyInfo(string Source, string Path, string Kind)
    {
        public override string ToString() =>
            Kind == "内置" ? Source : $"{Source}（{Path}）";
    }

    /// <summary>只做路径解析，不读文件内容（供界面显示用）。</summary>
    public static KeyInfo Resolve(AppConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.SshKeyPath) && File.Exists(cfg.SshKeyPath))
            return new KeyInfo("配置中指定", cfg.SshKeyPath, "文件");

        var local = System.IO.Path.Combine(BaseDir, "key.txt");
        if (File.Exists(local))
            return new KeyInfo("程序目录 key.txt", local, "文件");

        try
        {
            foreach (var f in Directory.GetFiles(BaseDir, "*.key").Concat(Directory.GetFiles(BaseDir, "id_*")))
                return new KeyInfo("程序目录密钥文件", f, "文件");
        }
        catch { }

        foreach (var name in new[] { "id_ed25519", "id_ecdsa", "id_rsa" })
        {
            var p = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", name);
            if (File.Exists(p)) return new KeyInfo("用户目录 .ssh", p, "文件");
        }

        if (EmbeddedName() != null)
            return new KeyInfo("程序内置密钥", "", "内置");

        return new KeyInfo("未找到", "", "");
    }

    static string EmbeddedName()
    {
        var asm = Assembly.GetExecutingAssembly();
        return asm.GetManifestResourceNames()
                  .FirstOrDefault(n => n.EndsWith("key.txt", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>加载私钥；找不到可用密钥时抛异常，信息里写清怎么补。</summary>
    public static PrivateKeyFile Load(AppConfig cfg)
    {
        var info = Resolve(cfg);

        if (info.Kind == "文件")
            return new PrivateKeyFile(info.Path);

        if (info.Kind == "内置")
        {
            var asm = Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream(EmbeddedName()!)!;
            return new PrivateKeyFile(s);
        }

        throw new FileNotFoundException(
            "找不到 SSH 私钥。请把私钥文件改名为 key.txt 放到程序目录（和 DNF86GM.exe 同级），" +
            "或在连接设置里填写私钥完整路径。");
    }
}

/// <summary>密码杂项（设置页访问密码）。</summary>
public static class Pwd
{
    public const string Default = "dnf86";

    public static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s ?? "")));

    public static bool Check(string storedHash, string input)
    {
        var h = Hash(input);
        return string.IsNullOrEmpty(storedHash)
            ? h == Hash(Default)
            : string.Equals(h, storedHash, StringComparison.OrdinalIgnoreCase);
    }
}
