using System.Net;
using System.Net.Sockets;
using Renci.SshNet;

namespace DNF86GM;

/// <summary>
/// SSH 本地端口转发：把 127.0.0.1:LocalPort 转发到服务器的 127.0.0.1:RemotePort。
/// 这样本机不需要对外开放 MySQL 端口，也不用改服务器防火墙。
///
/// 关于端口：配置里的 LocalPort 只是「首选」。如果它已被占用（上次异常退出留下的监听、
/// 另一个本程序实例、或本机本来就装了 MySQL/其它工具），Start 会自动往后找可用端口，
/// 实际使用的端口记在 <see cref="LocalPort"/>，调用方必须用它去连库。
/// 直接绑死首选端口会抛 WinSock 10048「通常每个套接字地址只允许使用一次」。
/// </summary>
public class SshTunnel : IDisposable
{
    SshClient _client;
    ForwardedPortLocal _port;

    public bool IsRunning => _port != null && _port.IsStarted;
    /// <summary>本次实际用的哪份私钥（方便排查）。</summary>
    public string KeySource { get; private set; } = "";
    /// <summary>本次实际绑定的本地端口（可能与配置里的 LocalPort 不同）。</summary>
    public int LocalPort { get; private set; }
    /// <summary>成功但需要提醒用户的情况（例如端口被占用已自动避让）。</summary>
    public string LastNote { get; private set; } = "";
    public string LastError { get; private set; } = "";

    public bool Start(AppConfig cfg)
    {
        Stop();
        LastError = "";
        LastNote = "";
        try
        {
            // 仓库版不带任何默认服务器地址，空值直接给出可读提示，不要让 SSH.NET 抛底层异常
            if (string.IsNullOrWhiteSpace(cfg.SshHost))
            {
                LastError = "还没有配置服务器地址（右键顶栏 → 连接设置）";
                return false;
            }

            // 私钥由 KeyStore 统一解析：配置路径 → 程序目录 key.txt → 用户 .ssh → 内置
            var info = KeyStore.Resolve(cfg);
            if (info.Kind == "")
            {
                LastError = "找不到 SSH 私钥（把私钥文件改名为 key.txt 放到程序目录即可）";
                return false;
            }
            KeySource = info.ToString();

            var key = KeyStore.Load(cfg);
            var auth = new PrivateKeyAuthenticationMethod(cfg.SshUser, key);
            var ci = new ConnectionInfo(cfg.SshHost, cfg.SshPort, cfg.SshUser, auth)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            _client = new SshClient(ci);
            _client.Connect();

            // 逐个试候选端口：先探测是否空闲，再真正监听；仍撞车（极小概率的竞争）就换下一个。
            Exception lastBind = null;
            foreach (var p in Candidates(cfg.LocalPort))
            {
                if (!IsPortFree(p)) continue;

                var fp = new ForwardedPortLocal("127.0.0.1", (uint)p, "127.0.0.1", (uint)cfg.RemotePort);
                try
                {
                    _client.AddForwardedPort(fp);
                    fp.Start();
                    _port = fp;
                    LocalPort = p;
                    if (p != cfg.LocalPort)
                        LastNote = $"本地端口 {cfg.LocalPort} 被占用，已自动改用 {p}";
                    return true;
                }
                catch (Exception ex)
                {
                    lastBind = ex;
                    try { _client.RemoveForwardedPort(fp); } catch { }
                    try { fp.Dispose(); } catch { }

                    // 只有「端口被占用」才继续换端口；认证/网络等错误直接上抛
                    if (!IsAddressInUse(ex)) throw;
                }
            }

            LastError = $"本地端口 {cfg.LocalPort} 及后续 20 个端口都被占用" +
                        (lastBind == null ? "" : $"（{lastBind.Message}）") +
                        "。请在「连接设置」里把本地端口改成一个没被占用的值。";
            Stop();
            return false;
        }
        catch (Exception ex)
        {
            LastError = IsAddressInUse(ex)
                ? $"本地端口 {cfg.LocalPort} 已被占用（WinSock 10048）。" +
                  "多半是上一次的 DNF86GM 还没退出，或本机有别的程序占用该端口；" +
                  "关掉它或在「连接设置」里换一个本地端口。"
                : ex.Message;
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        try { _port?.Stop(); } catch { }
        try { _port?.Dispose(); } catch { }
        _port = null;
        try { if (_client != null && _client.IsConnected) _client.Disconnect(); } catch { }
        try { _client?.Dispose(); } catch { }
        _client = null;
    }

    public void Dispose() => Stop();

    // ==================================================================
    // 端口可用性探测
    // ==================================================================

    /// <summary>首选端口 + 其后 20 个（够覆盖常见的 3307/3308 冲突场景）。</summary>
    static IEnumerable<int> Candidates(int preferred)
    {
        if (preferred > 0 && preferred <= 65535) yield return preferred;
        for (int i = 1; i <= 20; i++)
        {
            int p = preferred + i;
            if (p > 0 && p <= 65535) yield return p;
        }
    }

    /// <summary>端口能不能在 127.0.0.1 上绑定（只探测，不占用）。</summary>
    public static bool IsPortFree(int port)
    {
        if (port <= 0 || port > 65535) return false;
        try
        {
            var l = new TcpListener(IPAddress.Loopback, port);
            l.Start();
            l.Stop();
            return true;
        }
        catch { return false; }
    }

    /// <summary>挑一个可用端口：优先首选值，其次往后 20 个，最后交给系统随机分配。</summary>
    public static int PickFreePort(int preferred)
    {
        foreach (var p in Candidates(preferred))
            if (IsPortFree(p)) return p;

        try
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }
        catch { return preferred; }
    }

    /// <summary>判断异常链里是否是 WinSock 10048（AddressAlreadyInUse）。</summary>
    static bool IsAddressInUse(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is SocketException se &&
                (se.SocketErrorCode == SocketError.AddressAlreadyInUse || se.ErrorCode == 10048))
                return true;

            var m = e.Message ?? "";
            if (m.Contains("10048") ||
                m.Contains("只允许使用一次") ||
                m.IndexOf("only one usage", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }
}
