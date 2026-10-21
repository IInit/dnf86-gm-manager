using System.Drawing;
using System.Windows.Forms;

namespace DNF86GM;

public class MainForm : Form
{
    readonly AppConfig _cfg;
    readonly Db _db;
    readonly ItemDb _items = new();
    readonly SshTunnel _tunnel = new();
    readonly System.Windows.Forms.Timer _suggestTimer = new() { Interval = 150 };

    // 顶栏
    Label _lblState;

    // 角色选择
    TextBox _txtSearchChar;
    Button _btnSearchChar;
    DataGridView _grid;
    Label _lblTarget;
    CharRow _current;
    List<CharRow> _rows = new();

    // 物品 / 邮件
    ComboBox _cmbCat;
    TextBox _txtItemName;
    Button _btnItemSearch;
    ListBox _lstSuggest;
    TextBox _txtItemCode;
    NumericUpDown _numCount, _numStr, _numForge, _numRedVal, _numGold;
    ComboBox _cmbRed;
    TextBox _txtLetter;
    Button _btnSend;
    TextBox _txtLog;
    bool _suppress;

    // 行尾统计标签：把原来纯空白的占位列变成有用信息（角色数 / 物品命中数）
    Label _lblCharCount;
    Label _lblItemStat;

    // 子类型维度：按当前物品类型动态重建（「全部」时整行收起）
    TableLayoutPanel _itemRows;
    FlowLayoutPanel _flowFacet;
    readonly List<Facet> _facetDefs = new();
    readonly List<ComboBox> _facetCombos = new();

    /// <summary>重建维度控件期间为 true，用来抑制 SelectedIndexChanged 引发的连锁筛选。</summary>
    bool _rebuilding;

    /// <summary>
    /// 当前列表里显示的结果，与 ListBox 逐行一一对应。
    /// 点选时直接按行号从这里取，**绝不重新 Search**：
    /// 名称框被上一次选中改写成完整物品名后，再按它搜一次只会命中 1 条，
    /// 行号对不上就直接 return —— 这就是「框里已有内容时双击列表没反应」的真因。
    /// </summary>
    readonly List<Item> _suggest = new();

    /// <summary>连接状态（唯一真源）。界面文案只由 SetState() 按它渲染。</summary>
    bool _connected;

    /// <summary>自动连接进行中，防止重复进入。</summary>
    bool _connecting;

    // 主体内容的最小高度；窗口更高时物品列表会把多余空间吃掉，
    // 窗口更矮时由外层滚动容器兜住（不会把发送按钮挤没）。
    // 比上一版多 44px：物品区多了一行「子类型维度」。
    const int BodyMinHeight = 690;

    /// <summary>
    /// 内容区最大宽度。
    /// 660 是按「每行控件按其最长可能文本算出来的需求宽度」定的，不是估的：
    ///   ① 角色行：标签 56 + 输入 200 + 按钮 52 = 308（账号/角色名 ≤10 字符）
    ///   ② 物品行：标签 52 + 下拉 100 + 名称 240 + 按钮 52 = 444（下拉最长「材料/消耗品」）
    ///      维度行：3 槽 × (52 + 96) = 462（下拉最长「徽章·附魔」）
    ///   ③ 参数行：34+68+34+68+34+118 = 356 + 提示 200 = 556
    ///   代码行：52 + 76 + 提示 260 = 388
    /// 最宽的一行是 ③ 参数行 556，加上 GroupBox 内边距(20) 与边框(4) 后约 580；
    /// 660 留了 80 的余量给行尾统计文字，再窄就要开始截断提示语。
    /// </summary>
    const int BodyMaxWidth = 660;

