using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace DNF86GM;

/// <summary>
/// 「连接设置」对话框。原来它是主界面上的一个页签，谁打开程序都能看到
/// SSH 私钥路径和数据库 root 密码，所以改成：默认隐藏，输入访问密码后才弹出。
/// </summary>
public class SettingsForm : Form
{
    readonly AppConfig _cfg;      // 编辑的是副本，点保存才回写
    TextBox _txtSshHost, _txtSshPort, _txtSshUser, _txtSshKey, _txtLocalPort, _txtRemotePort;
    TextBox _txtDbHost, _txtDbPort, _txtDbUser, _txtDbPwd;
    TextBox _txtNewPwd, _txtNewPwd2;
    CheckBox _chkTunnel, _chkShowPwd;
    Label _lblKeyState;

    public AppConfig Result => _cfg;

    public SettingsForm(AppConfig cfg)
    {
        _cfg = cfg.Clone();

        Text = "连接设置";
        ClientSize = new Size(620, 624);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;

        // ---------------- SSH ----------------
        var gbSsh = new GroupBox
        {
            Text = "SSH 隧道（服务器 MySQL 未对公网开放，靠隧道访问）",
            Location = new Point(12, 10),
            Size = new Size(596, 254)
        };
        _chkTunnel = new CheckBox
        {
            Text = "启用 SSH 隧道",
            Location = new Point(12, 22),
            AutoSize = true,
            Checked = _cfg.UseTunnel
        };
        gbSsh.Controls.Add(_chkTunnel);

        int y = 52;
        _txtSshHost = Field(gbSsh, "服务器地址", ref y, _cfg.SshHost, 200);
        _txtSshPort = Field(gbSsh, "SSH 端口", ref y, _cfg.SshPort.ToString(), 90);
        _txtSshUser = Field(gbSsh, "SSH 账号", ref y, _cfg.SshUser, 160);

        gbSsh.Controls.Add(new Label { Text = "私钥文件", Location = new Point(12, y + 4), AutoSize = true });
        _txtSshKey = new TextBox { Location = new Point(100, y), Width = 380, Text = _cfg.SshKeyPath };
        _txtSshKey.TextChanged += (s, e) => ShowKeyState();
        gbSsh.Controls.Add(_txtSshKey);
        var btnBrowse = new Button { Text = "浏览…", Location = new Point(486, y - 1), Width = 96 };
        btnBrowse.Click += (s, e) => BrowseKey();
        gbSsh.Controls.Add(btnBrowse);
        y += 32;

        _lblKeyState = new Label
        {
            Location = new Point(100, y),
            AutoSize = false,
            Size = new Size(482, 36),
            ForeColor = Color.DimGray
        };
        gbSsh.Controls.Add(_lblKeyState);
        y += 36;

        _txtLocalPort = Field(gbSsh, "本地端口", ref y, _cfg.LocalPort.ToString(), 90);
        _txtRemotePort = Field(gbSsh, "远端 MySQL 端口", ref y, _cfg.RemotePort.ToString(), 90);

        // ---------------- DB ----------------
        var gbDb = new GroupBox
        {
            Text = "数据库",
            Location = new Point(12, 272),
            Size = new Size(596, 170)
        };
        int yy = 26;
        _txtDbHost = Field(gbDb, "地址", ref yy, _cfg.DbHost, 200);
        _txtDbPort = Field(gbDb, "端口", ref yy, _cfg.DbPort.ToString(), 90);
        _txtDbUser = Field(gbDb, "账号", ref yy, _cfg.DbUser, 160);

        gbDb.Controls.Add(new Label { Text = "密码", Location = new Point(12, yy + 4), AutoSize = true });
        _txtDbPwd = new TextBox
        {
            Location = new Point(100, yy),
            Width = 260,
            Text = _cfg.DbPassword,
            UseSystemPasswordChar = true
        };
        gbDb.Controls.Add(_txtDbPwd);
        _chkShowPwd = new CheckBox { Text = "显示", Location = new Point(370, yy + 2), AutoSize = true };
        _chkShowPwd.CheckedChanged += (s, e) => _txtDbPwd.UseSystemPasswordChar = !_chkShowPwd.Checked;
        gbDb.Controls.Add(_chkShowPwd);

        // ---------------- 访问密码 ----------------
        var gbPwd = new GroupBox
        {
            Text = "设置页访问密码（防止别人打开这个窗口）",
            Location = new Point(12, 450),
            Size = new Size(596, 92)
        };
        gbPwd.Controls.Add(new Label
        {
            // 已改过密码时不能再写「当前默认密码 dnf86」，否则会误导。
            Text = string.IsNullOrEmpty(_cfg.SettingsPasswordHash)
                ? "留空表示不修改；当前使用默认密码 dnf86"
                : "留空表示不修改；当前使用自定义密码",
            Location = new Point(12, 22),
            AutoSize = true,
            ForeColor = Color.Gray
        });
        gbPwd.Controls.Add(new Label { Text = "新密码", Location = new Point(12, 54), AutoSize = true });
        _txtNewPwd = new TextBox { Location = new Point(76, 50), Width = 160, UseSystemPasswordChar = true };
        gbPwd.Controls.Add(_txtNewPwd);
        gbPwd.Controls.Add(new Label { Text = "再输一次", Location = new Point(256, 54), AutoSize = true });
        _txtNewPwd2 = new TextBox { Location = new Point(332, 50), Width = 160, UseSystemPasswordChar = true };
        gbPwd.Controls.Add(_txtNewPwd2);

        // ---------------- 按钮 ----------------
        var btnOk = new Button { Text = "保存", Location = new Point(12, 556), Width = 110, Height = 34 };
        btnOk.Click += (s, e) => Save();
        var btnCancel = new Button
        {
            Text = "取消",
            Location = new Point(132, 556),
            Width = 90,
            Height = 34,
            DialogResult = DialogResult.Cancel
        };

        Controls.Add(gbSsh);
        Controls.Add(gbDb);
        Controls.Add(gbPwd);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ShowKeyState();
    }

