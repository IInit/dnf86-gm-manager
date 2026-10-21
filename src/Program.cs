using System.Text;

namespace DNF86GM;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            SelfTest();
            return;
        }

        if (args.Length > 0 && args[0].Equals("--testsend", StringComparison.OrdinalIgnoreCase))
        {
            TestSend(args);
            return;
        }

        // 状态自检：连接 / 断开时顶栏文案是否只剩「已连接 / 未连接」
        if (args.Length > 0 && args[0].Equals("--statetest", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            using var f = new MainForm();
            f.Show();
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(60); }
            var txt = f.StateTest();
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "state-test.txt"), txt, Encoding.UTF8);
            f.Close();
            return;
        }

        // 交互自检：模拟点击候选列表
        if (args.Length > 0 && args[0].Equals("--uitest", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            using var f = new MainForm();
            f.Show();
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(60); }
            var txt = f.ClickTest();
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-test.txt"), txt, Encoding.UTF8);
            f.Close();
            return;
        }

        // 维度自检：子类型维度随类型变化 / 多维度组合 / 切条件清空输入
        if (args.Length > 0 && args[0].Equals("--facetest", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            using var f = new MainForm();
            f.Show();
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(60); }
            var txt = f.FacetTest();
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "facet-test.txt"), txt, Encoding.UTF8);
            f.Close();
            return;
        }

        // 访问密码弹窗自检：--pwtest（临时把哈希清空回到默认密码，测完恢复 config.json）
        if (args.Length > 0 && args[0].Equals("--pwtest", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            var cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            var backup = File.Exists(cfgPath) ? File.ReadAllText(cfgPath, Encoding.UTF8) : null;
            string txt;
            try
            {
                var c = AppConfig.Load();
                c.SettingsPasswordHash = "";
                c.Save();
                using var dlg = new PasswordForm("连接设置 · 需要访问密码", c);
                txt = dlg.SelfTest();
            }
            finally
            {
                if (backup != null) File.WriteAllText(cfgPath, backup, new UTF8Encoding(false));
            }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pw-test.txt"), txt, Encoding.UTF8);
            return;
        }

        // 访问密码弹窗截图：--shotpwd 输出png [1=展开改密区]
        if (args.Length > 0 && args[0].Equals("--shotpwd", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            var path = args.Length > 1
                ? args[1]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pwd.png");
            using var dlg = new PasswordForm("连接设置 · 需要访问密码", AppConfig.Load());
            dlg.Show();
            for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(60); }
            if (args.Length > 2 && args[2] == "1") dlg.ExpandForShot();
            for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(60); }
            using var pwdBmp = new Bitmap(dlg.Width, dlg.Height);
            dlg.DrawToBitmap(pwdBmp, new Rectangle(0, 0, dlg.Width, dlg.Height));
            pwdBmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            dlg.Close();
            return;
        }

        // 指定尺寸截图：--shotsize 输出png 宽 高 [物品关键词]
        // 用来验证「收窄后不同窗口/屏幕尺寸」的适配（最小尺寸、大屏居中、内容是否被裁）
        if (args.Length > 0 && args[0].Equals("--shotsize", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            var outPath = args.Length > 1
                ? args[1]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shot-size.png");
            int w = args.Length > 2 ? int.Parse(args[2]) : 920;
            int h = args.Length > 3 ? int.Parse(args[3]) : 920;

            using var f = new MainForm();
            f.Show();
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(60); }
            f.Size = new Size(w, h);          // Show 之后设，绕过 OnLoad 的工作区钳制
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(60); }
            f.ShotConnect(args.Length > 4 ? args[4] : "");
            for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(80); }

            using var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height);
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.ClientSize.Width, f.ClientSize.Height));
            bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            f.Close();
            return;
        }

        // 设置对话框截图：--shotcfg 输出png
        if (args.Length > 0 && args[0].Equals("--shotcfg", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            var path = args.Length > 1
                ? args[1]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.png");
            using var dlg = new SettingsForm(AppConfig.Load());
            dlg.Show();
            for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(60); }
            using var bmp2 = new Bitmap(dlg.ClientSize.Width, dlg.ClientSize.Height);
            dlg.DrawToBitmap(bmp2, new Rectangle(0, 0, dlg.ClientSize.Width, dlg.ClientSize.Height));
            bmp2.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            dlg.Close();
            return;
        }

        // 界面自检截图：--shot [输出png] [页签序号0/1/2]
        if (args.Length > 0 && args[0].Equals("--shot", StringComparison.OrdinalIgnoreCase))
        {
            Shot(args);
            return;
        }

        // 维度筛选截图：--shotfacet [输出png] [类型] [维度1取值] [维度2取值]
        if (args.Length > 0 && args[0].Equals("--shotfacet", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            var outPath = args.Length > 1
                ? args[1]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shot-facet.png");
            string cat = args.Length > 2 ? args[2] : "武器";
            string d0 = args.Length > 3 ? args[3] : "";
            string d1 = args.Length > 4 ? args[4] : "";

            using var f = new MainForm();
            f.Show();
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(60); }
            f.ShotFacet(cat, d0, d1);
            for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(80); }

            using var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height);
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.ClientSize.Width, f.ClientSize.Height));
            bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            f.Close();
            return;
        }

        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show("程序启动失败：\n" + ex,
                "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 发一封测试邮件：--testsend [角色编号] [物品代码] [数量] [金币]
    /// 默认 1 号角色、物品 1（复活币）、数量 1、金币 0。
    /// 物品代码给 0 就是「只发金币」。
    /// </summary>
    static void TestSend(string[] args)
    {
        int cno = args.Length > 1 ? int.Parse(args[1]) : 1;
        int item = args.Length > 2 ? int.Parse(args[2]) : 1;
        int cnt = args.Length > 3 ? int.Parse(args[3]) : 1;
        long gold = args.Length > 4 ? long.Parse(args[4]) : 0;

        var sb = new StringBuilder();
        var cfg = AppConfig.Load();
        var tunnel = new SshTunnel();
        try
        {
            if (cfg.UseTunnel)
            {
                if (!tunnel.Start(cfg)) throw new Exception("隧道失败：" + tunnel.LastError);
                cfg.DbHost = "127.0.0.1";
                cfg.DbPort = cfg.LocalPort;
            }

            var db = new Db(cfg);
            sb.AppendLine("连接：" + db.TestConnection());

            var c = db.SearchChars("", 50).FirstOrDefault(x => x.CharacNo == cno);
            sb.AppendLine($"目标角色：{(c == null ? "未找到" : c.ToString())}");

            int lid = db.SendItem(cno, item, cnt, 0, 0, 0, 0, gold, "GM", "GM 测试邮件，收到可忽略。");
            sb.AppendLine($"发送成功，letter_id={lid}（物品={item}，数量={cnt}，金币={gold}）");
        }
        catch (Exception ex)
        {
            sb.AppendLine("失败：" + ex);
        }
        finally
        {
            tunnel.Dispose();
        }

        File.WriteAllText(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-send.txt"),
            sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 界面截图自检：把窗口真实渲染结果存成 PNG，用于核对控件有没有被遮挡/错位。
    /// 用法：DNF86GM.exe --shot [输出路径] [页签序号]
    /// </summary>
    static void Shot(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var outPath = args.Length > 1
            ? args[1]
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shot.png");
        int tab = args.Length > 2 ? int.Parse(args[2]) : -1;

        using var f = new MainForm();
        f.Show();
        for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(60); }
        // 先连接（Connect 内部会把页签切回角色页），再切到目标页签
        if (args.Length > 3 && args[3] == "1")
        {
            string kw = args.Length > 4 ? args[4] : "";
            f.ShotConnect(kw);
        }
        if (tab >= 0) { f.SelectTab(tab); }
        for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(80); }

        using var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.ClientSize.Width, f.ClientSize.Height));
        bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
        f.Close();
    }

    /// <summary>
    /// 命令行自检：不弹窗，结果写入 exe 同目录 self-test.txt。
    /// 用法：DNF86GM.exe --selftest
    /// </summary>
    static void SelfTest()
    {
        var log = new StringBuilder();
        int fail = 0;
        void W(string s) => log.AppendLine(s);
        void Step(string name, Func<string> act)
        {
            try { W($"[OK]   {name}: {act()}"); }
            catch (Exception ex) { fail++; W($"[FAIL] {name}: {ex.Message}"); }
        }

        W($"DNF86GM 自检  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        W(new string('-', 60));

        var cfg = AppConfig.Load();
        W($"配置: SSH {cfg.SshHost}:{cfg.SshPort} → 本地 {cfg.LocalPort} → 远端 {cfg.RemotePort}, 隧道={cfg.UseTunnel}");
        W($"私钥(配置值): {cfg.SshKeyPath}");
        W($"私钥(实际使用): {KeyStore.Resolve(cfg)}");

        var items = new ItemDb();
        Step("物品库", () =>
        {
            items.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "items.json"));
            return $"{items.All.Count} 条 / {items.Categories.Count - 1} 类";
        });

        Step("物品联想(复活币)", () =>
        {
            var r = items.Search("复活币", "全部", 5);
            return r.Count == 0 ? "未命中(异常)" : string.Join(", ", r.Select(x => $"{x.Name}[{x.Id}]"));
        });

        Step("物品联想(输入一半: 无色)", () =>
        {
            var r = items.Search("无色", "材料/消耗品", 5);
            return r.Count == 0 ? "未命中" : string.Join(", ", r.Select(x => $"{x.Name}[{x.Id}]"));
        });

        var tunnel = new SshTunnel();
        if (cfg.UseTunnel)
        {
            Step("SSH 隧道", () =>
            {
                if (!tunnel.Start(cfg)) throw new Exception(tunnel.LastError);
                cfg.DbHost = "127.0.0.1";
                cfg.DbPort = cfg.LocalPort;
                return $"127.0.0.1:{cfg.LocalPort} 已转发 · 私钥来自：{tunnel.KeySource}";
            });
        }

        var db = new Db(cfg);
        Step("数据库", () => db.TestConnection());

        Step("角色查询", () =>
        {
            var list = db.SearchChars("", 20);
            return $"{list.Count} 条，例：" + string.Join(" | ", list.Take(3).Select(x => x.ToString()));
        });

        tunnel.Dispose();
        W(new string('-', 60));
        W(fail == 0 ? "全部通过" : $"失败 {fail} 项");

        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.txt");
        File.WriteAllText(path, log.ToString(), Encoding.UTF8);
    }
}