    public MainForm()
    {
        _cfg = AppConfig.Load();
        _db = new Db(_cfg);

        Text = "DNF 86 版 GM 管理器";
        // 宽 700 = 内容 660 + 两侧各 10 + 边框；比内容多出的部分只作对称留白，
        // 不会再把输入框和列表拉长（这是「空旷」的主要来源）。
        Width = 700;
        Height = 900;
        MinimumSize = new Size(680, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(243, 244, 246);
        DoubleBuffered = true;

        // 用 TableLayoutPanel 做根容器：不依赖 Dock 的 z 序，
        // 避免「顶栏把内容盖住」这类玄学问题。
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));    // 顶栏
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // 主体（可滚动）
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));    // 日志（108→96，给 768 屏腾 12px）

        root.Controls.Add(CenterHost(BuildTop(), new Padding(10, 6, 10, 6)), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(CenterHost(BuildLogBox(), new Padding(10, 0, 10, 10)), 0, 2);
        Controls.Add(root);

        _items.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "items.json"));
        Log($"物品库已加载：{_items.All.Count} 条，类型 {_items.Categories.Count - 1} 种");
        // 连接失败时也要知道库里有多少条，所以这里先写一次统计
        if (_lblItemStat != null) _lblItemStat.Text = $"命中 0 / {_items.All.Count}";

        _suggestTimer.Tick += (s, e) => { _suggestTimer.Stop(); DoSuggest(false); };
        FormClosing += (s, e) => { _tunnel.Dispose(); _cfg.Save(); };

        // 启动即自动连接：先把窗口画出来，再连，避免启动瞬间白屏。
        Shown += (s, e) => BeginInvoke(new Action(() =>
        {
            Application.DoEvents();
            AutoConnect();
        }));
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);       // 此时 AutoScale（Font 模式）已跑完，Width 是放大后的物理值
        FitToWorkingArea();
    }

    /// <summary>
    /// 按屏幕工作区再收一次尺寸。AutoScaleMode.Font 会在 150% 缩放下把 920 逻辑放大成 1380 物理，
    /// 1366 屏就直接横向溢出；这里按实际工作区钳住，并把窗口重新居中。
    /// 注意：MinimumSize 也已被 AutoScale 放大，所以要先把它一起压到工作区以内。
    /// </summary>
    void FitToWorkingArea()
    {
        var wa = Screen.FromControl(this).WorkingArea;
        int maxW = Math.Max(600, wa.Width - 24);
        int maxH = Math.Max(480, wa.Height - 24);

        MinimumSize = new Size(Math.Min(MinimumSize.Width, maxW), Math.Min(MinimumSize.Height, maxH));

        int w = Math.Min(Width, maxW);
        int h = Math.Min(Height, maxH);
        if (w == Width && h == Height) return;

        Width = w;
        Height = h;
        Left = wa.Left + (wa.Width - w) / 2;
        Top = wa.Top + (wa.Height - h) / 2;
    }

    // ==================================================================
    // 自检辅助（命令行 --uitest / --statetest / --shot 用）
    // ==================================================================
    /// <summary>
    /// 自检：模拟在候选列表里点 / 双击某一行，验证「名称 + 代码」两个框都被填上。
    /// 走的是真实的 MouseDown 处理路径（含 IndexFromPoint），而不是直接调填充函数。
    /// </summary>
    public string ClickTest()
    {
        var sb = new System.Text.StringBuilder();
        AutoConnect();
        Application.DoEvents();

        // ---- 场景 1：名称框是刚输入的关键词（第一次点） ----
        _txtItemName.Text = "无色";
        _txtItemCode.Text = "";
        DoSuggest(false);
        Application.DoEvents();
        sb.AppendLine($"[场景1] 输入「无色」，候选 {_lstSuggest.Items.Count} 条");

        int i1 = _lstSuggest.Items.Count > 1 ? 1 : 0;
        string want1 = i1 < _suggest.Count ? _suggest[i1].Name : "";
        string got1 = ClickRow(i1, false);
        sb.AppendLine($"  单击第 {i1 + 1} 行（应为「{want1}」）→ {got1}");

        // ---- 场景 2：名称框已有内容（点过「搜索」/ 已选过一条），再双击另一行 ----
        _txtItemName.Text = "无色";
        DoSuggest(true);                       // 等同点「搜索」按钮：名称框被填成完整物品名
        Application.DoEvents();
        sb.AppendLine($"[场景2] 点搜索后 名称='{_txtItemName.Text}' 代码='{_txtItemCode.Text}'，候选 {_lstSuggest.Items.Count} 条");

        int i2 = _lstSuggest.Items.Count > 2 ? 2 : 0;
        string wantN = i2 < _suggest.Count ? _suggest[i2].Name : "";
        string wantC = i2 < _suggest.Count ? _suggest[i2].Id.ToString() : "";
        string got2 = ClickRow(i2, true);       // 双击
        sb.AppendLine($"  双击第 {i2 + 1} 行（应为「{wantN}」[{wantC}]）→ {got2}");
        // 判定必须在换场景之前做完，否则会被场景3 改写后的文本覆盖
        bool ok2 = wantN.Length > 0
                   && _txtItemName.Text == wantN
                   && _txtItemCode.Text == wantC;

        // ---- 场景 3：连点两行，验证缓存不会被打乱 ----
        _txtItemName.Text = "无色";
        DoSuggest(false);
        Application.DoEvents();
        ClickRow(0, false);
        string n3 = _txtItemName.Text;
        // 用「最后一个完整可见的行」：列表可能比可视高度长，点到看不见的位置
        // IndexFromPoint 会返回 -1，那是测不出问题的假失败
        int i3 = LastVisibleRow();
        string wantN3 = i3 < _suggest.Count ? _suggest[i3].Name : "";
        string got3 = ClickRow(i3, false);
        sb.AppendLine($"[场景3] 先点第 1 行（名称变「{n3}」）再点最后一行（应为「{wantN3}」）→ {got3}");
        bool ok3 = wantN3.Length > 0 && _txtItemName.Text == wantN3;

        bool ok1 = _lstSuggest.Items.Count > 0 && want1.Length > 0;
        sb.AppendLine($"场景1={ok1} 场景2={ok2} 场景3={ok3}");
        sb.AppendLine("结果=" + ((ok1 && ok2 && ok3) ? "成功" : "失败"));
        return sb.ToString();
    }

    /// <summary>候选列表里最后一个完整可见的行号（自检用）。</summary>
    int LastVisibleRow()
    {
        for (int i = _lstSuggest.Items.Count - 1; i >= 0; i--)
        {
            var r = _lstSuggest.GetItemRectangle(i);
            if (r.Height > 0 && r.Bottom <= _lstSuggest.ClientSize.Height) return i;
        }
        return 0;
    }

    /// <summary>模拟鼠标单击 / 双击候选列表某一行（双击 = 两次 MouseDown，与 WinForms 实际投递一致）。</summary>
    string ClickRow(int idx, bool doubleClick)
    {
        if (idx < 0 || idx >= _lstSuggest.Items.Count) return "行号越界";

        var rect = _lstSuggest.GetItemRectangle(idx);
        var pt = new Point(rect.Left + 6, rect.Top + rect.Height / 2);
        var mi = typeof(ListBox).GetMethod("OnMouseDown",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        for (int k = 0; k < (doubleClick ? 2 : 1); k++)
        {
            mi.Invoke(_lstSuggest, new object[] { new MouseEventArgs(MouseButtons.Left, k + 1, pt.X, pt.Y, 0) });
            Application.DoEvents();
        }
        return $"名称='{_txtItemName.Text}' 代码='{_txtItemCode.Text}'";
    }

    /// <summary>
    /// 自检：自动连接是否生效、状态文案是否只有两种、角色是否自动选上。
    /// </summary>
    public string StateTest()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"初始: 文案='{_lblState.Text}'");
        AutoConnect();
        Application.DoEvents();

        bool okConnected = _lblState.Text == "已连接";
        bool okAuto = _grid.Rows.Count > 0;
        sb.AppendLine($"自动连接后: 文案='{_lblState.Text}' 角色行数={_grid.Rows.Count} 已选角色={(_current == null ? "无" : _current.Name)}");
        sb.AppendLine($"目标条: {_lblTarget.Text}");

        // 只发金币的合法性校验（不发 SQL，只看前置判断）
        // 注意：没连上时不允许调 PrepareSend，它会弹模态框把自检卡死
        bool okReject = true, okGoldOnly = true;
        if (_current != null)
        {
            _txtItemCode.Text = "";
            _numGold.Value = 0;
            okReject = !PrepareSend(out _, out _, out _, out _, silent: true);
            _numGold.Value = 1234567;
            okGoldOnly = PrepareSend(out var iid, out var cnt, out var gold, out _, silent: true);
            sb.AppendLine($"空物品+0金币 会被拦下? {okReject} / 只填金币 可通过? {okGoldOnly}（item={iid} cnt={cnt} gold={gold}）");
            _numGold.Value = 0;
        }
        else sb.AppendLine("（未连上/无角色，跳过金币校验）");

        Disconnect();
        Application.DoEvents();
        bool okBack = _lblState.Text == "未连接";
        sb.AppendLine($"断开后: 文案='{_lblState.Text}'");

        AutoConnect();
        Application.DoEvents();
        bool okAgain = _lblState.Text == "已连接";
        sb.AppendLine($"再自动连接: 文案='{_lblState.Text}'");

        sb.AppendLine($"连接={okConnected} 自动选角色={okAuto} 文案回退={okBack} 可重连={okAgain} 金币校验={okReject && okGoldOnly}");
        sb.AppendLine("结果=" + ((okConnected && okAuto && okBack && okAgain && okReject && okGoldOnly) ? "成功" : "失败"));
        return sb.ToString();
    }

    /// <summary>
    /// 自检：子类型维度是否按类型正确生成、组合筛选是否真生效、切类型/切维度是否清空输入框。
    /// </summary>
    public string FacetTest()
    {
        var sb = new System.Text.StringBuilder();
        AutoConnect();
        Application.DoEvents();

        // ---- 场景 1：「全部」不显示任何维度，整行收起 ----
        SetCategory(FacetTable.AllCategory);
        Application.DoEvents();
        bool ok1 = !_flowFacet.Visible && _facetCombos.Count == 0 && _itemRows.RowStyles[1].Height == 0;
        sb.AppendLine($"[场景1] 类型=全部 → 维度可见={_flowFacet.Visible} 维度数={_facetCombos.Count} " +
                      $"行高={_itemRows.RowStyles[1].Height} 候选={_suggest.Count}");

        // ---- 场景 2：切到材料/消耗品 → 出现「用途 / 来源」两维，默认都是「全部」 ----
        SetCategory("材料/消耗品");
        Application.DoEvents();
        string dims2 = string.Join("/", _facetDefs.Select(x => x.Label));
        bool ok2 = _flowFacet.Visible && _facetCombos.Count == 2 && dims2 == "用途/来源"
                   && _facetCombos.All(c => c.SelectedIndex == 0)
                   && _itemRows.RowStyles[1].Height == 34;
        sb.AppendLine($"[场景2] 类型=材料/消耗品 → 维度=[{dims2}] 默认全为「全部」=" +
                      $"{_facetCombos.All(c => c.SelectedIndex == 0)} 行高={_itemRows.RowStyles[1].Height}");

        // ---- 场景 3：切维度必须清空「物品名称 / 物品代码」 ----
        _txtItemName.Text = "无色";
        _txtItemCode.Text = "3037";
        Application.DoEvents();
        SelectFacet(0, "设计图");
        Application.DoEvents();
        bool ok3 = _txtItemName.Text == "" && _txtItemCode.Text == "";
        sb.AppendLine($"[场景3] 填了名称「无色」+代码「3037」后切维度 → 名称='{_txtItemName.Text}' 代码='{_txtItemCode.Text}'");

        // ---- 场景 4：单维度命中 ----
        bool ok4 = _suggest.Count > 0 && _suggest.All(x => x.Sub == 3);
        sb.AppendLine($"[场景4] 用途=设计图 → {_suggest.Count} 条，全部是设计图={ok4}");

        // ---- 场景 5：双维度组合（用途 + 来源 同时生效） ----
        SelectFacet(1, "通用/野外");
        Application.DoEvents();
        bool ok5 = _suggest.Count > 0 && _suggest.All(x => x.Sub == 3 && x.From == 1);
        sb.AppendLine($"[场景5] 用途=设计图 + 来源=通用/野外 → {_suggest.Count} 条，全部命中={ok5}");

        // ---- 场景 6：切类型要清空输入，并换成武器的三维 ----
        _txtItemName.Text = "太刀";
        _txtItemCode.Text = "27600";
        Application.DoEvents();
        SetCategory("武器");
        Application.DoEvents();
        string dims6 = string.Join("/", _facetDefs.Select(x => x.Label));
        bool ok6 = _txtItemName.Text == "" && _txtItemCode.Text == "" && dims6 == "武器种类/稀有度/等级段";
        sb.AppendLine($"[场景6] 切类型=武器 → 维度=[{dims6}] 名称/代码已清空=" +
                      $"({_txtItemName.Text == "" && _txtItemCode.Text == ""})");

        // ---- 场景 7：三维同时选（种类 + 稀有度 + 等级段） ----
        SelectFacet(0, "太刀");
        SelectFacet(1, "稀有");
        SelectFacet(2, "61-70 级");
        Application.DoEvents();
        bool ok7 = _suggest.Count > 0 &&
                   _suggest.All(x => x.Sub == 20 && x.Rarity == 2 && x.Level >= 61 && x.Level <= 70);
        sb.AppendLine($"[场景7] 武器 · 太刀 + 稀有 + 61-70 级 → {_suggest.Count} 条，全部命中={ok7}");
        sb.AppendLine("  例：" + string.Join(" | ",
            _suggest.Take(3).Select(x => $"{x.Name}[{x.Id}] {FacetTable.Describe(x)}")));

        // ---- 场景 8：防具的「材质 × 部位」是同一子类型码的两种投影 ----
        SetCategory("防具");
        Application.DoEvents();
        SelectFacet(0, "布甲");
        SelectFacet(1, "上衣");
        Application.DoEvents();
        bool ok8 = _suggest.Count > 0 && _suggest.All(x => x.Sub / 10 == 21 && x.Sub % 10 == 1);
        sb.AppendLine($"[场景8] 防具 · 布甲 + 上衣 → {_suggest.Count} 条，全部命中={ok8}");

        // ---- 场景 9：其余类型维度数量 ----
        SetCategory("首饰");
        Application.DoEvents();
        bool ok9 = _facetCombos.Count == 3;
        SetCategory("时装/装扮");
        Application.DoEvents();
        bool ok9b = _facetCombos.Count == 2;
        sb.AppendLine($"[场景9] 首饰维度数=3? {ok9} / 时装维度数=2? {ok9b}");

        // ---- 场景 10：回到「全部」→ 维度收起、条件全部作废 ----
        SetCategory(FacetTable.AllCategory);
        Application.DoEvents();
        bool ok10 = !_flowFacet.Visible && _facetCombos.Count == 0 && _suggest.Count == 200;
        sb.AppendLine($"[场景10] 回到全部 → 维度可见={_flowFacet.Visible} 候选={_suggest.Count}");

        bool ok = ok1 && ok2 && ok3 && ok4 && ok5 && ok6 && ok7 && ok8 && ok9 && ok9b && ok10;
        sb.AppendLine($"维度随类型变化={ok1 && ok2 && ok6} 切条件清输入={ok3} 单维度={ok4} " +
                      $"多维度组合={ok5 && ok7 && ok8} 恢复全部={ok10}");
        sb.AppendLine("结果=" + (ok ? "成功" : "失败"));
        return sb.ToString();
    }

    /// <summary>自检辅助：按显示名切换物品类型。</summary>
    void SetCategory(string cat)
    {
        int i = _cmbCat.Items.IndexOf(cat);
        if (i >= 0) _cmbCat.SelectedIndex = i;
    }

    /// <summary>自检辅助：按显示名选中第 dim 个维度的某个取值。</summary>
    void SelectFacet(int dim, string label)
    {
        if (dim < 0 || dim >= _facetCombos.Count) return;
        int i = _facetCombos[dim].Items.IndexOf(label);
        if (i >= 0) _facetCombos[dim].SelectedIndex = i;
    }

    /// <summary>供截图自检：切到指定类型并选好维度取值（不给值就用默认「全部」）。</summary>
    public void ShotFacet(string cat, string dim0, string dim1)
    {
        AutoConnect();
        Application.DoEvents();

        SetCategory(cat);
        Application.DoEvents();
        if (!string.IsNullOrEmpty(dim0)) SelectFacet(0, dim0);
        if (!string.IsNullOrEmpty(dim1)) SelectFacet(1, dim1);
        Application.DoEvents();
    }

    /// <summary>供截图自检：自动连接并可选填入物品关键词。</summary>
    public void ShotConnect(string itemKeyword)
    {
        AutoConnect();
        Application.DoEvents();
        if (!string.IsNullOrEmpty(itemKeyword))
        {
            _txtItemName.Text = itemKeyword;
            DoSuggest(false);
            _numGold.Value = 1000000;       // 截图里能看到金币框有值
        }
        Application.DoEvents();
    }

    /// <summary>兼容旧命令行的 --shot 参数：现在只有一页，无需切换。</summary>
    public void SelectTab(int i) { }

    // ==================================================================
    // 顶栏：只有状态，不再有连接/断开按钮（启动自动连接）
    // ==================================================================
    Panel BuildTop()
    {
        var p = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, BackColor = Color.White };

        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // 右列 200 = 「右键此处 → 连接设置 / 重新连接」实测 185px + 余量
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));

        _lblState = new Label
        {
            Text = "未连接",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            AutoEllipsis = true
        };

        var hint = new Label
        {
            Text = "右键此处 → 连接设置 / 重新连接",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.Silver,
            AutoEllipsis = true
        };

        bar.Controls.Add(_lblState, 0, 0);
        bar.Controls.Add(hint, 1, 0);

        // 设置与重连入口故意做成「隐藏」的：右键顶栏 / Ctrl+Alt+S，设置还要密码。
        var menu = new ContextMenuStrip();
        menu.Items.Add("重新连接（自动连接失败时用）", null, (s2, e2) => { Disconnect(); AutoConnect(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("连接设置…（需要密码）", null, (s2, e2) => OpenSettings());

        p.ContextMenuStrip = menu;
        _lblState.ContextMenuStrip = menu;
        bar.ContextMenuStrip = menu;
        hint.ContextMenuStrip = menu;

        p.Controls.Add(bar);
        return p;
    }

    // ==================================================================
    // 主体：角色选择（上） + 发物品 / 邮件（下）
    // ==================================================================

    /// <summary>
    /// 把控件包进一个「最大 BodyMaxWidth 宽、水平居中」的容器。
    /// 顶栏和日志都走这里，左右边缘才能和主体落在同一条竖线上——
    /// 否则窗口一拉宽，只有主体是收窄居中的，顶栏的状态字还贴着窗口左边、
    /// 日志框还撑满整屏，视觉上是散的。
    /// </summary>
    Panel CenterHost(Control inner, Padding pad)
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = pad,
            Margin = Padding.Empty
        };

        inner.Dock = DockStyle.None;      // 交给 Fit() 显式摆位，Dock=Fill 会无视宽度上限
        host.Controls.Add(inner);

        void Fit()
        {
            int availW = Math.Max(200, host.ClientSize.Width - pad.Horizontal);
            int availH = Math.Max(20, host.ClientSize.Height - pad.Vertical);
            int w = Math.Min(BodyMaxWidth, availW);
            inner.Size = new Size(w, availH);
            inner.Location = new Point(pad.Left + (availW - w) / 2, pad.Top);
        }

        host.Resize += (s, e) => Fit();
        host.HandleCreated += (s, e) => Fit();
        Fit();
        return host;
    }

    Panel BuildBody()
    {
        // 外层 AutoScroll：窗口被拉得很矮时出现滚动条，不会把下面的控件挤成 0 高
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.White
        };

        var body = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 6,
            Location = new Point(10, 8),
            Width = 980,
            Height = BodyMinHeight,
            Margin = Padding.Empty,
            BackColor = Color.White
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 172));   // 0 角色
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));    // 1 目标角色
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // 2 物品（吸收多余高度）
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));    // 3 参数
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));    // 4 正文
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));    // 5 发送

        body.Controls.Add(BuildCharBox(), 0, 0);
        body.Controls.Add(BuildTargetBar(), 0, 1);
        body.Controls.Add(BuildItemBox(), 0, 2);
        body.Controls.Add(BuildParamBox(), 0, 3);
        body.Controls.Add(BuildMailBox(), 0, 4);
        body.Controls.Add(BuildSendButton(), 0, 5);

        scroll.Controls.Add(body);

        // 宽高都跟着可视区域走（AutoScroll 出现纵向滚动条时要扣掉那一条）
        void Fit()
        {
            // 先算高度（可能触发纵向滚动条），再按「是否真的会有滚动条」算宽度，
            // 否则会多留 17px 把横向滚动条也招出来
            int h = Math.Max(BodyMinHeight, scroll.ClientSize.Height - 16);
            body.Height = h;

            bool needV = h + 16 > scroll.ClientSize.Height;
            int w = scroll.ClientSize.Width - 20
                    - (needV ? SystemInformation.VerticalScrollBarWidth : 0);
            // 锁上限 + 水平居中：窗口再宽也不会把输入框/列表继续拉长，两侧留白对称
            body.Width = Math.Min(BodyMaxWidth, Math.Max(400, w));
            body.Left = Math.Max(8, (scroll.ClientSize.Width - body.Width) / 2);
        }
        scroll.Resize += (s, e) => Fit();
        scroll.HandleCreated += (s, e) => Fit();
        Fit();

        return scroll;
    }

    // --- ① 角色选择 ---
    GroupBox BuildCharBox()
    {
        var gb = new GroupBox
        {
            Text = "① 选择角色（可按账号 / 角色名搜索，点选一行作为收件人）",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Color.White
        };

        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10, 3, 10, 8)
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = Padding.Empty
        };
        // 标签 56（「账号/角色」实测 49）+ 输入 200（10 字符账号约 120）+ 按钮 52（「搜索」实测 20）
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var lb = new Label
        {
            Text = "账号/角色",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _txtSearchChar = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 6, 6) };
        _btnSearchChar = new Button { Text = "搜索", Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 5) };
        _btnSearchChar.Click += (s, e) => SearchChars(true);
        _txtSearchChar.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { SearchChars(true); e.Handled = true; } };

        bar.Controls.Add(lb, 0, 0);
        bar.Controls.Add(_txtSearchChar, 1, 0);
        bar.Controls.Add(_btnSearchChar, 2, 0);

        // 第 4 列原来没有任何控件（纯 546px 空白），改成右对齐的角色数统计
        _lblCharCount = new Label
        {
            Text = "共 0 个角色",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.Gray,
            AutoEllipsis = true,
            Padding = new Padding(0, 0, 4, 0)
        };
        bar.Controls.Add(_lblCharCount, 3, 0);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            // 不再让五列平分宽度（原来是 Fill + FillWeight）：角色名/职业/账号都不超过 10 个字符，
            // 均分会把「编号」「等级」这种两三位数字的列也撑成 100+ 宽，行里到处是空白。
            // 现在按内容定固定列宽，只让角色名吃掉剩余宽度。
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            AllowUserToDeleteRows = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        };
        // 列宽 = 该列最长内容 + 单元格左右边距(约 12)，实测：
        // 编号 7 位数字 57 / 等级「Lv.86」37 / 职业「格斗家(女)」64 / 账号 10 字符约 120。
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "no", HeaderText = "编号", Width = 58 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "name",
            HeaderText = "角色名",
            MinimumWidth = 130,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill   // 唯一变宽的列
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "lv", HeaderText = "等级", Width = 52 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "job", HeaderText = "职业", Width = 96 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "acc", HeaderText = "账号", Width = 130 });
        _grid.SelectionChanged += (s, e) => PickCurrent();
        _grid.CellDoubleClick += (s, e) =>
        {
            // 双击一行 = 直接去填物品名，少点一次
            if (e.RowIndex >= 0) { _txtItemName.Focus(); _txtItemName.SelectAll(); }
        };

        t.Controls.Add(bar, 0, 0);
        t.Controls.Add(_grid, 0, 1);
        gb.Controls.Add(t);
        return gb;
    }

    // --- 目标角色提示条 ---
    Label BuildTargetBar()
    {
        _lblTarget = new Label
        {
            Text = "尚未选择角色 —— 请在上方列表中点选一行",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Firebrick,
            AutoEllipsis = true,
            Padding = new Padding(6, 0, 0, 0),
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold)
        };
        return _lblTarget;
    }

    // --- ② 物品搜索 ---
    GroupBox BuildItemBox()
    {
        var gb = new GroupBox
        {
            Text = "② 选择物品（类型 → 子类型维度 → 名称，可逐级收窄；点选自动填代码）",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8)
        };

        // 用四行表格：类型行 / 子类型维度行（可收起）/ 候选列表（占满剩余高度）/ 代码行。
        // 不用像素坐标，避免列表长高后把下面的代码行盖住。
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10, 3, 10, 8)
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));     // 类型 / 名称 / 搜索
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));      // 子类型维度（默认收起，「全部」不显示）
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));     // 候选列表
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));     // 代码行
        _itemRows = t;

        // --- 搜索行 ---
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = Padding.Empty
        };
        // 列宽全部按「该项最长可能出现的文本」定，不再留大块缓冲：
        // 标签「物品类型」实测 44 → 52；下拉最长「材料/消耗品」61 + 箭头 25 → 100；
        // 名称输入 216（18 个汉字，覆盖绝大多数关键词）；按钮「搜索」20 + 内边距 → 52。
        // 剩下的宽度全给行尾统计——窗口收到最小尺寸时，统计文字是唯一可能被挤到换行的地方。
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));    // 「物品类型」
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));   // 类型下拉
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 216));   // 名称输入（18 个汉字，够输关键词）
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));    // 搜索
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));    // 行尾统计

        _cmbCat = new ComboBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 5, 8, 5),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        // 切类型 = 整套条件变了：清空名称/代码、按新类型重建维度行，再按新条件出结果
        _cmbCat.SelectedIndexChanged += (s, e) =>
        {
            if (_rebuilding) return;
            ClearItemInputs();
            RebuildFacets();
            DoSuggest(false);
        };
        _cmbCat.Items.Add(FacetTable.AllCategory);

        _txtItemName = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 5, 8, 5),
            PlaceholderText = "输入物品名称，如 无色"
        };
        _txtItemName.TextChanged += (s, e) =>
        {
            if (_suppress) return;
            _suggestTimer.Stop();
            _suggestTimer.Start();
        };
        _txtItemName.KeyDown += TxtItemName_KeyDown;

        _btnItemSearch = new Button { Text = "搜索", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
        _btnItemSearch.Click += (s, e) => DoSuggest(true);

        bar.Controls.Add(new Label
        {
            Text = "物品类型",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        bar.Controls.Add(_cmbCat, 1, 0);
        bar.Controls.Add(_txtItemName, 2, 0);
        bar.Controls.Add(_btnItemSearch, 3, 0);

        // 行尾统计：总条数 + 当前命中，替掉原来那片纯空白
        _lblItemStat = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.Gray,
            AutoEllipsis = true,
            Padding = new Padding(8, 0, 4, 0)
        };
        bar.Controls.Add(_lblItemStat, 4, 0);

        // --- 子类型维度行（内容由 RebuildFacets 按当前类型生成） ---
        // 每个维度占一个定宽槽位：维度名 + 下拉（首项「全部」= 不参与筛选）。
        // 「全部」类型下没有任何维度，整行高度归零，视觉上等于不存在。
        _flowFacet = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
            Margin = Padding.Empty,
            BackColor = Color.White,
            Visible = false
        };

        // --- 候选列表 ---
        // 自绘三栏（名称 / [代码] / 分类·维度）：原来用空格拼接，字体非等宽导致
        // id 列参差不齐，短名字右侧空一大片。自绘后右侧被分类列占住，视觉密度正常。
        _lstSuggest = new ListBox
        {
            Dock = DockStyle.Fill,
            // true：高度自动对齐到 ItemHeight 的整数倍，
            // 底部就不会露出一半被裁的行（维度行展开时特别明显）。
            IntegralHeight = true,
            Margin = new Padding(0, 2, 0, 4),
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 22,
            BorderStyle = BorderStyle.FixedSingle
        };
        _lstSuggest.DrawItem += LstSuggest_DrawItem;
        // 单击就填：用 IndexFromPoint 自己算点击的是哪一行，
        // 不依赖 ListBox 的选中态（双击时选中态可能还没更新，原来就是这里点不动）。
        _lstSuggest.MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            int i = _lstSuggest.IndexFromPoint(e.Location);
            if (i >= 0) { _lstSuggest.SelectedIndex = i; FillFromSuggest(i); }
        };
        _lstSuggest.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                FillFromSuggest(_lstSuggest.SelectedIndex);
                e.Handled = true;
            }
        };

        // --- 代码行 ---
        var codeBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty
        };
        // 标签「物品代码」44 → 52；输入框按最长 7 位物品代码（51）留 76，够看清又不空。
        codeBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        codeBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        codeBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _txtItemCode = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 5, 10, 5) };
        _txtItemCode.TextChanged += (s, e) => CodeLookup(false);
        _txtItemCode.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { CodeLookup(true); e.Handled = true; } };

        codeBar.Controls.Add(new Label
        {
            Text = "物品代码",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        codeBar.Controls.Add(_txtItemCode, 1, 0);
        codeBar.Controls.Add(new Label
        {
            Text = "留空即只发金币；也可直接输数字代码回车查询",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gray,
            AutoEllipsis = true
        }, 2, 0);

        t.Controls.Add(bar, 0, 0);
        t.Controls.Add(_flowFacet, 0, 1);
        t.Controls.Add(_lstSuggest, 0, 2);
        t.Controls.Add(codeBar, 0, 3);

        gb.Controls.Add(t);
        return gb;
    }

    // --- ③ 发放参数（含金币） ---
    GroupBox BuildParamBox()
    {
        var gb = new GroupBox
        {
            Text = "③ 发放参数（装备才用得上强化 / 锻造 / 红字）",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8)
        };

        // 原来用固定坐标堆到 x=492，右侧 290px 永远空着，窗口一宽就更空。
        // 改成 2 行 × 7 列表格：标签/控件成对，最后一列吃掉剩余宽度放提示。
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 8,
            RowCount = 2,
            Padding = new Padding(10, 2, 10, 4)
        };
        // 标签列 40（「数量」等两字实测 32）；普通数值列 68（4 位数字 + 微调箭头 17）；
        // 末组拆成 68 + 50 两列，金币跨这两列 = 118（「4,000,000,000」实测约 107），
        // 这样锻造框保持 68 不会被金币列撑宽，而两者左边缘仍在同一条竖线上。
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));   // 0 标签
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));   // 1 数量 / 红字
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));   // 2 标签
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));   // 3 强化 / 数值
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));   // 4 标签
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));   // 5 锻造 / 金币（前段）
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));   // 6 金币（后段）
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // 7 提示
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        Label Lb(string s) => new()
        {
            Text = s,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _numCount = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4),
            TextAlign = HorizontalAlignment.Right,   // 数字统一靠右，收窄后列内更整齐
            Minimum = 1,
            Maximum = 9999,
            Value = 1
        };
        _numStr = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4),
            TextAlign = HorizontalAlignment.Right,   // 数字统一靠右，收窄后列内更整齐
            Minimum = 0,
            Maximum = 31
        };
        _numForge = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4),
            TextAlign = HorizontalAlignment.Right,   // 数字统一靠右，收窄后列内更整齐
            Minimum = 0,
            Maximum = 8
        };
        _cmbRed = new ComboBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbRed.Items.AddRange(new object[] { "无", "力量", "智力", "体力", "精神" });
        _cmbRed.SelectedIndex = 0;

        _numRedVal = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4),
            TextAlign = HorizontalAlignment.Right,   // 数字统一靠右，收窄后列内更整齐
            Minimum = 0,
            Maximum = 999
        };
        _numGold = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4),
            TextAlign = HorizontalAlignment.Right,   // 数字统一靠右，收窄后列内更整齐
            Minimum = 0,
            Maximum = 4000000000m,     // postal.gold 是 int unsigned
            ThousandsSeparator = true,
            Increment = 10000
        };

        // 第一行：数量 / 强化 / 锻造
        t.Controls.Add(Lb("数量"), 0, 0);
        t.Controls.Add(_numCount, 1, 0);
        t.Controls.Add(Lb("强化"), 2, 0);
        t.Controls.Add(_numStr, 3, 0);
        t.Controls.Add(Lb("锻造"), 4, 0);
        t.Controls.Add(_numForge, 5, 0);

        // 第二行：红字 / 数值 / 金币（跨 5-6 两列）+ 提示
        t.Controls.Add(Lb("红字"), 0, 1);
        t.Controls.Add(_cmbRed, 1, 1);
        t.Controls.Add(Lb("数值"), 2, 1);
        t.Controls.Add(_numRedVal, 3, 1);
        t.Controls.Add(Lb("金币"), 4, 1);
        t.Controls.Add(_numGold, 5, 1);
        t.SetColumnSpan(_numGold, 2);
        t.Controls.Add(new Label
        {
            Text = "随邮件一起发放的游戏币，0 = 不发",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gray,
            AutoEllipsis = true
        }, 7, 1);

        gb.Controls.Add(t);

        // NumericUpDown 在构造里会把内部文本框的对齐重置成 Left，初始化器那一句会被覆盖，
        // 这里等控件树挂好后再统一设一遍，五个数字框才真正右对齐。
        foreach (var n in new[] { _numCount, _numStr, _numForge, _numRedVal, _numGold })
        {
            n.TextAlign = HorizontalAlignment.Right;
            if (n.Controls.Count > 1 && n.Controls[1] is TextBox tb)
                tb.TextAlign = HorizontalAlignment.Right;
        }

        return gb;
    }

    // --- ④ 邮件正文 ---
    GroupBox BuildMailBox()
    {
        var gb = new GroupBox
        {
            Text = "④ 邮件正文",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8)
        };
        _txtLetter = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            Text = _cfg.LetterText
        };
        gb.Controls.Add(_txtLetter);
        return gb;
    }

    // --- 发送按钮 ---
    Button BuildSendButton()
    {
        _btnSend = new Button
        {
            Text = "发送物品邮件（可随信附带金币）",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 2),
            BackColor = Color.SeaGreen,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold)
        };
        _btnSend.FlatAppearance.BorderSize = 0;
        _btnSend.Click += (s, e) => SendItem();
        return _btnSend;
    }

    // --- 日志 ---
    GroupBox BuildLogBox()
    {
        var gb = new GroupBox
        {
            Text = "运行日志",
            Dock = DockStyle.Fill,
            Margin = new Padding(10, 0, 10, 10),
            Padding = new Padding(6, 2, 6, 6)
        };
        _txtLog = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(250, 250, 250)
        };
        gb.Controls.Add(_txtLog);
        return gb;
    }

    // ==================================================================
    // 子类型维度（随物品类型动态生成）
    // ==================================================================
    /// <summary>
    /// 按当前物品类型重建维度行。
    /// 「全部」类型没有任何维度（不同类型之间没有公共轴），整行收起。
    /// </summary>
    void RebuildFacets()
    {
        var cat = _cmbCat.SelectedItem as string ?? FacetTable.AllCategory;
        var defs = FacetTable.For(cat);

        _rebuilding = true;
        _flowFacet.SuspendLayout();

        foreach (Control c in _flowFacet.Controls) c.Dispose();
        _flowFacet.Controls.Clear();
        _facetDefs.Clear();
        _facetCombos.Clear();

        foreach (var def in defs)
        {
            // 槽位定宽：标签 60（最长「武器种类」实测 56，52 会把「类」字挤掉）
            // + 下拉 96（最长选项「徽章·附魔」47 + 箭头 25）。
            // 三个维度 156×3 = 468，落在 660 宽的内容区里，不需要换行。
            var slot = new Panel { Width = 156, Height = 30, Margin = new Padding(0, 2, 6, 2) };

            var title = new Label
            {
                Text = def.Label,
                Location = new Point(0, 0),
                Size = new Size(60, 30),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var cmb = new ComboBox
            {
                Location = new Point(62, 3),
                Width = 94,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmb.Items.Add(FacetTable.AllCategory);          // 索引 0 = 不选 = 全部
            foreach (var o in def.Options) cmb.Items.Add(o.Label);
            cmb.SelectedIndex = 0;
            cmb.SelectedIndexChanged += (s, e) => OnFacetChanged();

            slot.Controls.Add(title);
            slot.Controls.Add(cmb);
            _flowFacet.Controls.Add(slot);

            _facetDefs.Add(def);
            _facetCombos.Add(cmb);
        }

        _flowFacet.ResumeLayout();
        _rebuilding = false;

        SetFacetRowVisible(defs.Count > 0);
    }

    /// <summary>维度行可见性：连同它的行高一起收起，避免留下一条空白带。</summary>
    void SetFacetRowVisible(bool show)
    {
        _flowFacet.Visible = show;
        if (_itemRows != null && _itemRows.RowStyles.Count > 1)
            _itemRows.RowStyles[1].Height = show ? 34 : 0;
    }

    /// <summary>
    /// 任一维度变化：条件变了，已填的名称/代码一律清空，避免残留条件把结果带偏。
    /// </summary>
    void OnFacetChanged()
    {
        if (_rebuilding) return;

        ClearItemInputs();
        DoSuggest(false);

        if (_facetCombos.Count > 0)
        {
            var cat = _cmbCat.SelectedItem as string ?? FacetTable.AllCategory;
            var picked = SelectedFacets().Select(x => x.Label);
            Log($"维度筛选：{cat} · {string.Join(" + ", picked.DefaultIfEmpty("全部"))} → {_suggest.Count} 条");
        }
    }

    /// <summary>清空「物品名称」与「物品代码」两个输入框（不触发联想/反查）。</summary>
    void ClearItemInputs()
    {
        _suppress = true;
        _txtItemName.Text = "";
        _txtItemCode.Text = "";
        _suppress = false;
    }

    /// <summary>当前实际选中的维度取值（「全部」不参与筛选）。</summary>
    List<FacetOption> SelectedFacets()
    {
        var list = new List<FacetOption>();
        for (int i = 0; i < _facetCombos.Count && i < _facetDefs.Count; i++)
        {
            int k = _facetCombos[i].SelectedIndex;
            if (k <= 0) continue;                       // 0 = 全部
            if (k - 1 < _facetDefs[i].Options.Count) list.Add(_facetDefs[i].Options[k - 1]);
        }
        return list;
    }

    // ==================================================================
    // 物品搜索（输入即联想）
    // ==================================================================
    void TxtItemName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && _lstSuggest.Items.Count > 0)
        {
            _lstSuggest.Focus();
            _lstSuggest.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            DoSuggest(true);
            e.Handled = true;
        }
    }

    /// <summary>直接按物品代码查询（手输代码时用）。</summary>
    void CodeLookup(bool fromEnter)
    {
        if (_suppress) return;
        var txt = _txtItemCode.Text.Trim();
        if (txt.Length == 0) return;

        if (!int.TryParse(txt, out var id) || id <= 0)
        {
            if (fromEnter)
                MessageBox.Show(this, "物品代码必须是正整数", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var it = _items.FindById(id);
        if (it == null)
        {
            if (fromEnter)
                MessageBox.Show(this, $"未搜索到物品（代码 {id} 不在物品库里）", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _suppress = true;
        _txtItemName.Text = it.Name;
        _suppress = false;
        RenderSuggest(new List<Item> { it });
        _lstSuggest.SelectedIndex = 0;
        Log($"按代码选中：{it.Name} [{it.Id}]");
    }

    void DoSuggest(bool explicitSearch)
    {
        if (_items.All.Count == 0) return;

        var kw = _txtItemName.Text.Trim();
        var cat = _cmbCat.SelectedItem as string ?? FacetTable.AllCategory;

        var list = _items.Search(kw, cat, 200, SelectedFacets());
        RenderSuggest(list);

        if (list.Count > 0)
        {
            _lstSuggest.SelectedIndex = 0;
            // 名称完全一致时直接填代码，省一次点击
            if (explicitSearch || (kw.Length > 0 && list[0].Name == kw))
                SetCode(list[0]);
        }
        else
        {
            _txtItemCode.Text = "";
            if (explicitSearch)
                MessageBox.Show(this, "未搜索到物品", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>把搜索结果同时写进列表控件和缓存，保证两者逐行对齐。</summary>
    void RenderSuggest(List<Item> list)
    {
        _suggest.Clear();
        if (list != null) _suggest.AddRange(list);

        _lstSuggest.BeginUpdate();
        _lstSuggest.Items.Clear();
        foreach (var it in _suggest)
            _lstSuggest.Items.Add($"{it.Name}    [{it.Id}]    {it.Cat} · {FacetTable.Describe(it)}");
        _lstSuggest.EndUpdate();

        if (_lblItemStat != null)
            _lblItemStat.Text = $"命中 {_suggest.Count} / {_items.All.Count}";
    }

    /// <summary>
    /// 候选行自绘成三栏：名称左对齐（超长自动省略）、[代码] 右对齐在中段、分类·维度靠最右且灰色。
    /// 数据取自 _suggest（与 Items 逐行对齐），不做任何重新搜索。
    /// </summary>
    void LstSuggest_DrawItem(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _suggest.Count) return;

        var it = _suggest[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;

        using (var bg = new SolidBrush(sel ? SystemColors.Highlight : Color.White))
            e.Graphics.FillRectangle(bg, e.Bounds);

        var fgMain = sel ? SystemColors.HighlightText : Color.FromArgb(32, 32, 32);
        var fgCode = sel ? SystemColors.HighlightText : Color.FromArgb(52, 92, 148);
        var fgSub = sel ? SystemColors.HighlightText : Color.Gray;
        var f = e.Font ?? Font;

        var row = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(40, e.Bounds.Width - 16), e.Bounds.Height);

        // 三列固定起点、统一左对齐：名称 / [代码] / 分类·维度。
        // 以前代码和分类是「靠右浮动」算出来的，起点随名称长短逐行变化，扫视时对不齐；
        // 现在代码列在每个宽度下都从同一个 x 开始，分类列同理。
        string code = $"[{it.Id}]";
        string tail = $"{it.Cat} · {FacetTable.Describe(it)}";

        const int CodeW = 62;    // 「[27612]」实测 51，留 11 给列间空隙
        const int Gap = 8;       // 代码列与分类列之间的间隔
        const int TailMax = 190; // 「材料/消耗品 · 设计图 · 活动」实测约 175
        // 宽度不够时先压分类列（下限 120），再压名称列，保证代码列起点始终稳定
        int tailW = Math.Min(TailMax, Math.Max(120, row.Width - CodeW - Gap - 140));
        int nameW = Math.Max(100, row.Width - CodeW - Gap - tailW);

        var nameRect = new Rectangle(row.Left, row.Y, nameW, row.Height);
        var codeRect = new Rectangle(nameRect.Right, row.Y, CodeW, row.Height);
        var tailRect = new Rectangle(codeRect.Right + Gap, row.Y, tailW, row.Height);

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(e.Graphics, it.Name, f, nameRect, fgMain, flags | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, code, f, codeRect, fgCode, flags | TextFormatFlags.Left);
        TextRenderer.DrawText(e.Graphics, tail, f, tailRect, fgSub, flags | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        if (sel) e.DrawFocusRectangle();
    }

    /// <summary>把候选列表第 i 项同时填进「物品名称」和「物品代码」两个输入框。</summary>
    void FillFromSuggest(int i)
    {
        if (i < 0) return;

        // 优先用列表缓存里的对象：它才是用户眼睛看到的那一行。
        // 只有缓存缺失（异常情况）才退回按当前关键词重搜。
        Item it = (i < _suggest.Count) ? _suggest[i] : null;
        if (it == null)
        {
            var list = _items.Search(_txtItemName.Text.Trim(),
                _cmbCat.SelectedItem as string ?? FacetTable.AllCategory, 200, SelectedFacets());
            if (i >= list.Count) return;
            it = list[i];
        }

        _suppress = true;                        // 阻止 TextChanged 再触发联想/反查
        _txtItemName.Text = it.Name;
        _txtItemCode.Text = it.Id.ToString();
        _suppress = false;

        // 光标摆到顺手的位置：名称末尾可继续补字，代码整段选中可直接覆盖
        _txtItemName.SelectionStart = _txtItemName.TextLength;
        _txtItemCode.SelectionStart = 0;
        _txtItemCode.SelectionLength = _txtItemCode.TextLength;

        // 保持该行高亮，让用户看清选中的是哪一条
        if (i < _lstSuggest.Items.Count) _lstSuggest.SelectedIndex = i;

        Log($"已选中：{it.Name} [{it.Id}]");
    }

    void SetCode(Item it)
    {
        _suppress = true;
        _txtItemName.Text = it.Name;
        _txtItemCode.Text = it.Id.ToString();
        _suppress = false;
    }

    // ==================================================================
    // 连接设置（默认隐藏，输入密码才打开）
    // ==================================================================
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Alt | Keys.S))
        {
            OpenSettings();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void OpenSettings()
    {
        using (var pf = new PasswordForm("连接设置 · 需要访问密码", _cfg))
        {
            if (pf.ShowDialog(this) != DialogResult.OK) return;
            // 弹窗里刚改过密码 = 已用当前密码验证过身份，直接放行；
            // 否则按 config.json 里的哈希再校验一次（改密码的落盘也可能失败）。
            if (!pf.PasswordChanged && !Pwd.Check(_cfg.SettingsPasswordHash, pf.Password))
            {
                MessageBox.Show(this, "密码不正确。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        using var f = new SettingsForm(_cfg);
        if (f.ShowDialog(this) != DialogResult.OK) return;

        _cfg.CopyFrom(f.Result);
        _cfg.Save();
        Log("设置已保存，正在按新参数重新连接…");
        Disconnect();
        AutoConnect();
    }

    // ==================================================================
    // 连接（启动自动执行，无需用户操作）
    // ==================================================================
    /// <summary>
    /// 统一刷新连接状态。界面上只允许出现「已连接 / 未连接」两种文案，
    /// 不附带版本号、IP、隧道端口、账号等任何信息——那些一律写进日志。
    /// </summary>
    void SetState(bool connected)
    {
        _connected = connected;

        if (_lblState == null) return;
        _lblState.Text = connected ? "已连接" : "未连接";
        _lblState.ForeColor = connected ? Color.SeaGreen : Color.DimGray;
        _lblState.Refresh();            // 立刻重绘，避免被后续耗时操作拖住
    }

    /// <summary>自动连接：启动、改完设置、或右键「重新连接」时调用。</summary>
    void AutoConnect()
    {
        if (_connecting) return;
        _connecting = true;
        try { Connect(); }
        finally { _connecting = false; }
    }

    void Connect()
    {
        SetState(false);                    // 连接过程中一律显示「未连接」
        try
        {
            if (_cfg.UseTunnel)
            {
                if (!_tunnel.Start(_cfg))
                {
                    Log("自动连接失败：SSH 隧道建立不了 —— " + _tunnel.LastError);
                    return;
                }
                _cfg.DbHost = "127.0.0.1";
                _cfg.DbPort = _cfg.LocalPort;
            }

            var ver = _db.TestConnection();

            SetState(true);
            Log($"自动连接成功（{ver}）" + (_cfg.UseTunnel ? $" · 隧道 127.0.0.1:{_cfg.LocalPort}" : ""));

            LoadCategories();
            SearchChars(false);              // 顺手把角色列表拉出来，省得用户先点搜索
        }
        catch (Exception ex)
        {
            SetState(false);
            Log("自动连接失败：" + ex.Message + "（右键顶栏可重新连接）");
        }
    }

    void Disconnect()
    {
        _tunnel.Stop();
        SetState(false);
        Log("已断开连接");
    }

    void LoadCategories()
    {
        if (_cmbCat.Items.Count > 1 || _items.Categories.Count == 0) return;
        _cmbCat.Items.Clear();
        foreach (var c in _items.Categories) _cmbCat.Items.Add(c);
        _cmbCat.SelectedIndex = 0;
    }

    // ==================================================================
    // 角色
    // ==================================================================
    void SearchChars(bool notify)
    {
        try
        {
            var list = _db.SearchChars(_txtSearchChar.Text);
            _rows = list;
            _grid.Rows.Clear();
            foreach (var c in list)
                _grid.Rows.Add(c.CharacNo, c.Name, c.Level, c.JobName, c.Account);

            Log($"角色搜索：{list.Count} 条");
            if (_lblCharCount != null)
                _lblCharCount.Text = $"共 {list.Count} 个角色";

            if (list.Count == 0)
            {
                _current = null;
                _lblTarget.Text = "没有匹配的角色";
                _lblTarget.ForeColor = Color.Firebrick;
                if (notify)
                    MessageBox.Show(this, "没有匹配的角色", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 自动选第一行：连上就有收件人，用户在下面直接发就行
            _grid.Rows[0].Selected = true;
            _grid.CurrentCell = _grid.Rows[0].Cells[0];
            PickCurrent();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "查询失败：" + ex.Message, "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void PickCurrent()
    {
        if (_grid.SelectedRows.Count == 0) return;
        var row = _grid.SelectedRows[0];
        if (row.Cells[0].Value == null) return;
        int no = Convert.ToInt32(row.Cells[0].Value);

        _current = _rows.FirstOrDefault(x => x.CharacNo == no);
        if (_current == null) return;

        _lblTarget.ForeColor = Color.Black;
        _lblTarget.Text =
            $"收件人：{_current.Name}（编号 {_current.CharacNo} · 账号 {_current.Account} · " +
            $"{_current.JobName} · Lv.{_current.Level} · 现有金币 {_current.Money:N0}）";
    }

    // ==================================================================
    // 发送
    // ==================================================================
    /// <summary>
    /// 发送前校验与取值：物品代码留空即「只发金币」。
    /// 返回 false 表示参数不成立（会自动弹提示）。
    /// </summary>
    bool PrepareSend(out int itemId, out int count, out long gold, out string itemName,
                     bool silent = false)
    {
        itemId = 0;
        count = 0;
        gold = 0;
        itemName = "";

        if (_current == null)
        {
            if (!silent)
                MessageBox.Show(this, "请先在上方列表里点选一个角色", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        bool hasItem = int.TryParse(_txtItemCode.Text.Trim(), out itemId) && itemId > 0;
        gold = (long)_numGold.Value;

        if (!hasItem && gold <= 0)
        {
            if (!silent)
                MessageBox.Show(this, "请先选择物品，或在「金币」里填一个大于 0 的金额", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        count = hasItem ? (int)_numCount.Value : 0;
        itemName = hasItem ? (_items.FindById(itemId)?.Name ?? "未知物品") : "";
        return true;
    }

    void SendItem()
    {
        if (!PrepareSend(out var itemId, out var count, out var gold, out var itemName))
            return;

        // 红字下拉 → 库里的约定值：3-力量 4-智力 1-体力 2-精神
        int redOpt = _cmbRed.SelectedIndex switch { 1 => 3, 2 => 4, 3 => 1, 4 => 2, _ => 0 };
        int redVal = redOpt == 0 ? 0 : (int)_numRedVal.Value;

        int str = (int)_numStr.Value;
        int forge = (int)_numForge.Value;

        try
        {
            _btnSend.Enabled = false;
            var letterId = _db.SendItem(
                _current.CharacNo, itemId, count, str, forge,
                redOpt, redVal, gold, _cfg.SenderName, _txtLetter.Text);

            var what = itemId > 0 ? $"{itemName} [{itemId}] × {count}" : "（无物品）";
            Log($"已发送：{what}" + (gold > 0 ? $" + 金币 {gold:N0}" : "") +
                $" → {_current.Name}（信件 {letterId}）");

            var lines = new List<string> { "发送成功", "" };
            if (itemId > 0) lines.Add($"物品：{itemName} [{itemId}] × {count}");
            if (gold > 0) lines.Add($"金币：{gold:N0}");
            lines.Add($"收件人：{_current.Name}");
            lines.Add("");
            lines.Add("游戏内小退到角色选择再进，或重新登录即可收取。");

            MessageBox.Show(this, string.Join("\r\n", lines),
                "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("发送失败：" + ex.Message);
            MessageBox.Show(this, "发送失败：" + ex.Message, "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSend.Enabled = true;
        }
    }

    void Log(string msg)
    {
        if (_txtLog == null) return;
        _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
    }
}