    TextBox Field(Control parent, string label, ref int y, string val, int width)
    {
        parent.Controls.Add(new Label { Text = label, Location = new Point(12, y + 4), AutoSize = true });
        var t = new TextBox { Location = new Point(100, y), Width = width, Text = val };
        parent.Controls.Add(t);
        y += 34;
        return t;
    }

    void ShowKeyState()
    {
        var probe = _cfg.Clone();
        probe.SshKeyPath = _txtSshKey.Text.Trim();
        var info = KeyStore.Resolve(probe);
        if (info.Kind == "")
        {
            _lblKeyState.ForeColor = Color.Firebrick;
            _lblKeyState.Text = "找不到可用私钥：把私钥文件改名为 key.txt 放到程序目录，或点「浏览…」选择。";
        }
        else
        {
            _lblKeyState.ForeColor = Color.SeaGreen;
            _lblKeyState.Text = "实际使用：" + info + "\r\n（此栏留空则按 程序目录 key.txt → 用户 .ssh 顺序自动查找）";
        }
    }

    void BrowseKey()
    {
        using var d = new OpenFileDialog { Title = "选择 SSH 私钥", Filter = "所有文件|*.*" };
        if (d.ShowDialog(this) == DialogResult.OK)
        {
            _txtSshKey.Text = d.FileName;
            ShowKeyState();
        }
    }

