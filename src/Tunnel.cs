using Renci.SshNet;

namespace DNF86GM;

/// <summary>
/// SSH 本地端口转发：把 127.0.0.1:LocalPort 转发到服务器的 127.0.0.1:RemotePort。
/// 这样本机不需要对外开放 MySQL 端口，也不用改服务器防火墙。
/// </summary>
public class SshTunnel : IDisposable
{
    SshClient _client;
    ForwardedPortLocal _port;

    public bool IsRunning => _port != null && _port.IsStarted;
    /// <summary>本次实际用的是哪份私钥（方便排查）。</summary>
    public string KeySource { get; private set; } = "";
    public string LastError { get; private set; } = "";

    public bool Start(AppConfig cfg)
    {
        Stop();
        LastError = "";
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

            _port = new ForwardedPortLocal("127.0.0.1", (uint)cfg.LocalPort, "127.0.0.1", (uint)cfg.RemotePort);
            _client.AddForwardedPort(_port);
            _port.Start();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
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
}
