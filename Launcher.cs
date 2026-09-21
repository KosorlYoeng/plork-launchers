using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FiveMServerLauncher
{
    public class LauncherForm : Form
    {
        // ================================================================
        //  ★ 服务器/品牌/远程配置（你只需要改这里就行）
        // ================================================================
        // 连接服务器
        private const string SERVER_CONNECT_CODE   = "qqqkya6";
        // 品牌显示
        private const string BRAND_FULL            = "MzzPlork";       // 大标题显示
        private const string BRAND_SHORT           = "Plork";          // 侧边LOGO/按钮
        private const string BRAND_VER             = "V2.0";          // 右上角版本（数字变大就代表有新版本）
        private const string BRAND_EN              = "Plork";      // 英文副标
        private const string BRAND_SUBTITLE        = "Plork Launcher";  // 小标题
        private const string LAUNCHER_SUBTITLE_CN  = "FiveM Login Control Panel";
        // FiveM 服务器真实地址（用于服务状态页探测延迟/在线），如果不知道可以先留 127.0.0.1:30120
        private const string SERVER_IP             = "151.243.226.55";
        private const int    SERVER_PORT           = 30120;

        // ★★★ GitHub 自动更新 + 更新日志远程地址（都放在 GitHub）
        // 操作步骤：在 GitHub 新建一个 Public 仓库，上传 2 个文件：
        //   1) version.json —— 记录最新版本号和下载地址
        //   2) changelog.txt —— 更新日志内容
        // 然后把下面 GITHUB_USER / GITHUB_REPO 改成你自己的
        private const string GITHUB_USER           = "2069127";              // ← 已自动填你的 GitHub 账号
        private const string GITHUB_REPO           = "fivem-launcher";      // ← 仓库名（保持默认也行，想换就换）
        private const string GITHUB_BRANCH         = "main";                  // GitHub 默认分支一般是 main

        // （下面两个会自动根据上面拼成 GitHub Raw 地址，一般不用手动改）
        private static string VERSION_JSON_URL { get { return "https://raw.githubusercontent.com/" + GITHUB_USER + "/" + GITHUB_REPO + "/" + GITHUB_BRANCH + "/version.json"; } }
        private static string CHANGELOG_URL    { get { return "https://raw.githubusercontent.com/" + GITHUB_USER + "/" + GITHUB_REPO + "/" + GITHUB_BRANCH + "/changelog.txt"; } }

        // 社群入口（展示 + 一键复制 / 打开）
        private const string SOCIAL_QQ_GROUP_NUM   = "123456789";
        private const string SOCIAL_QQ_GROUP_LINK  = "";   // 可选，QQ 群加群链接，例如 https://jq.qq.com/?_wv=1027&k=xxxxx
        private const string SOCIAL_DISCORD        = "https://discord.gg/5T3Wr5ymC";   // 可选
        private const string SOCIAL_WEBSITE        = "";   // 可选
        private const string SOCIAL_WECHAT_ID      = "";   // 可选
        // ================================================================

        private Process _fivemProcess;
        private bool    _fivemLaunchedByUs = false;

        // ========== 颜色（秋叶暖色极简风格） ==========
        private static readonly Color BG_APP        = Color.FromArgb(27, 24, 34);   // 深紫黑
        private static readonly Color BG_SIDEBAR    = Color.FromArgb(22, 20, 28);   // 侧边稍深
        private static readonly Color BG_CARD       = Color.FromArgb(35, 31, 44);   // 卡片
        private static readonly Color BG_CARD_HOVER = Color.FromArgb(45, 40, 56);
        private static readonly Color BG_INPUT      = Color.FromArgb(29, 26, 38);   // 输入/标签
        private static readonly Color BG_NAV_ACTIVE = Color.FromArgb(210, 120, 60); // 选中：秋叶橙
        private static readonly Color BG_TITLEBAR   = Color.FromArgb(18, 16, 23);
        private static readonly Color FG_PRIMARY    = Color.FromArgb(242, 237, 230);
        private static readonly Color FG_SECONDARY  = Color.FromArgb(185, 176, 160);
        private static readonly Color FG_FAINT      = Color.FromArgb(125, 117, 105);
        private static readonly Color ACCENT        = Color.FromArgb(230, 138, 72);  // 秋叶橙主色
        private static readonly Color ACCENT_HOVER  = Color.FromArgb(248, 158, 92);
        private static readonly Color ACCENT_GREEN  = Color.FromArgb(80, 180, 120);
        private static readonly Color ACCENT_RED    = Color.FromArgb(224, 90, 90);
        private static readonly Color BORDER_COLOR  = Color.FromArgb(55, 48, 68);
        private static readonly Color BTN_CLOSE_H   = Color.FromArgb(220, 50, 60);
        private static readonly Color BTN_MIN_H     = Color.FromArgb(48, 42, 60);

        [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        // 状态/页面控件引用
        private Label  lblInstallStatus;
        private Label  lblInstallHint;
        private Label  lblPingLeft;
        private Label  lblOnlineLeft;
        private Label  lblPingRight;
        private Label  lblOnlineRight;
        private Button btnStart;

        // 4 个页面容器
        private Panel  pageLauncher;
        private Panel  pageChangelog;
        private Panel  pageStatus;
        private Panel  pageCommunity;
        private Panel  bodyHost; // 承载各页面切换的容器

        // 导航按钮集合（用来切换选中态）
        private List<Panel> navPanels = new List<Panel>();
        private int currentPageIndex = 0;

        public LauncherForm()
        {
            Text = BRAND_FULL + " 启动器";
            Size = new Size(1020, 660);
            MinimumSize = new Size(980, 620);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BG_APP;
            Font = new Font("微软雅黑", 9F);
            MaximizeBox = false;
            MinimizeBox = false;

            BuildUI();
            UpdateFiveMStatus();

            // 启动时后台检查更新（不阻塞 UI）
            ThreadPool.QueueUserWorkItem(delegate { CheckUpdate(); });
        }

        // ================================================================
        //  GitHub 自动更新检查
        //  version.json 格式示例：
        //  {
        //    "version": "V2.1",
        //    "download_url": "https://github.com/你的账号/仓库/releases/download/V2.1/MzzPlork.exe",
        //    "release_page": "https://github.com/你的账号/仓库/releases/latest",
        //    "note": "修复了启动报错和新增了XX功能"
        //  }
        // ================================================================
        private void CheckUpdate()
        {
            // 还没配置 GitHub 账号时，直接跳过
            if (string.IsNullOrEmpty(GITHUB_USER) ||
                GITHUB_USER.IndexOf("your-", StringComparison.OrdinalIgnoreCase) >= 0) return;

            try
            {
                string jsonText;
                using (WebClient wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers["User-Agent"] = BRAND_FULL + "-Launcher/" + BRAND_VER;
                    jsonText = wc.DownloadString(VERSION_JSON_URL);
                }
                if (string.IsNullOrEmpty(jsonText)) return;

                // 简易 JSON 解析（不依赖 Newtonsoft，.NET Framework 4 可用）
                string remoteVer = ExtractJsonValue(jsonText, "version");
                string dlUrl     = ExtractJsonValue(jsonText, "download_url");
                string relUrl    = ExtractJsonValue(jsonText, "release_page");
                string note      = ExtractJsonValue(jsonText, "note");

                if (string.IsNullOrEmpty(remoteVer)) return;

                // 比较版本号（只比较数字部分，V2.0 vs V2.1  => 2.0 < 2.1 => 需要更新）
                if (CompareVersion(remoteVer, BRAND_VER) > 0)
                {
                    this.BeginInvoke((Action)delegate
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.Append("发现新版本！\n\n");
                        sb.Append("当前版本：").Append(BRAND_VER).Append("\n");
                        sb.Append("最新版本：").Append(remoteVer).Append("\n\n");
                        if (!string.IsNullOrEmpty(note)) sb.Append("更新说明：\n").Append(note).Append("\n\n");
                        sb.Append("是否立即跳转到下载地址？");

                        DialogResult dr = MessageBox.Show(this, sb.ToString(),
                            "发现新版本 - " + BRAND_FULL,
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                        if (dr == DialogResult.Yes)
                        {
                            string url = !string.IsNullOrEmpty(dlUrl) ? dlUrl : relUrl;
                            if (!string.IsNullOrEmpty(url))
                            {
                                try { Process.Start(url); }
                                catch (Exception ex)
                                {
                                    MessageBox.Show(this, "无法打开下载地址：" + url + "\n" + ex.Message
                                        + "\n\n请手动复制地址到浏览器下载。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                }
                            }
                        }
                    });
                }
            }
            catch
            {
                // 网络错误静默失败，不打扰用户
            }
        }

        // 简易 JSON 取字段值（支持 "key":"value" 和 "key": "value"）
        private static string ExtractJsonValue(string json, string key)
        {
            string marker = "\"" + key + "\"";
            int idx = json.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += marker.Length;
            // 跳过 : 和空白
            while (idx < json.Length && (json[idx] == ':' || char.IsWhiteSpace(json[idx]))) idx++;
            if (idx >= json.Length) return null;
            if (json[idx] == '"')
            {
                idx++;
                int start = idx;
                while (idx < json.Length && json[idx] != '"')
                {
                    if (json[idx] == '\\' && idx + 1 < json.Length) idx += 2;
                    else idx++;
                }
                if (idx >= json.Length) return null;
                return json.Substring(start, idx - start);
            }
            return null;
        }

        // 比较 V2.0 < V2.1，返回负数（a<b）/ 0 / 正数（a>b）
        private static int CompareVersion(string a, string b)
        {
            try
            {
                string sa = StripVersion(a);
                string sb = StripVersion(b);
                string[] pa = sa.Split('.');
                string[] pb = sb.Split('.');
                int n = Math.Max(pa.Length, pb.Length);
                for (int i = 0; i < n; i++)
                {
                    int va = (i < pa.Length) ? ParseIntOrZero(pa[i]) : 0;
                    int vb = (i < pb.Length) ? ParseIntOrZero(pb[i]) : 0;
                    if (va != vb) return va.CompareTo(vb);
                }
                return 0;
            }
            catch { return 0; }
        }
        private static string StripVersion(string v)
        {
            if (string.IsNullOrEmpty(v)) return "0";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                if (char.IsDigit(c) || c == '.') sb.Append(c);
            }
            string r = sb.ToString();
            return string.IsNullOrEmpty(r) ? "0" : r;
        }
        private static int ParseIntOrZero(string s)
        {
            int r; if (int.TryParse(s, out r)) return r; return 0;
        }

        // ================================================================
        //  UI 构建
        // ================================================================
        private void BuildUI()
        {
            // ---------- 左侧导航栏 ----------
            Panel sidebar = new Panel { Dock = DockStyle.Left, Width = 218, BackColor = BG_SIDEBAR };
            this.Controls.Add(sidebar);
            BuildSidebar(sidebar);

            // ---------- 右侧主区 ----------
            Panel mainArea = new Panel { Dock = DockStyle.Fill, BackColor = BG_APP };
            this.Controls.Add(mainArea);
            this.Controls.SetChildIndex(mainArea, 0);

            Panel titleBar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = BG_TITLEBAR };
            titleBar.MouseDown += DragWindow_MouseDown;
            mainArea.Controls.Add(titleBar);
            BuildTitleBar(titleBar);

            bodyHost = new Panel { Dock = DockStyle.Fill, BackColor = BG_APP };
            bodyHost.Padding = new Padding(22, 18, 22, 18);
            mainArea.Controls.Add(bodyHost);
            mainArea.Controls.SetChildIndex(bodyHost, 0);
            bodyHost.Resize += (s, e) => RelayoutPages();
            bodyHost.HandleCreated += (s, e) => BuildPages();
        }

        private void BuildSidebar(Panel bar)
        {
            // 顶部 Logo 卡
            Panel logoCard = new Panel
            {
                Size = new Size(bar.Width - 28, 78),
                Location = new Point(14, 14),
                BackColor = BG_CARD
            };
            logoCard.Region = RoundedRegion(logoCard.Width, logoCard.Height, 12);
            logoCard.Paint += (s, e) => DrawThinBorder(logoCard, e.Graphics);
            bar.Controls.Add(logoCard);

            PictureBox pic = new PictureBox
            {
                Size = new Size(34, 34),
                Location = new Point(18, 22),
                SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.Transparent,
                Image = DrawBrandLogo(34)
            };
            logoCard.Controls.Add(pic);

            Label l1 = new Label
            {
                Text = BRAND_FULL,
                ForeColor = FG_PRIMARY,
                BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 11F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(62, 19)
            };
            logoCard.Controls.Add(l1);

            Label l2 = new Label
            {
                Text = BRAND_EN,
                ForeColor = Color.FromArgb(210, 150, 95),
                BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 7.8F),
                AutoSize = true,
                Location = new Point(62, 44)
            };
            logoCard.Controls.Add(l2);

            // 导航项
            string[] icons = { "➤", "⟳", "◉", "◎" };
            string[] names = { "启动面板", "更新日志", "服务状态", "社群入口" };
            int y = 112;
            navPanels.Clear();
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                Panel nav = MakeNavItem(bar.Width - 28, 46, icons[i], names[i], i == 0);
                nav.Location = new Point(14, y);
                nav.Click += (s, e) => SwitchPage(idx);
                navPanels.Add(nav);
                bar.Controls.Add(nav);
                y += 54;
            }

            // 底部退出按钮
            Panel exit = MakeNavItem(bar.Width - 28, 46, "✕", "退出启动器", false, true);
            bar.Controls.Add(exit);
            exit.Click += (s, e) => this.Close();
            bar.Resize += (s, e) => exit.Location = new Point(14, bar.ClientSize.Height - 60);
            exit.Location = new Point(14, bar.ClientSize.Height - 60);
        }

        private Panel MakeNavItem(int w, int h, string icon, string name, bool active, bool isExit = false)
        {
            Panel p = new Panel
            {
                Width = w, Height = h,
                BackColor = active ? BG_NAV_ACTIVE : Color.Transparent,
                Cursor = Cursors.Hand
            };
            p.Region = RoundedRegion(w, h, 10);

            Label li = new Label
            {
                Text = icon,
                ForeColor = isExit ? Color.FromArgb(235, 120, 120) : (active ? Color.White : FG_SECONDARY),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI Symbol", active ? 9.5F : 9F, FontStyle.Bold),
                Size = new Size(28, 22),
                Location = new Point(14, 12),
                TextAlign = ContentAlignment.MiddleCenter
            };
            p.Controls.Add(li);

            Label ln = new Label
            {
                Text = name,
                ForeColor = isExit ? Color.FromArgb(235, 140, 140) : (active ? Color.White : FG_SECONDARY),
                BackColor = Color.Transparent,
                Font = new Font("微软雅黑", active ? 10F : 9.3F, active ? FontStyle.Bold : FontStyle.Regular),
                AutoSize = true,
                Location = new Point(52, 13)
            };
            p.Controls.Add(ln);

            Color normalBg = active ? BG_NAV_ACTIVE : Color.Transparent;
            Color hoverBg  = isExit ? Color.FromArgb(75, 30, 35) : Color.FromArgb(52, 45, 64);
            Color normalIconFg = isExit ? Color.FromArgb(235, 120, 120) : (active ? Color.White : FG_SECONDARY);
            Color hoverIconFg  = isExit ? Color.FromArgb(255, 160, 160) : (active ? Color.White : FG_PRIMARY);
            Color normalNameFg = isExit ? Color.FromArgb(235, 140, 140) : (active ? Color.White : FG_SECONDARY);
            Color hoverNameFg  = isExit ? Color.FromArgb(255, 170, 170) : (active ? Color.White : FG_PRIMARY);

            EventHandler enter = (s, e) => { p.BackColor = hoverBg; li.ForeColor = hoverIconFg; ln.ForeColor = hoverNameFg; };
            EventHandler leave = (s, e) => { p.BackColor = normalBg; li.ForeColor = normalIconFg; ln.ForeColor = normalNameFg; };
            p.MouseEnter  += enter; p.MouseLeave  += leave;
            li.MouseEnter += enter; li.MouseLeave += leave;
            ln.MouseEnter += enter; ln.MouseLeave += leave;
            return p;
        }

        private void BuildTitleBar(Panel bar)
        {
            PictureBox pic = new PictureBox
            {
                Size = new Size(30, 30), Location = new Point(22, 14),
                SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.Transparent,
                Image = DrawBrandLogo(30)
            };
            bar.Controls.Add(pic); pic.MouseDown += DragWindow_MouseDown;

            Label t1 = new Label
            {
                Text = BRAND_FULL,
                ForeColor = FG_PRIMARY, Font = new Font("微软雅黑", 10.5F, FontStyle.Bold),
                AutoSize = true, Location = new Point(62, 12)
            };
            t1.MouseDown += DragWindow_MouseDown; bar.Controls.Add(t1);

            Label t2 = new Label
            {
                Text = BRAND_SUBTITLE,
                ForeColor = FG_FAINT, Font = new Font("微软雅黑", 8.2F),
                AutoSize = true, Location = new Point(62, 33)
            };
            t2.MouseDown += DragWindow_MouseDown; bar.Controls.Add(t2);

            // 版本框
            Panel vb = new Panel { Size = new Size(52, 26), BackColor = BG_CARD };
            vb.Region = RoundedRegion(52, 26, 8);
            vb.Paint += (s, e) => DrawThinBorder(vb, e.Graphics);
            Label lv = new Label
            {
                Text = BRAND_VER, ForeColor = FG_SECONDARY, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 8F, FontStyle.Bold), AutoSize = true
            };
            lv.Location = new Point((vb.Width - lv.PreferredWidth) / 2, (vb.Height - lv.PreferredHeight) / 2);
            vb.Controls.Add(lv);
            vb.Location = new Point(bar.ClientSize.Width - 170, 16);
            bar.Controls.Add(vb);
            bar.Resize += (s, e) => vb.Location = new Point(bar.ClientSize.Width - 170, 16);

            // 最小化
            Button btnMin = new Button
            {
                Text = "—", Size = new Size(42, 30), FlatStyle = FlatStyle.Flat,
                BackColor = BG_TITLEBAR, ForeColor = FG_SECONDARY, Font = new Font("Segoe UI", 12F),
                Cursor = Cursors.Hand, Location = new Point(bar.ClientSize.Width - 105, 14)
            };
            btnMin.FlatAppearance.BorderSize = 0;
            btnMin.Click += (s, e) => this.WindowState = FormWindowState.Minimized;
            btnMin.MouseEnter += (s, e) => btnMin.BackColor = BTN_MIN_H;
            btnMin.MouseLeave += (s, e) => btnMin.BackColor = BG_TITLEBAR;
            bar.Controls.Add(btnMin);
            bar.Resize += (s, e) => btnMin.Location = new Point(bar.ClientSize.Width - 105, 14);

            // 关闭
            Button btnClose = new Button
            {
                Text = "✕", Size = new Size(42, 30), FlatStyle = FlatStyle.Flat,
                BackColor = BG_TITLEBAR, ForeColor = FG_SECONDARY, Font = new Font("Segoe UI", 10F),
                Cursor = Cursors.Hand, Location = new Point(bar.ClientSize.Width - 55, 14)
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => this.Close();
            btnClose.MouseEnter += (s, e) => btnClose.BackColor = BTN_CLOSE_H;
            btnClose.MouseLeave += (s, e) => btnClose.BackColor = BG_TITLEBAR;
            bar.Controls.Add(btnClose);
            bar.Resize += (s, e) => btnClose.Location = new Point(bar.ClientSize.Width - 55, 14);
        }

        private void DragWindow_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        // ================================================================
        //  四个页面
        // ================================================================
        private void BuildPages()
        {
            int w = bodyHost.ClientSize.Width - bodyHost.Padding.Horizontal;
            int h = bodyHost.ClientSize.Height - bodyHost.Padding.Vertical;
            int x = bodyHost.Padding.Left;
            int y = bodyHost.Padding.Top;

            pageLauncher  = new Panel { Size = new Size(w, h), Location = new Point(x, y), BackColor = BG_APP, Visible = true  };
            pageChangelog = new Panel { Size = new Size(w, h), Location = new Point(x, y), BackColor = BG_APP, Visible = false };
            pageStatus    = new Panel { Size = new Size(w, h), Location = new Point(x, y), BackColor = BG_APP, Visible = false };
            pageCommunity = new Panel { Size = new Size(w, h), Location = new Point(x, y), BackColor = BG_APP, Visible = false };

            bodyHost.Controls.Add(pageLauncher);
            bodyHost.Controls.Add(pageChangelog);
            bodyHost.Controls.Add(pageStatus);
            bodyHost.Controls.Add(pageCommunity);

            BuildLauncherPage(pageLauncher);
            BuildChangelogPage(pageChangelog);
            BuildStatusPage(pageStatus);
            BuildCommunityPage(pageCommunity);
        }

        private void RelayoutPages()
        {
            if (bodyHost == null || pageLauncher == null) return;
            int w = bodyHost.ClientSize.Width - bodyHost.Padding.Horizontal;
            int h = bodyHost.ClientSize.Height - bodyHost.Padding.Vertical;
            int x = bodyHost.Padding.Left;
            int y = bodyHost.Padding.Top;
            pageLauncher.Size  = new Size(w, h); pageLauncher.Location  = new Point(x, y);
            pageChangelog.Size = new Size(w, h); pageChangelog.Location = new Point(x, y);
            pageStatus.Size    = new Size(w, h); pageStatus.Location    = new Point(x, y);
            pageCommunity.Size = new Size(w, h); pageCommunity.Location = new Point(x, y);
            // 重新布局内容（页内的控件按大小自适应）
            LayoutLauncherPage(pageLauncher);
        }

        private void SwitchPage(int idx)
        {
            currentPageIndex = idx;
            pageLauncher.Visible  = (idx == 0);
            pageChangelog.Visible = (idx == 1);
            pageStatus.Visible    = (idx == 2);
            pageCommunity.Visible = (idx == 3);

            // 更新导航样式
            for (int i = 0; i < navPanels.Count; i++)
            {
                bool active = (i == idx);
                Panel p = navPanels[i];
                if (p == null || p.Controls.Count < 2) continue;
                p.BackColor = active ? BG_NAV_ACTIVE : Color.Transparent;
                Label li = (Label)p.Controls[0];
                Label ln = (Label)p.Controls[1];
                li.ForeColor = active ? Color.White : FG_SECONDARY;
                li.Font = new Font("Segoe UI Symbol", active ? 9.5F : 9F, FontStyle.Bold);
                ln.ForeColor = active ? Color.White : FG_SECONDARY;
                ln.Font = new Font("微软雅黑", active ? 10F : 9.3F, active ? FontStyle.Bold : FontStyle.Regular);
            }

            // 页面专属动作
            if (idx == 1) RefreshChangelog();
            if (idx == 2) RefreshStatus();
        }

        // ---------- 页面1：启动面板（主功能页，保留原有样式但更简约） ----------
        private void BuildLauncherPage(Panel p) { LayoutLauncherPage(p); }

        private void LayoutLauncherPage(Panel host)
        {
            host.SuspendLayout();
            // 清理动态内容（标签 dyn）
            List<Control> rem = new List<Control>();
            foreach (Control c in host.Controls) if (c.Tag != null && c.Tag.ToString() == "dyn") rem.Add(c);
            foreach (Control c in rem) { host.Controls.Remove(c); c.Dispose(); }

            int w = host.ClientSize.Width;
            int h = host.ClientSize.Height;

            int leftW  = (int)(w * 0.67);
            int rightW = w - leftW - 16;

            // 左侧主内容
            Panel left = new Panel
            {
                Size = new Size(leftW, h), Location = new Point(0, 0),
                BackColor = BG_APP, Tag = "dyn"
            };
            host.Controls.Add(left);

            Label hdr = new Label
            {
                Text = "服务器信息", ForeColor = FG_PRIMARY,
                Font = new Font("微软雅黑", 11F, FontStyle.Bold), AutoSize = true,
                Location = new Point(0, 0)
            };
            left.Controls.Add(hdr);

            Panel tag = MakePill("自动连接接入", ACCENT, Color.FromArgb(70, 42, 22));
            tag.Location = new Point(leftW - tag.Width, -2);
            left.Controls.Add(tag);

            // Banner
            int bannerH = (int)(h * 0.48);
            Panel banner = MakeCard(leftW, bannerH, 0, 40);
            banner.Tag = "dyn";
            banner.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Rectangle(0, 0, banner.Width, banner.Height),
                    Color.FromArgb(72, 42, 22), Color.FromArgb(32, 26, 40), 0f))
                {
                    g.FillRectangle(br, 0, 0, banner.Width, banner.Height);
                }
                // 秋叶元素：圆形橙点散落
                Random rnd = new Random(42);
                for (int i = 0; i < 14; i++)
                {
                    int cx = rnd.Next(banner.Width);
                    int cy = banner.Height - rnd.Next(banner.Height - 20);
                    int dia = 5 + rnd.Next(22);
                    Color col = Color.FromArgb(
                        120 + rnd.Next(90),
                        60 + rnd.Next(70),
                        30 + rnd.Next(40));
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(80, col)))
                        g.FillEllipse(sb, cx, cy, dia, dia);
                }
                DrawThinBorder(banner, g);
            };
            left.Controls.Add(banner);

            Label lb1 = new Label
            {
                Text = BRAND_FULL, ForeColor = Color.White, BackColor = Color.Transparent,
                Font = new Font("Impact", 34F), AutoSize = true, Location = new Point(32, 40)
            };
            banner.Controls.Add(lb1);

            Label lb2 = new Label
            {
                Text = "欢迎使用 " + BRAND_FULL + " 启动器，点击启动游戏即可连接服务器。\n"
                     + "FiveM 连接码：connect " + SERVER_CONNECT_CODE,
                ForeColor = Color.FromArgb(235, 228, 215), BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 9.5F), AutoSize = true,
                Location = new Point(34, 95), MaximumSize = new Size(leftW - 60, 0)
            };
            banner.Controls.Add(lb2);

            Panel tag2 = MakePill("操作面板", ACCENT, Color.FromArgb(70, 42, 22));
            tag2.Location = new Point(leftW - tag2.Width - 22, 22);
            banner.Controls.Add(tag2);

            // 两张小卡
            int sy = banner.Bottom + 16;
            int sw = (leftW - 14) / 2;
            int sh = 90;

            Panel pc1 = MakeCard(sw, sh, 0, sy);
            left.Controls.Add(pc1);
            AddCardContent(pc1, "服务器延迟", "--ms", out lblPingLeft);

            Panel pc2 = MakeCard(sw, sh, sw + 14, sy);
            left.Controls.Add(pc2);
            AddCardContent(pc2, "在线人数", "--/128", out lblOnlineLeft);

            // 运行提示
            int ty = sy + sh + 16;
            int th = h - ty;
            Panel ptip = MakeCard(leftW, th, 0, ty);
            left.Controls.Add(ptip);
            Label ltip = new Label
            {
                Text = "运行提示", ForeColor = FG_PRIMARY, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 9F, FontStyle.Bold), AutoSize = true,
                Location = new Point(22, 18)
            };
            ptip.Controls.Add(ltip);
            lblInstallStatus = new Label
            {
                Text = "● 检测中...", ForeColor = FG_SECONDARY, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 9F), AutoSize = true, Location = new Point(22, 44)
            };
            ptip.Controls.Add(lblInstallStatus);
            lblInstallHint = new Label
            {
                Text = "本启动器会自动检测 FiveM 安装路径，请确保已安装 FiveM。",
                ForeColor = FG_SECONDARY, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 9F), AutoSize = true,
                MaximumSize = new Size(leftW - 44, 0), Location = new Point(22, 64)
            };
            ptip.Controls.Add(lblInstallHint);

            // 右侧快捷面板
            Panel right = MakeCard(rightW, h, leftW + 16, 0);
            right.Tag = "dyn";
            host.Controls.Add(right);

            Label qht = new Label
            {
                Text = "快捷操作", ForeColor = FG_PRIMARY,
                Font = new Font("微软雅黑", 11F, FontStyle.Bold), AutoSize = true,
                Location = new Point(24, 22)
            };
            right.Controls.Add(qht);

            Panel tagp = MakePill("操作面板", ACCENT, Color.FromArgb(70, 42, 22));
            tagp.Location = new Point(rightW - tagp.Width - 24, 18);
            right.Controls.Add(tagp);

            Panel ln1 = new Panel { Size = new Size(rightW - 48, 1), BackColor = BORDER_COLOR, Location = new Point(24, 62) };
            right.Controls.Add(ln1);

            // 延迟行
            Label lp1 = new Label
            {
                Text = "服务器延迟", ForeColor = FG_SECONDARY, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 9F), AutoSize = true,
                Location = new Point(24, 88)
            };
            right.Controls.Add(lp1);
            Panel pill1 = MakePillSmall("--ms", FG_SECONDARY, BG_INPUT);
            pill1.Location = new Point(rightW - pill1.Width - 24, 85);
            lblPingRight = (Label)pill1.Controls[0];
            right.Controls.Add(pill1);

            // 在线行
            Label lp2 = new Label
            {
                Text = "在线人数", ForeColor = FG_SECONDARY, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 9F), AutoSize = true,
                Location = new Point(24, 130)
            };
            right.Controls.Add(lp2);
            Panel pill2 = MakePillSmall("--/128", ACCENT_GREEN, BG_INPUT);
            pill2.Location = new Point(rightW - pill2.Width - 24, 127);
            lblOnlineRight = (Label)pill2.Controls[0];
            right.Controls.Add(pill2);

            Panel ln2 = new Panel { Size = new Size(rightW - 48, 1), BackColor = BORDER_COLOR, Location = new Point(24, 170) };
            right.Controls.Add(ln2);

            btnStart = new Button
            {
                Text = "启动游戏", Size = new Size(rightW - 48, 58), Location = new Point(24, 200),
                BackColor = ACCENT, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Font = new Font("微软雅黑", 12F, FontStyle.Bold), Cursor = Cursors.Hand
            };
            btnStart.FlatAppearance.BorderSize = 0;
            btnStart.Click += BtnStart_Click;
            btnStart.MouseEnter += (s, e) => btnStart.BackColor = ACCENT_HOVER;
            btnStart.MouseLeave += (s, e) => btnStart.BackColor = ACCENT;
            right.Controls.Add(btnStart);

            Label under = new Label
            {
                Text = "※ 关闭启动器将自动关闭 FiveM",
                ForeColor = FG_FAINT, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 8F), AutoSize = true,
                Location = new Point(24, 272)
            };
            right.Controls.Add(under);

            Label bot = new Label
            {
                Text = "connect " + SERVER_CONNECT_CODE,
                ForeColor = FG_FAINT, BackColor = Color.Transparent,
                Font = new Font("Consolas", 7.5F), AutoSize = true
            };
            bot.Location = new Point(rightW - bot.PreferredWidth - 24, h - 24);
            right.Controls.Add(bot);

            host.ResumeLayout();
            UpdateFiveMStatus();
        }

        // ---------- 页面2：更新日志（远程加载） ----------
        private RichTextBox rtbLog;
        private Label lblChangelogStatus;

        private void BuildChangelogPage(Panel host)
        {
            Label hd = new Label
            {
                Text = "更新日志", ForeColor = FG_PRIMARY,
                Font = new Font("微软雅黑", 11F, FontStyle.Bold), AutoSize = true,
                Location = new Point(0, 0)
            };
            host.Controls.Add(hd);

            Button btnRefresh = new Button
            {
                Text = "⟳  刷新", Size = new Size(96, 32), FlatStyle = FlatStyle.Flat,
                BackColor = BG_CARD, ForeColor = FG_PRIMARY, Cursor = Cursors.Hand,
                Font = new Font("微软雅黑", 9F), Location = new Point(host.ClientSize.Width - 96, -4)
            };
            btnRefresh.FlatAppearance.BorderSize = 0;
            btnRefresh.Region = RoundedRegion(96, 32, 8);
            btnRefresh.Click += (s, e) => RefreshChangelog();
            btnRefresh.MouseEnter += (s, e) => btnRefresh.BackColor = BG_CARD_HOVER;
            btnRefresh.MouseLeave += (s, e) => btnRefresh.BackColor = BG_CARD;
            host.Controls.Add(btnRefresh);
            host.Resize += (s, e) => btnRefresh.Location = new Point(host.ClientSize.Width - 96, -4);

            Panel tag = MakePill("远程自动更新", ACCENT, Color.FromArgb(70, 42, 22));
            tag.Location = new Point(host.ClientSize.Width - 96 - tag.Width - 10, 0);
            host.Controls.Add(tag);
            host.Resize += (s, e) => tag.Location = new Point(host.ClientSize.Width - 96 - tag.Width - 10, 0);

            Panel card = MakeCard(host.ClientSize.Width, host.ClientSize.Height - 44, 0, 44);
            host.Controls.Add(card);
            host.Resize += (s, e) => card.Size = new Size(host.ClientSize.Width, host.ClientSize.Height - 44);

            lblChangelogStatus = new Label
            {
                Text = "加载中...", ForeColor = FG_SECONDARY, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 8.5F), AutoSize = true,
                Location = new Point(18, 14)
            };
            card.Controls.Add(lblChangelogStatus);

            rtbLog = new RichTextBox
            {
                BackColor = BG_INPUT,
                ForeColor = FG_PRIMARY,
                BorderStyle = BorderStyle.None,
                Font = new Font("微软雅黑", 9.5F),
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                DetectUrls = true,
                Size = new Size(card.ClientSize.Width - 36, card.ClientSize.Height - 56),
                Location = new Point(18, 40)
            };
            rtbLog.LinkClicked += (s, e) =>
            {
                try { Process.Start(e.LinkText); } catch { }
            };
            card.Controls.Add(rtbLog);
            card.Resize += (s, e) => rtbLog.Size = new Size(card.ClientSize.Width - 36, card.ClientSize.Height - 56);

            // 本地默认更新日志（远程拉不到时显示这个）
            SetChangelogDefault();
        }

        private void SetChangelogDefault()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("【" + BRAND_FULL + " 启动器 更新日志】");
            sb.AppendLine("——————————————————————————");
            sb.AppendLine("[ " + DateTime.Now.ToString("yyyy-MM-dd") + " ]   " + BRAND_VER);
            sb.AppendLine("  · 全新秋叶暖色调极简 UI");
            sb.AppendLine("  · 支持启动面板 / 更新日志 / 服务状态 / 社群入口 四个页面");
            sb.AppendLine("  · 更新日志支持远程 URL 加载（在 CHANGELOG_URL 配置）");
            sb.AppendLine("  · 修复 FiveM 自定义安装目录检测");
            sb.AppendLine("  · 关闭启动器自动关闭 FiveM");
            sb.AppendLine("");
            sb.AppendLine("提示：管理员只需要把 changelog.txt 放到远程 URL，" + LAUNCHER_SUBTITLE_CN + " 就会自动读取最新内容。");
            sb.AppendLine("远程文本格式（每行一条，建议时间倒序）：");
            sb.AppendLine("  [2026-08-29] 增加什么功能");
            sb.AppendLine("  [2026-08-28] 修复什么问题");
            rtbLog.Text = sb.ToString();
        }

        private void RefreshChangelog()
        {
            if (lblChangelogStatus != null) lblChangelogStatus.Text = "正在从远程拉取...";
            if (string.IsNullOrEmpty(CHANGELOG_URL))
            {
                if (lblChangelogStatus != null)
                    lblChangelogStatus.Text = "未配置 CHANGELOG_URL，显示本地默认日志。只改 Launcher.cs 顶部常量即可远程。";
                SetChangelogDefault();
                return;
            }
            // 后台线程拉取
            ThreadPool.QueueUserWorkItem(delegate
            {
                string text = null;
                string statusText = null;
                try
                {
                    using (WebClient wc = new WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        wc.Headers["User-Agent"] = BRAND_FULL + "-Launcher/" + BRAND_VER;
                        text = wc.DownloadString(CHANGELOG_URL);
                        statusText = "更新完成 · " + DateTime.Now.ToString("HH:mm:ss") + " · 来源：远程";
                    }
                }
                catch (Exception ex)
                {
                    statusText = "远程拉取失败：" + ex.Message + "（使用本地默认）";
                }
                this.BeginInvoke((Action)delegate
                {
                    if (lblChangelogStatus != null)
                        lblChangelogStatus.Text = statusText;
                    if (!string.IsNullOrEmpty(text))
                    {
                        rtbLog.Text = "【" + BRAND_FULL + " 更新日志】\n——————————————————————————\n" + text;
                    }
                    else
                    {
                        SetChangelogDefault();
                    }
                });
            });
        }

        // ---------- 页面3：服务状态 ----------
        private Label lblStateVal;
        private Label lblStateIP;
        private Label lblStatePing;
        private Button btnRefreshStatus;
        private Label lblFiveMPathStatus;

        private void BuildStatusPage(Panel host)
        {
            Label hd = new Label
            {
                Text = "服务状态", ForeColor = FG_PRIMARY,
                Font = new Font("微软雅黑", 11F, FontStyle.Bold), AutoSize = true,
                Location = new Point(0, 0)
            };
            host.Controls.Add(hd);

            btnRefreshStatus = new Button
            {
                Text = "⟳  重新检测", Size = new Size(110, 32), FlatStyle = FlatStyle.Flat,
                BackColor = BG_CARD, ForeColor = FG_PRIMARY, Cursor = Cursors.Hand,
                Font = new Font("微软雅黑", 9F), Location = new Point(host.ClientSize.Width - 110, -4)
            };
            btnRefreshStatus.FlatAppearance.BorderSize = 0;
            btnRefreshStatus.Region = RoundedRegion(110, 32, 8);
            btnRefreshStatus.Click += (s, e) =>
            {
                UpdateFiveMStatus();
                RefreshStatus();
            };
            btnRefreshStatus.MouseEnter += (s, e) => btnRefreshStatus.BackColor = BG_CARD_HOVER;
            btnRefreshStatus.MouseLeave += (s, e) => btnRefreshStatus.BackColor = BG_CARD;
            host.Controls.Add(btnRefreshStatus);
            host.Resize += (s, e) => btnRefreshStatus.Location = new Point(host.ClientSize.Width - 110, -4);

            int w = host.ClientSize.Width;
            int h = host.ClientSize.Height - 44;
            int half = (w - 16) / 2;

            // 服务器状态卡
            Panel c1 = MakeCard(half, h, 0, 44);
            host.Controls.Add(c1);
            host.Resize += (s, e) =>
            {
                w = host.ClientSize.Width;
                h = host.ClientSize.Height - 44;
                half = (w - 16) / 2;
                c1.Size = new Size(half, h);
            };

            AddSectionTitle(c1, "服务器连接性");
            int cy = 50;
            AddStatusRow(c1, "服务器地址", SERVER_IP + ":" + SERVER_PORT, ref cy, out lblStateIP);
            AddStatusRow(c1, "连接状态", "检测中...", ref cy, out lblStateVal);
            AddStatusRow(c1, "延迟 (Ping)", "-- ms", ref cy, out lblStatePing);

            AddSectionTitle(c1, "FiveM 客户端", cy + 8);
            cy += 36;
            Label l;
            AddStatusRow(c1, "安装状态", "--", ref cy, out l);
            // 每次刷新绑定到同一个显示
            lblFiveMPathStatus = l;

            // 直连码卡
            Panel c2 = MakeCard(half, h, half + 16, 44);
            host.Controls.Add(c2);
            host.Resize += (s, e) =>
            {
                c2.Location = new Point(half + 16, 44);
                c2.Size = new Size(host.ClientSize.Width - half - 16, h);
            };
            AddSectionTitle(c2, "服务器直连");
            Label tip = new Label
            {
                Text = "在 FiveM 客户端按 F8 打开控制台，输入：",
                ForeColor = FG_SECONDARY, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 9F), AutoSize = true, Location = new Point(24, 54)
            };
            c2.Controls.Add(tip);

            Panel cmdCard = new Panel
            {
                Size = new Size(half - 48, 54),
                Location = new Point(24, 82),
                BackColor = BG_INPUT
            };
            cmdCard.Region = RoundedRegion(cmdCard.Width, cmdCard.Height, 8);
            cmdCard.Paint += (s, e) => DrawThinBorder(cmdCard, e.Graphics);
            Label cmdText = new Label
            {
                Text = "connect " + SERVER_CONNECT_CODE,
                ForeColor = ACCENT, BackColor = Color.Transparent,
                Font = new Font("Consolas", 14F, FontStyle.Bold), AutoSize = true
            };
            cmdText.Location = new Point(18, (cmdCard.Height - cmdText.PreferredHeight) / 2);
            cmdCard.Controls.Add(cmdText);
            c2.Controls.Add(cmdCard);
            host.Resize += (s, e) =>
            {
                int newW = c2.ClientSize.Width - 48;
                if (newW < 100) newW = 100;
                cmdCard.Size = new Size(newW, 54);
            };

            Button cp = new Button
            {
                Text = "复制连接码", Size = new Size(130, 36), FlatStyle = FlatStyle.Flat,
                BackColor = ACCENT, ForeColor = Color.White, Cursor = Cursors.Hand,
                Font = new Font("微软雅黑", 9.5F, FontStyle.Bold),
                Location = new Point(24, 152)
            };
            cp.FlatAppearance.BorderSize = 0;
            cp.Region = RoundedRegion(130, 36, 8);
            cp.Click += (s, e) =>
            {
                try { Clipboard.SetText("connect " + SERVER_CONNECT_CODE); cp.Text = "✓ 已复制"; cp.BackColor = ACCENT_GREEN; }
                catch { cp.Text = "复制失败"; }
                ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(1400); this.BeginInvoke((Action)delegate { cp.Text = "复制连接码"; cp.BackColor = ACCENT; }); });
            };
            cp.MouseEnter += (s, e) => { if (cp.BackColor != ACCENT_GREEN) cp.BackColor = ACCENT_HOVER; };
            cp.MouseLeave += (s, e) => { if (cp.BackColor != ACCENT_GREEN) cp.BackColor = ACCENT; };
            c2.Controls.Add(cp);

            Button js = new Button
            {
                Text = "▶ 启动游戏 & 连接", Size = new Size(half - 48, 52), FlatStyle = FlatStyle.Flat,
                BackColor = BG_CARD_HOVER, ForeColor = FG_PRIMARY, Cursor = Cursors.Hand,
                Font = new Font("微软雅黑", 11F, FontStyle.Bold), Location = new Point(24, 204)
            };
            js.FlatAppearance.BorderSize = 0;
            js.Region = RoundedRegion(js.Width, 52, 10);
            js.Click += (s, e) =>
            {
                if (btnStart != null) BtnStart_Click(btnStart, EventArgs.Empty);
            };
            js.MouseEnter += (s, e) => js.BackColor = ACCENT;
            js.MouseLeave += (s, e) => js.BackColor = BG_CARD_HOVER;
            c2.Controls.Add(js);
            host.Resize += (s, e) =>
            {
                int newW = c2.ClientSize.Width - 48;
                if (newW < 100) newW = 100;
                js.Size = new Size(newW, 52);
            };
        }

        private void AddSectionTitle(Control c, string text) { AddSectionTitle(c, text, 20); }
        private void AddSectionTitle(Control c, string text, int y)
        {
            Label s = new Label
            {
                Text = text, ForeColor = FG_FAINT, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 8.5F, FontStyle.Bold), AutoSize = true,
                Location = new Point(24, y)
            };
            c.Controls.Add(s);
            Panel ln = new Panel { Size = new Size(c.ClientSize.Width - 48, 1), BackColor = BORDER_COLOR, Location = new Point(24, y + 20) };
            c.Controls.Add(ln);
        }

        private void AddStatusRow(Control host, string label, string value, ref int y, out Label valRef)
        {
            Label la = new Label
            {
                Text = label, ForeColor = FG_SECONDARY, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 9F), AutoSize = true, Location = new Point(24, y)
            };
            host.Controls.Add(la);
            valRef = new Label
            {
                Text = value, ForeColor = FG_PRIMARY, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 10F, FontStyle.Bold), AutoSize = true,
                Location = new Point(host.ClientSize.Width - 24, y)
            };
            valRef.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            host.Controls.Add(valRef);
            y += 30;
        }

        private void RefreshStatus()
        {
            if (lblStateVal != null) lblStateVal.Text = "检测中...";
            if (lblStatePing != null) lblStatePing.Text = "-- ms";

            // 同步展示 FiveM 安装状态
            string exe = FindFiveMExecutable();
            if (lblFiveMPathStatus != null)
            {
                if (!string.IsNullOrEmpty(exe))
                {
                    lblFiveMPathStatus.Text = "✓ 已安装";
                    lblFiveMPathStatus.ForeColor = ACCENT_GREEN;
                    ToolTip tt = new ToolTip();
                    tt.SetToolTip(lblFiveMPathStatus, exe);
                }
                else
                {
                    lblFiveMPathStatus.Text = "未检测到";
                    lblFiveMPathStatus.ForeColor = ACCENT_RED;
                }
            }

            // 后台探测
            ThreadPool.QueueUserWorkItem(delegate
            {
                string state = "未知", pingS = "-- ms";
                try
                {
                    // TCP 探测
                    using (TcpClient tcp = new TcpClient())
                    {
                        IAsyncResult ar = tcp.BeginConnect(SERVER_IP, SERVER_PORT, null, null);
                        WaitHandle wh = ar.AsyncWaitHandle;
                        bool ok = wh.WaitOne(3000);
                        if (ok && tcp.Connected)
                        {
                            state = "在线";
                            try { tcp.EndConnect(ar); } catch { }
                        }
                        else
                        {
                            state = "离线 / 端口未开放";
                        }
                        wh.Close();
                    }
                }
                catch (Exception ex)
                {
                    state = "异常：" + ex.Message;
                }
                try
                {
                    using (Ping p = new Ping())
                    {
                        PingReply r = p.Send(SERVER_IP, 3000);
                        if (r.Status == IPStatus.Success) pingS = r.RoundtripTime + " ms";
                        else pingS = "超时";
                    }
                }
                catch { pingS = "不可达"; }

                this.BeginInvoke((Action)delegate
                {
                    if (lblStateVal != null)
                    {
                        lblStateVal.Text = state;
                        lblStateVal.ForeColor = (state == "在线") ? ACCENT_GREEN : ACCENT_RED;
                    }
                    if (lblStatePing != null)
                    {
                        lblStatePing.Text = pingS;
                        lblStatePing.ForeColor = (pingS.EndsWith("ms") && !pingS.StartsWith("--")) ? ACCENT_GREEN : FG_FAINT;
                    }
                    // 顺便同步启动面板里的显示
                    if (lblPingLeft  != null) { lblPingLeft.Text  = pingS; lblPingLeft.ForeColor = lblStatePing.ForeColor; }
                    if (lblPingRight != null) { lblPingRight.Text = pingS; lblPingRight.ForeColor = lblStatePing.ForeColor; }
                });
            });
        }

        // ---------- 页面4：社群入口 ----------
        private void BuildCommunityPage(Panel host)
        {
            Label hd = new Label
            {
                Text = "社群入口", ForeColor = FG_PRIMARY,
                Font = new Font("微软雅黑", 11F, FontStyle.Bold), AutoSize = true,
                Location = new Point(0, 0)
            };
            host.Controls.Add(hd);

            int w = host.ClientSize.Width;
            int cardW = (w - 32) / 3;
            int cardH = host.ClientSize.Height - 44;

            // QQ群
            Panel p1 = MakeCard(cardW, cardH, 0, 44);
            host.Controls.Add(p1);
            AddCommunityCard(p1, "QQ 群", SOCIAL_QQ_GROUP_NUM, "加群讨论", Color.FromArgb(20, 140, 240),
                delegate
                {
                    if (!string.IsNullOrEmpty(SOCIAL_QQ_GROUP_LINK))
                    {
                        try { Process.Start(SOCIAL_QQ_GROUP_LINK); return; } catch { }
                    }
                    try { Clipboard.SetText(SOCIAL_QQ_GROUP_NUM); MessageBox.Show(this, "QQ群号已复制：" + SOCIAL_QQ_GROUP_NUM, "复制成功", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                    catch { MessageBox.Show(this, "QQ群号：" + SOCIAL_QQ_GROUP_NUM, "QQ群", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                });

            // Discord / 官网
            Panel p2 = MakeCard(cardW, cardH, cardW + 16, 44);
            host.Controls.Add(p2);
            string linkTxt2 = !string.IsNullOrEmpty(SOCIAL_DISCORD) ? SOCIAL_DISCORD : (!string.IsNullOrEmpty(SOCIAL_WEBSITE) ? SOCIAL_WEBSITE : "暂未配置");
            AddCommunityCard(p2, "官方社区", linkTxt2, "打开链接", Color.FromArgb(114, 137, 218),
                delegate
                {
                    string go = !string.IsNullOrEmpty(SOCIAL_DISCORD) ? SOCIAL_DISCORD : SOCIAL_WEBSITE;
                    if (!string.IsNullOrEmpty(go)) { try { Process.Start(go); } catch { MessageBox.Show(this, "无法打开：" + go); } }
                    else { MessageBox.Show(this, "管理员暂未配置此入口。", BRAND_FULL, MessageBoxButtons.OK, MessageBoxIcon.Information); }
                });

            // 微信群
            Panel p3 = MakeCard(cardW, cardH, cardW * 2 + 32, 44);
            host.Controls.Add(p3);
            string wechatTxt = !string.IsNullOrEmpty(SOCIAL_WECHAT_ID) ? SOCIAL_WECHAT_ID : "暂无";
            AddCommunityCard(p3, "微信号", wechatTxt, "复制微信号", Color.FromArgb(10, 190, 90),
                delegate
                {
                    if (string.IsNullOrEmpty(SOCIAL_WECHAT_ID))
                    {
                        MessageBox.Show(this, "管理员暂未配置微信号。", BRAND_FULL, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    try { Clipboard.SetText(SOCIAL_WECHAT_ID); MessageBox.Show(this, "微信号已复制：" + SOCIAL_WECHAT_ID, "复制成功", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                    catch { MessageBox.Show(this, "微信号：" + SOCIAL_WECHAT_ID, "微信", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                });

            host.Resize += (s, e) =>
            {
                w = host.ClientSize.Width;
                cardW = (w - 32) / 3;
                cardH = host.ClientSize.Height - 44;
                p1.Size = new Size(cardW, cardH);
                p2.Location = new Point(cardW + 16, 44); p2.Size = new Size(cardW, cardH);
                p3.Location = new Point(cardW * 2 + 32, 44); p3.Size = new Size(cardW - ((w - 32) % 3), cardH);
            };
        }

        private void AddCommunityCard(Panel p, string title, string value, string btnText, Color accent, Action onClick)
        {
            // 图标圆
            Panel ic = new Panel { Size = new Size(70, 70), BackColor = Color.FromArgb(accent.R / 3, accent.G / 3, accent.B / 3) };
            ic.Location = new Point((p.ClientSize.Width - 70) / 2, 36);
            ic.Region = new Region(new GraphicsPath());
            using (GraphicsPath gp = new GraphicsPath()) { gp.AddEllipse(0, 0, 70, 70); ic.Region = new Region(gp); }
            p.Controls.Add(ic);
            p.Resize += (s, e) => ic.Location = new Point((p.ClientSize.Width - 70) / 2, 36);

            Label sym = new Label
            {
                Text = (title.IndexOf("QQ") >= 0) ? "Q" : (title.IndexOf("社区") >= 0 ? "◎" : "微"),
                ForeColor = accent, BackColor = Color.Transparent,
                Font = new Font("Impact", 30F, FontStyle.Bold), AutoSize = true
            };
            sym.Location = new Point((ic.Width - sym.PreferredWidth) / 2, (ic.Height - sym.PreferredHeight) / 2 - 2);
            ic.Controls.Add(sym);

            Label t = new Label
            {
                Text = title, ForeColor = FG_PRIMARY, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 10.5F, FontStyle.Bold), AutoSize = true
            };
            t.Location = new Point((p.ClientSize.Width - t.PreferredWidth) / 2, 124);
            p.Controls.Add(t);
            p.Resize += (s, e) => t.Location = new Point((p.ClientSize.Width - t.PreferredWidth) / 2, 124);

            Label v = new Label
            {
                Text = value, ForeColor = FG_SECONDARY, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 9.5F), AutoSize = true
            };
            v.Location = new Point((p.ClientSize.Width - v.PreferredWidth) / 2, 150);
            p.Controls.Add(v);
            p.Resize += (s, e) => v.Location = new Point((p.ClientSize.Width - v.PreferredWidth) / 2, 150);

            Button b = new Button
            {
                Text = btnText, Size = new Size(p.ClientSize.Width - 56, 44), FlatStyle = FlatStyle.Flat,
                BackColor = accent, ForeColor = Color.White, Cursor = Cursors.Hand,
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                Location = new Point(28, p.ClientSize.Height - 72)
            };
            b.FlatAppearance.BorderSize = 0;
            b.Region = RoundedRegion(b.Width, 44, 10);
            b.Click += (s, e) => onClick();
            Color norm = accent;
            b.MouseEnter += (s, e) => b.BackColor = Color.FromArgb(
                Math.Min(255, norm.R + 22), Math.Min(255, norm.G + 22), Math.Min(255, norm.B + 22));
            b.MouseLeave += (s, e) => b.BackColor = norm;
            p.Controls.Add(b);
            p.Resize += (s, e) =>
            {
                int bw = p.ClientSize.Width - 56;
                if (bw < 100) bw = 100;
                b.Size = new Size(bw, 44);
                b.Location = new Point(28, p.ClientSize.Height - 72);
                b.Region = RoundedRegion(b.Width, 44, 10);
            };
        }

        // ================================================================
        //  绘制 / 卡 / Pill 辅助
        // ================================================================
        private Panel MakeCard(int w, int h, int x, int y)
        {
            Panel p = new Panel
            {
                Size = new Size(w, h), Location = new Point(x, y),
                BackColor = BG_CARD
            };
            p.Region = RoundedRegion(w, h, 12);
            p.Paint += (s, e) => DrawThinBorder(p, e.Graphics);
            return p;
        }

        private void AddCardContent(Panel card, string title, string value, out Label valueLbl)
        {
            Label lt = new Label
            {
                Text = title, ForeColor = FG_SECONDARY, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 9F), AutoSize = true, Location = new Point(22, 18)
            };
            card.Controls.Add(lt);
            valueLbl = new Label
            {
                Text = value, ForeColor = ACCENT, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 17F, FontStyle.Bold), AutoSize = true,
                Location = new Point(22, 44)
            };
            card.Controls.Add(valueLbl);
        }

        private Panel MakePill(string text, Color fg, Color bg)
        {
            Label lbl = new Label
            {
                Text = text, ForeColor = fg, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 8F, FontStyle.Bold), AutoSize = true
            };
            int hp = 14, vp = 6;
            Panel p = new Panel { BackColor = bg };
            p.Width  = lbl.PreferredWidth  + hp * 2;
            p.Height = lbl.PreferredHeight + vp * 2;
            lbl.Location = new Point(hp, vp);
            p.Controls.Add(lbl);
            p.Region = RoundedRegion(p.Width, p.Height, p.Height / 2);
            return p;
        }

        private Panel MakePillSmall(string text, Color fg, Color bg)
        {
            Label lbl = new Label
            {
                Text = text, ForeColor = fg, BackColor = Color.Transparent,
                Font = new Font("微软雅黑", 8.5F, FontStyle.Bold), AutoSize = true
            };
            int hp = 12, vp = 5;
            Panel p = new Panel { BackColor = bg };
            p.Width  = Math.Max(70, lbl.PreferredWidth + hp * 2);
            p.Height = 26;
            lbl.Location = new Point((p.Width - lbl.PreferredWidth) / 2, (p.Height - lbl.PreferredHeight) / 2);
            p.Controls.Add(lbl);
            p.Region = RoundedRegion(p.Width, p.Height, 8);
            return p;
        }

        private Region RoundedRegion(int w, int h, int r)
        {
            GraphicsPath gp = new GraphicsPath();
            int d = r * 2;
            gp.AddArc(0, 0, d, d, 180, 90);
            gp.AddArc(w - d, 0, d, d, 270, 90);
            gp.AddArc(w - d, h - d, d, d, 0, 90);
            gp.AddArc(0, h - d, d, d, 90, 90);
            gp.CloseAllFigures();
            return new Region(gp);
        }

        private void DrawThinBorder(Control ctl, Graphics g)
        {
            using (Pen p = new Pen(BORDER_COLOR))
            {
                Rectangle r = new Rectangle(0, 0, ctl.Width - 1, ctl.Height - 1);
                GraphicsPath gp = new GraphicsPath();
                int radius = 12; int d = radius * 2;
                gp.AddArc(r.X, r.Y, d, d, 180, 90);
                gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                gp.CloseAllFigures();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawPath(p, gp);
            }
        }

        private Bitmap DrawBrandLogo(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                GraphicsPath gp = new GraphicsPath();
                int radius = (int)(size * 0.22); int d = radius * 2;
                gp.AddArc(0, 0, d, d, 180, 90);
                gp.AddArc(size - d - 1, 0, d, d, 270, 90);
                gp.AddArc(size - d - 1, size - d - 1, d, d, 0, 90);
                gp.AddArc(0, size - d - 1, d, d, 90, 90);
                gp.CloseAllFigures();
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Rectangle(0, 0, size, size),
                    Color.FromArgb(170, 90, 35), ACCENT, 45f))
                {
                    g.FillPath(br, gp);
                }
                using (Font f = new Font("微软雅黑", (int)(size * 0.55), FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, "秋", f, new Rectangle(0, 0, size, size),
                        Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            return bmp;
        }

        // ================================================================
        //  FiveM 检测（保留上次修复的完整版本）
        // ================================================================
        private string FindFiveMExecutable()
        {
            // 1) HKCU\Software\FiveM （最高优先级）
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\FiveM"))
                {
                    if (key != null)
                    {
                        string[] names = key.GetValueNames();
                        foreach (string vn in names)
                        {
                            object ov = key.GetValue(vn);
                            if (ov == null) continue;
                            string sv = ov.ToString();
                            if (sv.EndsWith("FiveM.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(sv)) return sv;
                        }
                        foreach (string vn in names)
                        {
                            object ov = key.GetValue(vn);
                            if (ov == null) continue;
                            string sv = ov.ToString();
                            if (sv.IndexOf("FiveM.app", StringComparison.OrdinalIgnoreCase) >= 0 && Directory.Exists(sv))
                            {
                                DirectoryInfo di = new DirectoryInfo(sv);
                                if (di.Parent != null)
                                {
                                    string exe = Path.Combine(di.Parent.FullName, "FiveM.exe");
                                    if (File.Exists(exe)) return exe;
                                }
                            }
                        }
                    }
                }
            } catch { }

            // 2) CitizenFX（Last Run Location）
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\CitizenFX\FiveM"))
                {
                    if (key != null)
                    {
                        object lastRun = key.GetValue("Last Run Location");
                        if (lastRun != null)
                        {
                            string dir = lastRun.ToString();
                            if (Directory.Exists(dir))
                            {
                                DirectoryInfo di = new DirectoryInfo(dir.TrimEnd('\\'));
                                if (di.Parent != null)
                                {
                                    string exe = Path.Combine(di.Parent.FullName, "FiveM.exe");
                                    if (File.Exists(exe)) return exe;
                                }
                                string exe2 = Path.Combine(dir, "FiveM.exe");
                                if (File.Exists(exe2)) return exe2;
                            }
                        }
                    }
                }
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\CitizenFX\FXDK"))
                {
                    if (key != null)
                    {
                        object ov = key.GetValue("Last Run Location");
                        if (ov != null)
                        {
                            string dir = ov.ToString();
                            if (Directory.Exists(dir))
                            {
                                DirectoryInfo di = new DirectoryInfo(dir.TrimEnd('\\'));
                                if (di.Parent != null)
                                {
                                    string exe = Path.Combine(di.Parent.FullName, "FiveM.exe");
                                    if (File.Exists(exe)) return exe;
                                }
                            }
                        }
                    }
                }
            } catch { }

            // 3) fivem:// 协议（HKCR 与 HKCU\Classes）
            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(@"fivem\shell\open\command"))
                {
                    if (key != null)
                    {
                        object cmd = key.GetValue("");
                        if (cmd != null)
                        {
                            string exe = ExtractExeFromCommand(cmd.ToString().Trim());
                            if (!string.IsNullOrEmpty(exe) && File.Exists(exe)) return exe;
                        }
                    }
                }
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\fivem\shell\open\command"))
                {
                    if (key != null)
                    {
                        object cmd = key.GetValue("");
                        if (cmd != null)
                        {
                            string exe = ExtractExeFromCommand(cmd.ToString().Trim());
                            if (!string.IsNullOrEmpty(exe) && File.Exists(exe)) return exe;
                        }
                    }
                }
            } catch { }

            // 4) 系统常见位置
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData     = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string program     = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programX86  = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string desk        = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string startMenu   = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            List<string> cands = new List<string>
            {
                Path.Combine(localAppData, "FiveM", "FiveM.exe"),
                Path.Combine(appData,     "FiveM", "FiveM.exe"),
                Path.Combine(program,     "FiveM", "FiveM.exe"),
                Path.Combine(programX86,  "FiveM", "FiveM.exe"),
                Path.Combine(userProfile, "FiveM", "FiveM.exe"),
                Path.Combine(desk,        "FiveM.exe"),
                Path.Combine(startMenu,   "Programs", "FiveM", "FiveM.exe"),
            };

            // 5) 所有盘符 × 常见游戏目录
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                string[] subDirs = {
                    "FiveM\\FiveM.exe", "Games\\FiveM\\FiveM.exe", "Game\\FiveM\\FiveM.exe",
                    "游戏\\FiveM\\FiveM.exe", "Steam\\steamapps\\common\\FiveM\\FiveM.exe",
                    "SteamLibrary\\steamapps\\common\\FiveM\\FiveM.exe",
                    "Epic Games\\FiveM\\FiveM.exe", "Rockstar Games\\FiveM\\FiveM.exe",
                    "Program Files\\FiveM\\FiveM.exe"
                };
                foreach (DriveInfo drive in drives)
                {
                    try
                    {
                        if (drive.DriveType == DriveType.CDRom || drive.DriveType == DriveType.Network
                            || drive.DriveType == DriveType.NoRootDirectory || !drive.IsReady) continue;
                        string root = drive.RootDirectory.FullName;
                        foreach (string sd in subDirs)
                        {
                            string c = Path.Combine(root, sd);
                            if (File.Exists(c)) return c;
                            cands.Add(c);
                        }
                        string direct = Path.Combine(root, "FiveM.exe");
                        if (File.Exists(direct)) return direct;
                    } catch { }
                }
            } catch { }

            // 6) 注册表卸载信息
            string[] unins = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (string hive in unins)
            {
                foreach (RegistryHive rh in new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(rh, RegistryView.Default))
                        using (RegistryKey uk = baseKey.OpenSubKey(hive))
                        {
                            if (uk != null)
                            {
                                foreach (string sub in uk.GetSubKeyNames())
                                {
                                    try
                                    {
                                        using (RegistryKey k = uk.OpenSubKey(sub))
                                        {
                                            if (k == null) continue;
                                            object dn = k.GetValue("DisplayName");
                                            if (dn == null || dn.ToString().IndexOf("FiveM", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                            object loc = k.GetValue("InstallLocation");
                                            object ic = k.GetValue("DisplayIcon");
                                            object us = k.GetValue("UninstallString");
                                            string[] tries = {
                                                loc != null ? Path.Combine(loc.ToString(), "FiveM.exe") : null,
                                                ic  != null ? ic.ToString().Split(',')[0].Trim().Trim('"') : null,
                                                us  != null ? ExtractExeFromCommand(us.ToString()) : null
                                            };
                                            foreach (string t in tries)
                                                if (!string.IsNullOrEmpty(t) && File.Exists(t)) return t;
                                        }
                                    } catch { }
                                }
                            }
                        }
                    } catch { }
                }
            }

            foreach (string c in cands) if (File.Exists(c)) return c;
            return null;
        }

        private static string ExtractExeFromCommand(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            raw = raw.Trim();
            if (raw.StartsWith("\""))
            {
                int end = raw.IndexOf('"', 1);
                if (end > 0) return raw.Substring(1, end - 1);
            }
            int s = raw.IndexOf(' ');
            return s >= 0 ? raw.Substring(0, s) : raw;
        }

        private bool HasFiveMProtocol()
        {
            try { using (RegistryKey k = Registry.ClassesRoot.OpenSubKey(@"fivem\shell\open\command")) if (k != null && k.GetValue("") != null) return true; } catch { }
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\fivem\shell\open\command")) if (k != null && k.GetValue("") != null) return true; } catch { }
            return false;
        }

        private void UpdateFiveMStatus()
        {
            if (lblInstallStatus == null || btnStart == null) return;
            string exe = FindFiveMExecutable();
            if (!string.IsNullOrEmpty(exe))
            {
                lblInstallStatus.Text = "● FiveM 已安装（已定位）";
                lblInstallStatus.ForeColor = ACCENT_GREEN;
                lblInstallHint.Text = "路径：" + exe + "\n点击右侧「启动游戏」即可自动连接 connect " + SERVER_CONNECT_CODE + "。";
                btnStart.Enabled = true; btnStart.BackColor = ACCENT; btnStart.Cursor = Cursors.Hand;
            }
            else if (HasFiveMProtocol())
            {
                lblInstallStatus.Text = "● FiveM 已安装（协议方式）";
                lblInstallStatus.ForeColor = ACCENT_GREEN;
                lblInstallHint.Text = "检测到 fivem:// 协议可用，将通过协议方式连接服务器。";
                btnStart.Enabled = true; btnStart.BackColor = ACCENT; btnStart.Cursor = Cursors.Hand;
            }
            else
            {
                lblInstallStatus.Text = "● 未检测到 FiveM";
                lblInstallStatus.ForeColor = ACCENT_RED;
                lblInstallHint.Text = "本机未检测到 FiveM 客户端。\n请访问 https://fivem.net 下载并安装后重试。";
                btnStart.Enabled = false;
                btnStart.BackColor = Color.FromArgb(90, 86, 104);
                btnStart.Cursor = Cursors.Default;
            }
            // 同步服务状态页
            if (lblFiveMPathStatus != null)
            {
                if (!string.IsNullOrEmpty(exe))
                {
                    lblFiveMPathStatus.Text = "✓ 已安装";
                    lblFiveMPathStatus.ForeColor = ACCENT_GREEN;
                }
                else
                {
                    lblFiveMPathStatus.Text = "未检测到";
                    lblFiveMPathStatus.ForeColor = ACCENT_RED;
                }
            }
        }

        // ================================================================
        //  启动 / 关闭
        // ================================================================
        private void BtnStart_Click(object sender, EventArgs e)
        {
            btnStart.Enabled = false;
            string prev = btnStart.Text;
            btnStart.Text = "正在启动...";
            Application.DoEvents();

            bool ok = false;
            try { ok = TryLaunchFiveM(); }
            catch (Exception ex) { MessageBox.Show(this, "启动出错：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }

            if (ok)
            {
                _fivemLaunchedByUs = true;
                btnStart.Text = "游戏运行中";
                btnStart.BackColor = Color.FromArgb(175, 98, 48);
                if (lblInstallStatus != null)
                {
                    lblInstallStatus.Text = "● FiveM 已启动，连接中...";
                    lblInstallStatus.ForeColor = ACCENT_GREEN;
                }
                btnStart.Enabled = true; // 保留可点击，只是切换显示
            }
            else
            {
                MessageBox.Show(this,
                    "无法启动 FiveM 客户端。\n请确认 FiveM 已正确安装，或手动在 FiveM 控制台（F8）输入：connect " + SERVER_CONNECT_CODE +
                    "\n\n如还未安装，请访问 https://fivem.net 下载。",
                    "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnStart.Text = prev;
                UpdateFiveMStatus();
            }
        }

        private bool TryLaunchFiveM()
        {
            // ======== 修复 FiveM 启动报错 "This application should be launched directly from the shell or a web browser"
            // FiveM 官方强制要求：要么通过 fivem:// 协议（浏览器方式）启动，要么通过 Shell（资源管理器）方式启动
            // 所以优先走协议方式；找不到协议才走 EXE，且 EXE 方式必须 UseShellExecute=true
            // ==================================================================

            // 方式一（首选）：fivem:// 协议启动，FiveM 官方推荐、100% 兼容
            if (HasFiveMProtocol())
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "fivem://connect/" + SERVER_CONNECT_CODE,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    // 协议启动无法直接拿到真实 Process，我们返回 true 让状态变成"运行中"
                    // 并开始轮询监控 FiveM 进程是否出现
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        Thread.Sleep(3000); // 等协议启动器拉起 FiveM.exe
                        bool found = false;
                        for (int i = 0; i < 10; i++)
                        {
                            try
                            {
                                Process[] procs = Process.GetProcessesByName("FiveM");
                                if (procs != null && procs.Length > 0)
                                {
                                    found = true;
                                    foreach (Process p in procs)
                                    {
                                        try
                                        {
                                            if (_fivemProcess == null || _fivemProcess.HasExited)
                                            {
                                                p.EnableRaisingEvents = true;
                                                p.Exited += (s, ev) => this.BeginInvoke((Action)delegate
                                                {
                                                    _fivemLaunchedByUs = false; _fivemProcess = null;
                                                    if (btnStart != null) { btnStart.Text = "启动游戏"; btnStart.BackColor = ACCENT; }
                                                    UpdateFiveMStatus();
                                                });
                                                _fivemProcess = p;
                                                break;
                                            }
                                        } catch { }
                                    }
                                    break;
                                }
                            } catch { }
                            Thread.Sleep(1500);
                        }
                        _fivemLaunchedByUs = found;
                    });
                    return true;
                } catch { }
            }

            // 方式二（兜底）：直接启动 FiveM.exe，但必须走 Shell 模式 + 设置 WorkingDirectory
            string exe = FindFiveMExecutable();
            if (!string.IsNullOrEmpty(exe))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName         = exe,
                        Arguments        = "+connect " + SERVER_CONNECT_CODE,
                        UseShellExecute  = true,         // 必须 true，否则 FiveM 报 shell/browser 错误
                        WorkingDirectory = Path.GetDirectoryName(exe)
                    };
                    _fivemProcess = Process.Start(psi);
                    if (_fivemProcess != null)
                    {
                        _fivemProcess.EnableRaisingEvents = true;
                        _fivemProcess.Exited += (s, ev) => this.BeginInvoke((Action)delegate
                        {
                            _fivemLaunchedByUs = false; _fivemProcess = null;
                            if (btnStart != null) { btnStart.Text = "启动游戏"; btnStart.BackColor = ACCENT; }
                            UpdateFiveMStatus();
                        });
                        return true;
                    }
                } catch { }
            }
            return false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            KillFiveMProcesses();
        }

        private void KillFiveMProcesses()
        {
            try
            {
                if (_fivemLaunchedByUs && _fivemProcess != null && !_fivemProcess.HasExited)
                {
                    try { _fivemProcess.Kill(); } catch { }
                }
                string[] names = {
                    "FiveM",
                    "FiveM_b1604", "FiveM_b2060", "FiveM_b2189", "FiveM_b2372",
                    "FiveM_b2545", "FiveM_b2612", "FiveM_b2699", "FiveM_b2802",
                    "FiveM_b2944", "FiveM_b3095", "FiveM_b3258", "FiveM_b3323",
                    "FiveM_b3407", "FiveM_b3570", "FiveM_b3751", "FiveM_b3788"
                };
                foreach (string n in names)
                {
                    try
                    {
                        Process[] procs = Process.GetProcessesByName(n);
                        foreach (Process p in procs) { try { if (!p.HasExited) p.Kill(); } catch { } }
                    } catch { }
                }
            } catch { }
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm());
        }
    }
}