    void Save()
    {
        if (!string.IsNullOrEmpty(_txtNewPwd.Text) || !string.IsNullOrEmpty(_txtNewPwd2.Text))
        {
            if (_txtNewPwd.Text.Length < 4)
            {
                MessageBox.Show(this, "新密码至少 4 位", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_txtNewPwd.Text != _txtNewPwd2.Text)
            {
                MessageBox.Show(this, "两次输入的新密码不一致", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _cfg.SettingsPasswordHash = Pwd.Hash(_txtNewPwd.Text);
        }

        _cfg.UseTunnel = _chkTunnel.Checked;
        _cfg.SshHost = _txtSshHost.Text.Trim();
        _cfg.SshPort = ParseInt(_txtSshPort.Text, 22);
        _cfg.SshUser = _txtSshUser.Text.Trim();
        _cfg.SshKeyPath = _txtSshKey.Text.Trim();
        _cfg.LocalPort = ParseInt(_txtLocalPort.Text, 3307);
        _cfg.RemotePort = ParseInt(_txtRemotePort.Text, 3000);
        _cfg.DbHost = _txtDbHost.Text.Trim();
        _cfg.DbPort = ParseInt(_txtDbPort.Text, 3307);
        _cfg.DbUser = _txtDbUser.Text.Trim();
        _cfg.DbPassword = _txtDbPwd.Text;

        DialogResult = DialogResult.OK;
        Close();
    }

    static int ParseInt(string s, int def) => int.TryParse(s, out var v) ? v : def;
}

/// <summary>
/// 输入访问密码的小弹窗。除了校验密码，还能就地改密码：
/// 点「修改密码…」展开下方配置区，用上方输入框里的<strong>当前密码</strong>验证身份，
/// 校验通过后直接把新密码的 SHA-256 写进 config.json（立即生效，不必先进设置页）。
/// 忘记密码时的兜底：把 config.json 里的 SettingsPasswordHash 清空，即回到默认 dnf86。
/// </summary>
public class PasswordForm : Form
{
    // ---- 布局常量：收起只有「输入 + 提示 + 按钮」，展开才长出改密区 ----
    const int W = 400;            // 窗宽（比旧版 340 宽，容纳灰字提示与改密行）
    const int HCollapsed = 160;   // 收起高度
    const int HExpanded = 316;    // 展开高度
    const int EditTop = 160;      // 改密区起始 y（正好在按钮行下方）

    readonly AppConfig _cfg;
    readonly TextBox _txt;              // 当前密码
    readonly TextBox _txtNew, _txtNew2; // 新密码 / 确认
    readonly Button _btnEdit;           // 修改密码… / 收起
    readonly Panel _pwdArea;            // 改密区容器
    readonly Label _lblHint, _lblState;
    bool _expanded;

    /// <summary>弹窗里的密码输入值。</summary>
    public string Password => _txt.Text;

    /// <summary>本次弹窗内已成功改过密码 —— 调用方可视为已通过身份校验。</summary>
    public bool PasswordChanged { get; private set; }

    /// <summary>改密区当前是否展开（自检用；不能用 Control.Visible 判断，父窗未 Show 时它恒为 false）。</summary>
    public bool IsExpanded => _expanded;

    public PasswordForm(string title, AppConfig cfg)
    {
        _cfg = cfg;

        Text = title;
        ClientSize = new Size(W, HCollapsed);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.White;

        // ---- 当前密码 ----
        Controls.Add(new Label { Text = "请输入访问密码：", Location = new Point(18, 20), AutoSize = true });

        _txt = new TextBox
        {
            Location = new Point(18, 46),
            Width = 364,
            UseSystemPasswordChar = true
        };
        // 重新输入时抹掉上一次的错误提示
        _txt.TextChanged += (s, e) => SetState("", Color.DimGray);
        Controls.Add(_txt);

        // ---- 灰色提示行（用户要的就是这行）----
        _lblHint = new Label
        {
            Location = new Point(18, 78),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei UI", 8.25f)
        };
        Controls.Add(_lblHint);

        // ---- 底部按钮行 ----
        _btnEdit = new Button { Text = "修改密码…", Location = new Point(18, 104), Width = 112, Height = 32 };
        _btnEdit.Click += (s, e) => ToggleEdit();
        Controls.Add(_btnEdit);

        var ok = new Button
        {
            Text = "确定",
            Location = new Point(200, 104),
            Width = 90,
            Height = 32,
            DialogResult = DialogResult.OK
        };
        var cancel = new Button
        {
            Text = "取消",
            Location = new Point(296, 104),
            Width = 86,
            Height = 32,
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(ok);
        Controls.Add(cancel);

        // ---- 改密区（默认收起）----
        _pwdArea = new Panel
        {
            Location = new Point(0, EditTop),
            Size = new Size(W, HExpanded - EditTop),
            Visible = false
        };
        // 1px 分隔线，用 Panel 画比 Label 边框稳
        _pwdArea.Controls.Add(new Panel
        {
            Location = new Point(18, 6),
            Size = new Size(364, 1),
            BackColor = Color.Gainsboro
        });
        _pwdArea.Controls.Add(new Label
        {
            Text = "修改访问密码（先用上方输入的当前密码验证身份）",
            Location = new Point(18, 14),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei UI", 8.25f)
        });

        _pwdArea.Controls.Add(new Label { Text = "新密码", Location = new Point(18, 46), AutoSize = true });
        _txtNew = new TextBox
        {
            Location = new Point(86, 42),
            Width = 240,
            UseSystemPasswordChar = true
        };
        _txtNew.KeyDown += EnterSaves;
        _pwdArea.Controls.Add(_txtNew);

        _pwdArea.Controls.Add(new Label { Text = "再输一次", Location = new Point(18, 78), AutoSize = true });
        _txtNew2 = new TextBox
        {
            Location = new Point(86, 74),
            Width = 240,
            UseSystemPasswordChar = true
        };
        _txtNew2.KeyDown += EnterSaves;
        _pwdArea.Controls.Add(_txtNew2);

        var btnSave = new Button { Text = "保存新密码", Location = new Point(18, 106), Width = 112, Height = 30 };
        btnSave.Click += (s, e) => SaveNewPassword();
        _pwdArea.Controls.Add(btnSave);

        _lblState = new Label
        {
            Location = new Point(140, 112),
            AutoSize = true,
            ForeColor = Color.DimGray,
            Font = new Font("Microsoft YaHei UI", 8.25f)
        };
        _pwdArea.Controls.Add(_lblState);
        Controls.Add(_pwdArea);

        // 在改密区的输入框里按 Enter 就保存，别让 AcceptButton 把窗直接关掉
        AcceptButton = ok;
        CancelButton = cancel;

        RefreshHint();
    }

    /// <summary>提示行随「是否已自定义密码」变化，避免改过密码后仍显示默认值。</summary>
    void RefreshHint()
    {
        _lblHint.Text = string.IsNullOrEmpty(_cfg.SettingsPasswordHash)
            ? "首次使用默认密码 dnf86（可在设置内修改）"
            : "当前为自定义密码（忘记可清空 config.json 恢复默认）";
    }

    void SetState(string text, Color color)
    {
        _lblState.Text = text;
        _lblState.ForeColor = color;
    }

    void EnterSaves(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        SaveNewPassword();
    }

    /// <summary>展开 / 收起改密区。收起时窗口缩回初始高度，不留空白带。</summary>
    void ToggleEdit()
    {
        _expanded = !_expanded;
        _pwdArea.Visible = _expanded;
        _btnEdit.Text = _expanded ? "收起 ▴" : "修改密码…";
        ClientSize = new Size(W, _expanded ? HExpanded : HCollapsed);
        if (_expanded) _txtNew.Focus();
        else _txt.Focus();
    }

    /// <summary>供截图自检：把改密区展开（已展开则不动作）。</summary>
    public void ExpandForShot()
    {
        if (!_expanded) ToggleEdit();
    }

    /// <summary>
    /// 保存新密码。顺序：先验当前密码（身份）→ 再验新密码长度 / 两次一致 / 不能与旧的相同
    /// → 写哈希 + 立即落盘 → 回读 config.json 确认真的写进去了。
    /// </summary>
    void SaveNewPassword()
    {
        var cur = _txt.Text ?? "";

        if (!Pwd.Check(_cfg.SettingsPasswordHash, cur))
        {
            SetState("上方当前密码不正确", Color.Firebrick);
            _txt.Focus();
            return;
        }

        var np = _txtNew.Text ?? "";
        if (np.Length < 4) { SetState("新密码至少 4 位", Color.Firebrick); _txtNew.Focus(); return; }
        if (np != _txtNew2.Text) { SetState("两次输入不一致", Color.Firebrick); _txtNew2.Focus(); return; }
        if (np == cur) { SetState("新密码与当前密码相同", Color.Firebrick); _txtNew.Focus(); return; }

        _cfg.SettingsPasswordHash = Pwd.Hash(np);
        _cfg.Save();

        // 回读校验：Save() 内部吞异常（目录只读等），不读一遍无法确认真的落地
        var back = AppConfig.Load();
        if (!string.Equals(back.SettingsPasswordHash, _cfg.SettingsPasswordHash, StringComparison.OrdinalIgnoreCase))
        {
            _cfg.SettingsPasswordHash = "";
            SetState("写入 config.json 失败（目录只读？）", Color.Firebrick);
            return;
        }

        PasswordChanged = true;
        // 同步到上方输入框：调用方还会用 Password 做一次校验，这样点「确定」不会失败
        _txt.Text = np;
        _txtNew.Clear();
        _txtNew2.Clear();
        SetState("已保存，点「确定」即可进入", Color.SeaGreen);
        RefreshHint();
    }

    /// <summary>
    /// 自检：布局折叠、提示文案、各类非法输入拦截、改密落盘。
    /// 会真实改写 config.json，调用方负责备份并恢复。
    /// </summary>
    public string SelfTest()
    {
        if (!string.IsNullOrEmpty(_cfg.SettingsPasswordHash))
            return "跳过：自检要求 config.json 里的 SettingsPasswordHash 为空（即默认密码状态）\r\n";

        var sb = new StringBuilder();
        int pass = 0, fail = 0;
        void Check(bool ok, string name, string extra = "")
        {
            if (ok) pass++; else fail++;
            sb.AppendLine($"[{(ok ? "OK" : "FAIL")}] {name}" + (string.IsNullOrEmpty(extra) ? "" : " · " + extra));
        }

        sb.AppendLine($"PasswordForm 自检  默认密码={Pwd.Default}");

        // 1 初始布局
        Check(ClientSize.Height == HCollapsed && !_expanded, "初始收起", $"高 {ClientSize.Height}");
        Check(_lblHint.Text.Contains(Pwd.Default), "提示行含默认密码", _lblHint.Text);
        Check(_btnEdit.Text.StartsWith("修改密码"), "入口按钮文案", _btnEdit.Text);

        // 2 展开 / 收起
        ToggleEdit();
        Check(_expanded && ClientSize.Height == HExpanded, "展开后窗口变高", $"高 {ClientSize.Height}");
        Check(_btnEdit.Text.StartsWith("收起"), "展开后按钮变「收起」", _btnEdit.Text);
        ToggleEdit();
        Check(!_expanded && ClientSize.Height == HCollapsed, "收起后恢复原高度", $"高 {ClientSize.Height}");
        ToggleEdit();   // 重新展开，继续改密测试

        // 3 当前密码错
        _txt.Text = "not-the-password";
        _txtNew.Text = "abc12345"; _txtNew2.Text = "abc12345";
        SaveNewPassword();
        Check(!PasswordChanged && _lblState.Text.Contains("不正确"), "当前密码错被拒绝", _lblState.Text);

        // 4 新密码太短
        _txt.Text = Pwd.Default;
        _txtNew.Text = "abc"; _txtNew2.Text = "abc";
        SaveNewPassword();
        Check(!PasswordChanged && _lblState.Text.Contains("4 位"), "新密码不足 4 位被拒绝", _lblState.Text);

        // 5 两次不一致
        _txtNew.Text = "abc12345"; _txtNew2.Text = "abc12346";
        SaveNewPassword();
        Check(!PasswordChanged && _lblState.Text.Contains("不一致"), "两次输入不一致被拒绝", _lblState.Text);

        // 6 新密码与当前相同
        _txtNew.Text = Pwd.Default; _txtNew2.Text = Pwd.Default;
        SaveNewPassword();
        Check(!PasswordChanged && _lblState.Text.Contains("相同"), "新旧密码相同被拒绝", _lblState.Text);

        // 7 正常改密
        _txtNew.Text = "gm2026pass"; _txtNew2.Text = "gm2026pass";
        SaveNewPassword();
        Check(PasswordChanged, "改密成功");
        Check(_lblState.Text.Contains("已保存"), "成功提示", _lblState.Text);
        Check(_txt.Text == "gm2026pass", "上方输入框同步为新密码");
        Check(Pwd.Check(_cfg.SettingsPasswordHash, "gm2026pass"), "新密码通过校验");
        Check(!Pwd.Check(_cfg.SettingsPasswordHash, Pwd.Default), "旧默认密码已失效");
        Check(_lblHint.Text.Contains("自定义"), "提示行切换为自定义", _lblHint.Text);

        var back = AppConfig.Load();
        Check(string.Equals(back.SettingsPasswordHash, _cfg.SettingsPasswordHash, StringComparison.OrdinalIgnoreCase),
            "已写入 config.json", back.SettingsPasswordHash.Length > 8 ? back.SettingsPasswordHash[..8] + "…" : back.SettingsPasswordHash);

        sb.AppendLine(new string('-', 50));
        sb.AppendLine(fail == 0 ? $"全部通过（{pass} 项）" : $"失败 {fail} 项 / 共 {pass + fail} 项");
        return sb.ToString();
    }
}
