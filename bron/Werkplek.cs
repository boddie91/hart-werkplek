// Hart Werkplek - schermvullend werkprogramma met ingebouwde browsers.
// Geschreven in C# 5 zodat het met de standaard Windows-compiler (csc) te bouwen is.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        // Scherp beeld: Windows niet laten uitrekken, maar zelf op de echte schermresolutie tekenen
        try { if (!SetProcessDpiAwarenessContext(new IntPtr(-2))) SetProcessDPIAware(); }
        catch { try { SetProcessDPIAware(); } catch { } }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

class Tab
{
    public string Key;
    public string Label;
    public string Url;
    public Button Btn;
    public WebView2 View;
    public Task Ready;
    public bool Fixed;              // eigen pagina van het programma, niet instelbaar
    public Control Host;            // wat getoond/verborgen wordt (browser of gesplitst scherm)
    public SplitContainer Split;    // alleen bij tabbladen met een zijvenster
    public FlowLayoutPanel Strip;
    public Panel PaneBody;
    public List<PaneTab> Pane = new List<PaneTab>();
}

class PaneTab
{
    public WebView2 View;
    public Panel Chip;
    public Button Btn;
}

class MainForm : Form
{
    static readonly Color Accent = Color.FromArgb(178, 58, 91);
    static readonly Color AccentSoft = Color.FromArgb(248, 230, 236);
    static readonly Color Ink = Color.FromArgb(29, 36, 51);
    static readonly Color Muted = Color.FromArgb(93, 102, 120);
    static readonly Color Line = Color.FromArgb(221, 225, 234);
    static readonly Color Bg = Color.FromArgb(238, 240, 244);
    static readonly Color Chip = Color.FromArgb(246, 247, 250);

    readonly string appDir;
    readonly string webDir;
    readonly string dataDir;
    readonly string clipDir;
    readonly string cfgPath;
    readonly float k; // schaal t.o.v. 100%
    int versie;
    Button btnUpdate;
    const string VersieUrl = "https://raw.githubusercontent.com/boddie91/hart-werkplek/main/bron/versie.txt";

    Dictionary<string, string> cfg = new Dictionary<string, string>();
    readonly List<Tab> order = new List<Tab>();
    readonly Dictionary<string, Tab> tabs = new Dictionary<string, Tab>();

    CoreWebView2Environment env;
    Panel side;
    Panel content;
    Panel clip;
    FlowLayoutPanel clipList;
    Button btnFull;
    Button btnClip;
    Button btnVerslag;
    Panel verslag;
    Splitter verslagSplit;
    WebView2 verslagView;
    Tab current;
    bool isFull;

    const string OutlookScript = @"
(function(){
  var h = location.hostname;
  if (!/outlook\.(office|office365|live)\.com$|outlook\.cloud\.microsoft$/.test(h)) return;
  function scan(){
    try {
      var items = Array.prototype.slice.call(document.querySelectorAll('[role=""listbox""] [role=""option""]')).slice(0, 8);
      var out = [];
      items.forEach(function(el){
        var label = el.getAttribute('aria-label') || '';
        var lines = (el.innerText || '').split('\n').map(function(s){ return s.trim(); }).filter(function(s){ return s.length; });
        if (!lines.length) return;
        out.push({ l: lines.slice(0, 4), u: /ongelezen|unread/i.test(label) });
      });
      if (out.length && window.chrome && chrome.webview) chrome.webview.postMessage('inbox:' + JSON.stringify(out));
    } catch(e) {}
  }
  setTimeout(scan, 6000);
  setInterval(scan, 30000);
})();";

    public MainForm()
    {
        appDir = AppDomain.CurrentDomain.BaseDirectory;
        webDir = Path.Combine(appDir, "web");
        dataDir = Path.Combine(appDir, "gegevens");
        clipDir = Path.Combine(appDir, "klembord");
        cfgPath = Path.Combine(appDir, "instellingen.txt");
        try { int.TryParse(File.ReadAllText(Path.Combine(appDir, "versie.txt")).Trim(), out versie); } catch { }

        using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;

        Text = "Hart Werkplek";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BackColor = Bg;
        Font = new Font("Segoe UI", 10.5f);
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen.WorkingArea;

        Directory.CreateDirectory(clipDir);
        CleanOldClips();
        LoadConfig();
        SyncChecklist();
        BuildTabs();
        BuildUi();
        RefreshClips();
    }

    int S(int px) { return (int)Math.Round(px * k); }

    // ---------- instellingen ----------
    void LoadConfig()
    {
        cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(cfgPath)) return;
        foreach (string raw in File.ReadAllLines(cfgPath, Encoding.UTF8))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            int i = line.IndexOf('=');
            if (i < 1) continue;
            cfg[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
        }
    }

    string Cfg(string key)
    {
        string v;
        return cfg.TryGetValue(key, out v) ? v : "";
    }

    static readonly Dictionary<string, string> Standaard = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "aanmeldpaneel", "https://boddie91.github.io/hart-aanmeldpaneel/taskpane.html" }
    };

    string ResolveUrl(string key, string label)
    {
        string v = Cfg(key);
        if (v.Length == 0 && Standaard.ContainsKey(key)) v = Standaard[key];
        if (v.Length == 0)
            return "https://werkplek.local/leeg.html?tab=" + Uri.EscapeDataString(label) + "&sleutel=" + key;
        if ((v.Length > 2 && v[1] == ':') || v.StartsWith("\\\\"))
            return new Uri(v).AbsoluteUri;
        return v;
    }

    void BuildTabs()
    {
        AddTab("start", "Start", "https://werkplek.local/start.html");
        AddTab("outlook", "Outlook", null);
        AddTab("james", "James", null);
        AddTab("remarkable", "reMarkable", null);
        AddTab("caseload", "Caseload", null);
        AddTab("dsm", "DSM", "https://werkplek.local/dsm.html");
        AddTab("rapportages", "Rapportages", "https://werkplek.local/rapportages.html");
        AddTab("interventies", "Interventies", "https://werkplek.local/interventies.html");
        AddTab("sessieblad", "Sessieblad", "https://werkplek.local/sessieblad.html");
        AddTab("sjablonen", "Sjablonen", null);
    }

    void AddTab(string key, string label, string fixedUrl)
    {
        Tab t = new Tab();
        t.Key = key;
        t.Label = label;
        t.Fixed = fixedUrl != null;
        t.Url = fixedUrl ?? ResolveUrl(key, label);
        order.Add(t);
        tabs[key] = t;
    }

    // ---------- scherm opbouwen ----------
    void BuildUi()
    {
        content = new Panel();
        content.Dock = DockStyle.Fill;
        content.BackColor = Bg;
        Controls.Add(content);

        BuildVerslagPanel();
        BuildClipPanel();

        side = new Panel();
        side.Dock = DockStyle.Left;
        side.Width = S(210);
        side.BackColor = Color.White;
        side.Padding = new Padding(S(10), S(14), S(10), S(14));
        side.Paint += delegate(object s, PaintEventArgs e)
        {
            using (Pen p = new Pen(Line)) e.Graphics.DrawLine(p, side.Width - 1, 0, side.Width - 1, side.Height);
        };
        Controls.Add(side);

        // onderkant: vier icoonknoppen naast elkaar
        tips = new ToolTip();
        TableLayoutPanel onder = new TableLayoutPanel();
        onder.Dock = DockStyle.Bottom;
        onder.Height = S(48);
        onder.ColumnCount = 4;
        onder.RowCount = 1;
        for (int c = 0; c < 4; c++) onder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        onder.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        onder.Margin = new Padding(0);
        onder.Padding = new Padding(0, S(6), 0, 0);

        btnUpdate = IconButton("\uE72C", "Bijwerken");
        btnUpdate.Click += delegate { Bijwerken(); };
        Button btnCfg = IconButton("\uE713", "Instellingen");
        btnCfg.Click += delegate { OpenSettings(); };
        btnFull = IconButton("\uE73F", "Venster (F11)");
        btnFull.Click += delegate { SetFull(!isFull); };
        Button btnExit = IconButton("\uE7E8", "Afsluiten");
        btnExit.Click += delegate { Close(); };
        onder.Controls.Add(btnUpdate, 0, 0);
        onder.Controls.Add(btnCfg, 1, 0);
        onder.Controls.Add(btnFull, 2, 0);
        onder.Controls.Add(btnExit, 3, 0);
        side.Controls.Add(onder);

        // klembord-knop direct onder de tabbladen
        btnClip = SideButton("Klembord");
        btnClip.Dock = DockStyle.Top;
        btnClip.Click += delegate { clip.Visible = !clip.Visible; StyleClipButton(); };
        side.Controls.Add(btnClip);

        btnVerslag = SideButton("Sessieverslag");
        btnVerslag.Dock = DockStyle.Top;
        btnVerslag.Click += delegate { ShowVerslag(!verslag.Visible); };
        side.Controls.Add(btnVerslag);

        Panel gap = new Panel();
        gap.Dock = DockStyle.Top;
        gap.Height = S(14);
        side.Controls.Add(gap);

        // tabbladen (omgekeerd toevoegen bij Dock=Top)
        for (int i = order.Count - 1; i >= 0; i--)
        {
            Tab t = order[i];
            Button b = SideButton(t.Label);
            b.Dock = DockStyle.Top;
            Tab captured = t;
            b.Click += delegate { ShowTab(captured); };
            t.Btn = b;
            side.Controls.Add(b);
        }

        // klok en datum, zichtbaar ook als de taakbalk verborgen is (schermvullend)
        Panel klok = new Panel();
        klok.Dock = DockStyle.Top;
        klok.Height = S(62);
        klok.Padding = new Padding(S(8), 0, 0, S(10));
        Label tijd = new Label();
        tijd.Dock = DockStyle.Top;
        tijd.Height = S(32);
        tijd.Font = new Font("Segoe UI Light", 20f);
        tijd.ForeColor = Ink;
        tijd.TextAlign = ContentAlignment.MiddleLeft;
        Label datum = new Label();
        datum.Dock = DockStyle.Top;
        datum.Height = S(20);
        datum.Font = new Font("Segoe UI", 9.5f);
        datum.ForeColor = Muted;
        datum.TextAlign = ContentAlignment.MiddleLeft;
        klok.Controls.Add(datum);
        klok.Controls.Add(tijd);
        side.Controls.Add(klok);
        CultureInfo nl = new CultureInfo("nl-NL");
        Action zetKlok = delegate
        {
            DateTime nu = DateTime.Now;
            tijd.Text = nu.ToString("HH:mm", nl);
            datum.Text = nu.ToString("dddd d MMMM", nl);
        };
        zetKlok();
        System.Windows.Forms.Timer klokTimer = new System.Windows.Forms.Timer();
        klokTimer.Interval = 1000;
        klokTimer.Tick += delegate { zetKlok(); };
        klokTimer.Start();

        Label title = new Label();
        title.Text = "Hart Werkplek";
        title.Font = new Font("Segoe UI Semibold", 13f);
        title.ForeColor = Accent;
        title.Dock = DockStyle.Top;
        title.Height = S(48);
        title.Padding = new Padding(S(8), 0, 0, S(8));
        title.TextAlign = ContentAlignment.MiddleLeft;
        side.Controls.Add(title);
    }

    ToolTip tips;

    Button IconButton(string glyph, string tip)
    {
        Button b = new Button();
        b.Text = glyph;
        b.Font = new Font("Segoe MDL2 Assets", 13f);
        b.Dock = DockStyle.Fill;
        b.Margin = new Padding(S(2), 0, S(2), 0);
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(244, 245, 248);
        b.TextAlign = ContentAlignment.MiddleCenter;
        b.ForeColor = Muted;
        b.BackColor = Color.White;
        b.Cursor = Cursors.Hand;
        b.AccessibleName = tip;
        tips.SetToolTip(b, tip);
        return b;
    }

    Button SideButton(string text)
    {
        Button b = new Button();
        b.Text = "   " + text;
        b.Height = S(44);
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(244, 245, 248);
        b.TextAlign = ContentAlignment.MiddleLeft;
        b.ForeColor = Ink;
        b.BackColor = Color.White;
        b.Cursor = Cursors.Hand;
        return b;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        SetFull(true);
        try
        {
            Directory.CreateDirectory(dataDir);
            env = await CoreWebView2Environment.CreateAsync(null, dataDir, null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "De ingebouwde browser (Microsoft Edge WebView2) kon niet starten.\n\n" + ex.Message,
                "Hart Werkplek", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        ShowTab(tabs["start"]);
        ZoekUpdate();
        // Outlook alvast op de achtergrond laden, zodat de inbox op de startpagina verschijnt
        try { await EnsureView(tabs["outlook"]); } catch { }
    }

    // ---------- schermvullend ----------
    void SetFull(bool on)
    {
        isFull = on;
        if (on)
        {
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = Screen.FromControl(this).Bounds;
            btnFull.Text = "\uE73F";
            tips.SetToolTip(btnFull, "Venster (F11)");
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Maximized;
            btnFull.Text = "\uE740";
            tips.SetToolTip(btnFull, "Volledig scherm (F11)");
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F11) { SetFull(!isFull); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------- tabbladen ----------
    Task EnsureView(Tab t)
    {
        if (t.Ready == null) t.Ready = CreateView(t);
        return t.Ready;
    }

    async Task CreateView(Tab t)
    {
        WebView2 v = new WebView2();
        v.Dock = DockStyle.Fill;
        v.DefaultBackgroundColor = Color.White;
        t.View = v;
        if (t.Key == "james") BuildSplit(t, v);
        else t.Host = v;
        t.Host.Visible = false;
        content.Controls.Add(t.Host);
        await v.EnsureCoreWebView2Async(env);
        Setup(v.CoreWebView2, t);
        if (t.Key == "outlook")
            await v.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(OutlookScript);
        if (t.Key == "remarkable")
            await v.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(RemarkableScript);
        if (t.Key == "start")
        {
            await v.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(PaneelScript);
            CoreWebView2 core = v.CoreWebView2;
            core.NavigationCompleted += delegate
            {
                string u = Cfg("aanmeldpaneel").Equals("uit", StringComparison.OrdinalIgnoreCase) ? "uit" : ResolveUrl("aanmeldpaneel", "Aanmeldpaneel");
                try { core.PostWebMessageAsString("paneel:" + u); } catch { }
            };
        }
        v.CoreWebView2.Navigate(t.Url);
    }

    async void ShowTab(Tab t)
    {
        if (env == null) return;
        current = t;
        foreach (Tab x in order)
        {
            bool on = x == t;
            x.Btn.BackColor = on ? AccentSoft : Color.White;
            x.Btn.ForeColor = on ? Accent : Ink;
            x.Btn.Font = new Font("Segoe UI", 10.5f, on ? FontStyle.Bold : FontStyle.Regular);
            if (x.Host != null && !on) x.Host.Visible = false;
        }
        try { await EnsureView(t); } catch { return; }
        if (current != t) return;
        t.Host.Visible = true;
        t.Host.BringToFront();
        t.View.Focus();
    }

    void Setup(CoreWebView2 core, Tab owner)
    {
        core.SetVirtualHostNameToFolderMapping("werkplek.local", webDir, CoreWebView2HostResourceAccessKind.Allow);
        if (owner != null && owner.Split != null)
            core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e) { OpenInPane(owner, e); };
        else
            core.NewWindowRequested += OnNewWindow;
        core.WebMessageReceived += OnMessage;
        core.DownloadStarting += OnDownload;
    }

    // Pop-ups (inloggen, bijlagen, invoegtoepassingen) openen in een eigen klein venster
    async void OnNewWindow(object sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        CoreWebView2Deferral def = e.GetDeferral();
        Form f = new Form();
        f.Text = "Hart Werkplek";
        f.Icon = Icon;
        f.StartPosition = FormStartPosition.CenterScreen;
        f.Size = new Size(S(1000), S(760));
        if (e.WindowFeatures.HasSize)
            f.ClientSize = new Size(Math.Max(S(400), S((int)e.WindowFeatures.Width)), Math.Max(S(300), S((int)e.WindowFeatures.Height)));
        WebView2 v = new WebView2();
        v.Dock = DockStyle.Fill;
        f.Controls.Add(v);
        f.Show(this);
        try
        {
            await v.EnsureCoreWebView2Async(env);
            v.CoreWebView2.NewWindowRequested += OnNewWindow;
            v.CoreWebView2.DownloadStarting += OnDownload;
            v.CoreWebView2.WindowCloseRequested += delegate { f.Close(); };
            v.CoreWebView2.DocumentTitleChanged += delegate { f.Text = v.CoreWebView2.DocumentTitle; };
            e.NewWindow = v.CoreWebView2;
            e.Handled = true;
        }
        finally
        {
            def.Complete();
        }
    }

    static bool IsOutlookHost(string url)
    {
        Uri u;
        if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
        string h = u.Host.ToLowerInvariant();
        return h == "outlook.office.com" || h == "outlook.office365.com" || h == "outlook.live.com" || h == "outlook.cloud.microsoft";
    }

    void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string m;
        try { m = e.TryGetWebMessageAsString(); } catch { return; }
        if (m == null) return;
        string src = e.Source ?? "";

        // inbox-overzicht: alleen aannemen van Outlook zelf, doorgeven aan de startpagina
        if (m.StartsWith("inbox:"))
        {
            if (!IsOutlookHost(src)) return;
            Tab st = tabs["start"];
            if (st.View != null && st.View.CoreWebView2 != null)
                st.View.CoreWebView2.PostWebMessageAsString(m);
            return;
        }

        // opdrachten alleen van de eigen pagina's van het programma
        if (!src.StartsWith("https://werkplek.local/")) return;

        if (m.StartsWith("tab:"))
        {
            Tab t;
            if (tabs.TryGetValue(m.Substring(4), out t)) ShowTab(t);
        }
        else if (m.StartsWith("open:"))
        {
            string rest = m.Substring(5);
            int i = rest.IndexOf('|');
            if (i < 0) return;
            Tab t;
            if (!tabs.TryGetValue(rest.Substring(0, i), out t)) return;
            string url = rest.Substring(i + 1);
            if (t.View != null && t.View.CoreWebView2 != null) t.View.CoreWebView2.Navigate(url);
            else t.Url = url;
            ShowTab(t);
        }
        else if (m == "instellingen")
        {
            BeginInvoke(new Action(OpenSettings));
        }
        else if (m == "checklist")
        {
            Tab d = tabs["dsm"];
            if (d.View != null && d.View.CoreWebView2 != null) d.View.CoreWebView2.PostWebMessageAsString("toon-checklist");
            else d.Url = "https://werkplek.local/dsm.html#checklist";
            ShowTab(d);
        }
        else if (m == "kies-checklist")
        {
            CoreWebView2 core = sender as CoreWebView2;
            BeginInvoke(new Action(delegate { KiesChecklist(core); }));
        }
        else if (m == "verslag")
        {
            BeginInvoke(new Action(delegate { ShowVerslag(true); }));
        }
        else if (m.StartsWith("pdf:"))
        {
            // "pdf:<breedte mm>x<hoogte mm>|<bestandsnaam>"
            string rest = m.Substring(4);
            int i = rest.IndexOf('|');
            if (i < 0) return;
            string[] wh = rest.Substring(0, i).Split('x');
            double w, h;
            if (wh.Length != 2) return;
            if (!double.TryParse(wh[0], NumberStyles.Float, CultureInfo.InvariantCulture, out w)) return;
            if (!double.TryParse(wh[1], NumberStyles.Float, CultureInfo.InvariantCulture, out h)) return;
            if (w < 50 || w > 500 || h < 50 || h > 500) return;
            PrintSheet(sender as CoreWebView2, w / 25.4, h / 25.4, rest.Substring(i + 1));
        }
        else if (m == "meldingen")
        {
            try { Process.Start("ms-settings:notifications"); } catch { }
        }
    }

    // ---------- gesplitst scherm: vensters van James openen ernaast, met tabbladen ----------
    void BuildSplit(Tab t, WebView2 main)
    {
        SplitContainer sc = new SplitContainer();
        sc.Dock = DockStyle.Fill;
        sc.SplitterWidth = S(6);
        sc.BackColor = Line;
        sc.Panel1.BackColor = Color.White;
        sc.Panel2.BackColor = Color.White;
        sc.Panel1.Controls.Add(main);
        sc.Panel2Collapsed = true;

        Panel body = new Panel();
        body.Dock = DockStyle.Fill;
        sc.Panel2.Controls.Add(body);

        FlowLayoutPanel strip = new FlowLayoutPanel();
        strip.Dock = DockStyle.Top;
        strip.Height = S(40);
        strip.WrapContents = false;
        strip.BackColor = Chip;
        strip.Padding = new Padding(S(6), S(5), S(6), 0);
        sc.Panel2.Controls.Add(strip);

        t.Split = sc;
        t.Strip = strip;
        t.PaneBody = body;
        t.Host = sc;
    }

    async void OpenInPane(Tab t, CoreWebView2NewWindowRequestedEventArgs e)
    {
        CoreWebView2Deferral def = e.GetDeferral();
        PaneTab pt = null;
        try
        {
            pt = AddPaneTab(t);
            await pt.View.EnsureCoreWebView2Async(env);
            CoreWebView2 core = pt.View.CoreWebView2;
            PaneTab mine = pt;
            core.DownloadStarting += OnDownload;
            core.NewWindowRequested += delegate(object s2, CoreWebView2NewWindowRequestedEventArgs e2) { OpenInPane(t, e2); };
            core.WindowCloseRequested += delegate { BeginInvoke(new Action(delegate { ClosePaneTab(t, mine); })); };
            core.DocumentTitleChanged += delegate
            {
                string title = core.DocumentTitle;
                if (!string.IsNullOrEmpty(title)) mine.Btn.Text = title;
            };
            e.NewWindow = core;
            e.Handled = true;
        }
        catch
        {
            if (pt != null) ClosePaneTab(t, pt);
        }
        finally
        {
            def.Complete();
        }
    }

    PaneTab AddPaneTab(Tab t)
    {
        bool first = t.Pane.Count == 0;
        PaneTab pt = new PaneTab();

        WebView2 v = new WebView2();
        v.Dock = DockStyle.Fill;
        v.DefaultBackgroundColor = Color.White;
        t.PaneBody.Controls.Add(v);
        pt.View = v;

        Panel chip = new Panel();
        chip.Size = new Size(S(180), S(30));
        chip.Margin = new Padding(0, 0, S(4), 0);

        Button b = new Button();
        b.Text = "Laden...";
        b.Dock = DockStyle.Fill;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.TextAlign = ContentAlignment.MiddleLeft;
        b.AutoEllipsis = true;
        b.Font = new Font("Segoe UI", 9f);
        b.Cursor = Cursors.Hand;
        chip.Controls.Add(b);

        Button x = new Button();
        x.Text = "\u00D7";
        x.Dock = DockStyle.Right;
        x.Width = S(28);
        x.FlatStyle = FlatStyle.Flat;
        x.FlatAppearance.BorderSize = 0;
        x.Font = new Font("Segoe UI", 10f);
        x.Cursor = Cursors.Hand;
        chip.Controls.Add(x);

        pt.Chip = chip;
        pt.Btn = b;
        b.Click += delegate { SelectPaneTab(t, pt); };
        x.Click += delegate { ClosePaneTab(t, pt); };

        t.Strip.Controls.Add(chip);
        t.Pane.Add(pt);
        if (first)
        {
            t.Split.Panel2Collapsed = false;
            try { t.Split.SplitterDistance = t.Split.Width / 2; } catch { }
        }
        SelectPaneTab(t, pt);
        return pt;
    }

    void SelectPaneTab(Tab t, PaneTab sel)
    {
        foreach (PaneTab p in t.Pane)
        {
            bool on = p == sel;
            p.View.Visible = on;
            Color c = on ? Color.White : Chip;
            p.Chip.BackColor = c;
            foreach (Control ch in p.Chip.Controls) { ch.BackColor = c; ch.ForeColor = on ? Accent : Ink; }
        }
        sel.View.BringToFront();
    }

    void ClosePaneTab(Tab t, PaneTab pt)
    {
        if (!t.Pane.Contains(pt)) return;
        t.Pane.Remove(pt);
        t.Strip.Controls.Remove(pt.Chip);
        t.PaneBody.Controls.Remove(pt.View);
        try { pt.View.Dispose(); } catch { }
        pt.Chip.Dispose();
        if (t.Pane.Count == 0) t.Split.Panel2Collapsed = true;
        else SelectPaneTab(t, t.Pane[t.Pane.Count - 1]);
    }

    // ---------- aanmeldpaneel (als tegel op de startpagina) ----------
    const string PaneelScript = @"
(function(){
  if (location.hostname !== 'boddie91.github.io' || location.pathname.indexOf('hart-aanmeldpaneel') < 0) return;
  var st = document.createElement('style');
  st.textContent = '#tab-nieuw{display:none!important}';
  (document.head || document.documentElement).appendChild(st);
  setInterval(function(){
    var n = document.getElementById('tab-nieuw');
    if (n && n.getAttribute('aria-selected') === 'true') {
      var t = document.getElementById('tab-todo');
      if (t) t.click();
    }
  }, 400);
})();";

    // ---------- checklist (eigen bestand, niet op GitHub) ----------
    string ChecklistDoel { get { return Path.Combine(webDir, "eigen", "checklist.html"); } }

    string ChecklistBron()
    {
        foreach (string key in new string[] { "checklist", "dsmtool" })
        {
            string v = Cfg(key);
            if (v.Length > 2 && v[1] == ':' && File.Exists(v) &&
                (v.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || v.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)))
                return v;
        }
        return null;
    }

    void SyncChecklist()
    {
        try
        {
            string bron = ChecklistBron();
            if (bron == null) return;
            if (File.Exists(ChecklistDoel) && File.GetLastWriteTimeUtc(ChecklistDoel) >= File.GetLastWriteTimeUtc(bron)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ChecklistDoel));
            File.Copy(bron, ChecklistDoel, true);
        }
        catch { }
    }

    void KiesChecklist(CoreWebView2 core)
    {
        bool full = isFull;
        SetFull(false);
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = "Kies het bestand van de checklist (Differentiaaldiagnostiek)";
            d.Filter = "Webpagina (*.html;*.htm)|*.html;*.htm";
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ChecklistDoel));
                    File.Copy(d.FileName, ChecklistDoel, true);
                    if (Cfg("checklist").Length == 0)
                    {
                        File.AppendAllText(cfgPath, "\r\n# Bestand van de checklist (wordt bij het starten bijgewerkt als het nieuwer is)\r\nchecklist = " + d.FileName + "\r\n", Encoding.UTF8);
                        LoadConfig();
                    }
                    if (core != null) core.PostWebMessageAsString("checklist-ok");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Kopiëren lukte niet.\n\n" + ex.Message, "Hart Werkplek");
                }
            }
        }
        if (full) SetFull(true);
    }

    // ---------- sessieverslag: paneel dat naast elk tabblad open kan ----------
    void BuildVerslagPanel()
    {
        verslagSplit = new Splitter();
        verslagSplit.Dock = DockStyle.Right;
        verslagSplit.Width = S(6);
        verslagSplit.BackColor = Line;
        verslagSplit.Visible = false;
        Controls.Add(verslagSplit);

        verslag = new Panel();
        verslag.Dock = DockStyle.Right;
        verslag.Width = S(460);
        verslag.BackColor = Color.White;
        verslag.Visible = false;
        Controls.Add(verslag);
    }

    async void ShowVerslag(bool on)
    {
        if (env == null) return;
        verslag.Visible = on;
        verslagSplit.Visible = on;
        btnVerslag.BackColor = on ? AccentSoft : Color.White;
        btnVerslag.ForeColor = on ? Accent : Ink;
        if (!on || verslagView != null) return;
        WebView2 v = new WebView2();
        v.Dock = DockStyle.Fill;
        v.DefaultBackgroundColor = Color.White;
        verslag.Controls.Add(v);
        verslagView = v;
        try
        {
            await v.EnsureCoreWebView2Async(env);
            Setup(v.CoreWebView2, null);
            v.CoreWebView2.Navigate("https://werkplek.local/verslag.html");
        }
        catch { }
    }

    // ---------- klembord: downloads komen hier binnen, slepen naar James ----------
    void BuildClipPanel()
    {
        clip = new Panel();
        clip.Dock = DockStyle.Right;
        clip.Width = S(280);
        clip.BackColor = Color.White;
        clip.Visible = false;
        clip.AllowDrop = true;
        clip.Paint += delegate(object s, PaintEventArgs e)
        {
            using (Pen p = new Pen(Line)) e.Graphics.DrawLine(p, 0, 0, 0, clip.Height);
        };
        clip.DragEnter += ClipDragEnter;
        clip.DragDrop += ClipDragDrop;
        Controls.Add(clip);

        clipList = new FlowLayoutPanel();
        clipList.Dock = DockStyle.Fill;
        clipList.FlowDirection = FlowDirection.TopDown;
        clipList.WrapContents = false;
        clipList.AutoScroll = true;
        clipList.Padding = new Padding(S(12), S(4), S(12), S(12));
        clipList.AllowDrop = true;
        clipList.DragEnter += ClipDragEnter;
        clipList.DragDrop += ClipDragDrop;
        clip.Controls.Add(clipList);

        Label hint = new Label();
        clipHint = hint;
        hint.Dock = DockStyle.Top;
        hint.Height = S(64);
        hint.Padding = new Padding(S(14), 0, S(14), 0);
        hint.ForeColor = Muted;
        hint.Font = new Font("Segoe UI", 9f);
        hint.Text = ClipUitleg;
        clip.Controls.Add(hint);

        Panel head = new Panel();
        head.Dock = DockStyle.Top;
        head.Height = S(52);
        clip.Controls.Add(head);

        Label t = new Label();
        t.Text = "Klembord";
        t.Font = new Font("Segoe UI Semibold", 12f);
        t.ForeColor = Ink;
        t.Location = new Point(S(14), S(14));
        t.AutoSize = true;
        head.Controls.Add(t);

        Button open = SmallButton("Map");
        open.Location = new Point(S(130), S(12));
        open.Click += delegate { Process.Start("explorer.exe", "\"" + clipDir + "\""); };
        head.Controls.Add(open);

        Button empty = SmallButton("Leeg");
        empty.Location = new Point(S(200), S(12));
        empty.Click += delegate
        {
            if (MessageBox.Show(this, "Alle bestanden van het klembord wissen?", "Hart Werkplek", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                foreach (string f in Directory.GetFiles(clipDir)) { try { File.Delete(f); } catch { } }
                RefreshClips();
            }
        };
        head.Controls.Add(empty);
    }

    Button SmallButton(string text)
    {
        Button b = new Button();
        b.Text = text;
        b.Size = new Size(S(62), S(30));
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Line;
        b.BackColor = Color.White;
        b.ForeColor = Ink;
        b.Font = new Font("Segoe UI", 9f);
        b.Cursor = Cursors.Hand;
        return b;
    }

    // Slepen naar het klembord: gewone bestanden, "virtuele" bestanden uit een webpagina, of een link
    static string DropUrl(IDataObject d)
    {
        try
        {
            string u = null;
            if (d.GetDataPresent("UniformResourceLocatorW"))
            {
                MemoryStream ms = d.GetData("UniformResourceLocatorW") as MemoryStream;
                if (ms != null) u = Encoding.Unicode.GetString(ms.ToArray()).TrimEnd('\0');
            }
            if (string.IsNullOrEmpty(u) && d.GetDataPresent(DataFormats.UnicodeText))
                u = (d.GetData(DataFormats.UnicodeText) as string ?? "").Trim();
            if (!string.IsNullOrEmpty(u) && (u.StartsWith("https://") || u.StartsWith("http://")) && u.IndexOf('\n') < 0) return u;
        }
        catch { }
        return null;
    }

    // Houdt in het tabblad reMarkable bij hoe slepen en downloaden werken (zonder documentnamen),
    // zodat het slepen naar het klembord later precies goed gebouwd kan worden.
    const string RemarkableScript = @"(function(){
  if (!/(^|\.)remarkable\.com$/.test(location.hostname)) return;
  var log = []; window.__rmLog = log;
  function clean(u){ try { var x = new URL(u, location.href); var keys = []; x.searchParams.forEach(function(v,k){ keys.push(k); }); return x.origin + x.pathname + (keys.length ? '?' + keys.join('&') : ''); } catch(e) { return ''; } }
  function add(o){ o.tijd = new Date().toISOString().slice(11,19); log.push(o); if (log.length > 60) log.shift(); }
  var veilig = /^(download|downloaden|export|exporteren|pdf|share|delen|send|verzenden|save|opslaan|more|meer|menu|\.\.\.|…)/i;
  function desc(el){
    if (!el || !el.tagName) return null;
    var a = {};
    for (var i = 0; i < el.attributes.length; i++) {
      var at = el.attributes[i];
      if (!/^(class|role|draggable|href|type|id|data-)/.test(at.name) && at.name !== 'aria-label' && at.name !== 'title') continue;
      var v = at.value;
      if (at.name === 'href') v = clean(v);
      else if (at.name === 'aria-label' || at.name === 'title') v = veilig.test(v.trim()) ? v.trim().slice(0,30) : '[' + v.length + ' tekens]';
      else v = v.slice(0,120);
      a[at.name] = v;
    }
    return { tag: el.tagName, a: a };
  }
  try {
    var of = window.fetch;
    window.fetch = function(i, init){
      var url = typeof i === 'string' ? i : (i && i.url) || '';
      var m = (init && init.method) || (i && i.method) || 'GET';
      var p = of.apply(this, arguments);
      p.then(function(r){ add({ t: 'fetch', m: m, u: clean(url), s: r.status, ct: r.headers.get('content-type') }); }, function(){});
      return p;
    };
    var oo = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function(m, u){
      var x = this; x.__d = { t: 'xhr', m: m, u: clean(u) };
      x.addEventListener('load', function(){ var d = x.__d; d.s = x.status; try { d.ct = x.getResponseHeader('content-type'); } catch(e) {} add(d); });
      return oo.apply(this, arguments);
    };
  } catch(e) {}
  window.addEventListener('dragstart', function(e){
    var pad = [], el = e.target;
    for (var i = 0; el && i < 8; i++, el = el.parentElement) pad.push(desc(el));
    var knoppen = [];
    var c = e.target.closest ? (e.target.closest('[role=row],li,article,[draggable=true]') || e.target.parentElement) : null;
    if (c) c.querySelectorAll('button,[role=button],a,[role=menuitem]').forEach(function(b){ if (knoppen.length < 15) knoppen.push(desc(b)); });
    var types = []; try { types = Array.prototype.slice.call(e.dataTransfer.types); } catch(x) {}
    var links = []; try { var ul = e.dataTransfer.getData('text/uri-list'); if (ul) links = ul.split(/\r?\n/).map(clean); } catch(x) {}
    add({ t: 'dragstart', pad: pad, knoppen: knoppen, types: types, links: links });
  }, false);
  document.addEventListener('click', function(e){
    var b = e.target.closest ? e.target.closest('button,[role=button],[role=menuitem]') : null;
    if (!b) return;
    var tekst = (b.innerText || '').trim();
    add({ t: 'klik', el: desc(b), tekst: veilig.test(tekst) ? tekst.slice(0,30) : '[' + tekst.length + ' tekens]' });
  }, true);
})();
";

    async void SchrijfSleepInfo(IDataObject data, string url)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Hart Werkplek versie " + versie + " - sleepinfo reMarkable - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        sb.AppendLine("Er staan geen documentnamen of inhoud in dit bestand.");
        sb.AppendLine();
        try { sb.AppendLine("Formaten: " + string.Join(", ", data.GetFormats())); } catch { }
        if (url != null)
        {
            Uri u;
            if (Uri.TryCreate(url, UriKind.Absolute, out u)) sb.AppendLine("Link: " + u.Scheme + "://" + u.Host + u.AbsolutePath + (u.Query.Length > 0 ? " (met zoekdeel)" : ""));
        }
        sb.AppendLine();
        Tab r;
        if (tabs.TryGetValue("remarkable", out r) && r.View != null && r.View.CoreWebView2 != null)
        {
            try
            {
                string j = await r.View.CoreWebView2.ExecuteScriptAsync("JSON.stringify(window.__rmLog||[],null,1)");
                if (j != null && j.Length > 1 && j[0] == '"') { try { j = Regex.Unescape(j.Substring(1, j.Length - 2)); } catch { } }
                sb.AppendLine("Logboek tabblad reMarkable:");
                sb.AppendLine(j);
            }
            catch { }
        }
        try
        {
            string pad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "reMarkable-sleepinfo.txt");
            File.WriteAllText(pad, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    Label clipHint;
    System.Windows.Forms.Timer meldingTimer;
    const string ClipUitleg = "Downloads komen hier binnen. Sleep een bestand naar James om het te uploaden. Na 1 dag wordt het vanzelf gewist.";

    void ToonMelding(string tekst)
    {
        if (clipHint == null) return;
        clip.Visible = true;
        StyleClipButton();
        clipHint.Height = S(96);
        clipHint.ForeColor = Accent;
        clipHint.Text = tekst;
        if (meldingTimer == null)
        {
            meldingTimer = new System.Windows.Forms.Timer();
            meldingTimer.Interval = 12000;
            meldingTimer.Tick += delegate
            {
                meldingTimer.Stop();
                clipHint.Height = S(64);
                clipHint.ForeColor = Muted;
                clipHint.Text = ClipUitleg;
            };
        }
        meldingTimer.Stop();
        meldingTimer.Start();
    }

    static bool IsWebpagina(string pad)
    {
        string ext = Path.GetExtension(pad).ToLowerInvariant();
        return ext == ".htm" || ext == ".html" || ext == ".url" || ext == ".mht";
    }

    static bool LijktBestand(string url)
    {
        string u = url.ToLowerInvariant();
        int q = u.IndexOf('?'); string pad = q >= 0 ? u.Substring(0, q) : u;
        return pad.EndsWith(".pdf") || pad.EndsWith(".png") || pad.EndsWith(".jpg") || pad.EndsWith(".jpeg") || pad.EndsWith(".zip") || pad.EndsWith(".epub") || pad.EndsWith(".rmdoc") || u.Contains("download");
    }

    void ClipDragEnter(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("FileGroupDescriptorW") || DropUrl(e.Data) != null;
        e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
    }

    void ClipDragDrop(object sender, DragEventArgs e)
    {
        int n = 0;
        string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files != null)
        {
            foreach (string f in files)
            {
                if (!File.Exists(f)) continue;
                if (Path.GetDirectoryName(f).TrimEnd('\\') == clipDir.TrimEnd('\\')) continue;
                try { File.Copy(f, UniquePath(Path.GetFileName(f))); n++; } catch { }
            }
        }
        if (n == 0 && e.Data.GetDataPresent("FileGroupDescriptorW"))
        {
            List<string> paden = new List<string>();
            SaveVirtualFiles(e.Data, paden);
            // Een webpagina in plaats van het bestand zelf (bijv. een link uit reMarkable): niet bewaren
            bool alleenPagina = paden.Count > 0 && paden.TrueForAll(IsWebpagina);
            if (alleenPagina) foreach (string pp in paden) { try { File.Delete(pp); } catch { } }
            else n = paden.Count;
        }
        if (n == 0)
        {
            string url = DropUrl(e.Data);
            if (url != null && LijktBestand(url)) DownloadVanLink(url);
            else
            {
                SchrijfSleepInfo(e.Data, url);
                ToonMelding("Slepen uit reMarkable werkt nog niet: dit was een link, niet de pdf. Op je bureaublad staat nu reMarkable-sleepinfo.txt. Stuur dat naar Claude, dan maak ik het slepen af.");
            }
        }
        RefreshClips();
    }

    [DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM pmedium);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr hMem);

    int SaveVirtualFiles(IDataObject data, List<string> paden)
    {
        int saved = 0;
        try
        {
            MemoryStream ms = data.GetData("FileGroupDescriptorW") as MemoryStream;
            if (ms == null) return 0;
            byte[] b = ms.ToArray();
            int count = BitConverter.ToInt32(b, 0);
            System.Runtime.InteropServices.ComTypes.IDataObject com = data as System.Runtime.InteropServices.ComTypes.IDataObject;
            if (com == null) return 0;
            short cf = (short)DataFormats.GetFormat("FileContents").Id;
            for (int i = 0; i < count && i < 50; i++)
            {
                int off = 4 + i * 592;
                if (off + 592 > b.Length) break;
                string name = Encoding.Unicode.GetString(b, off + 72, 520);
                int z = name.IndexOf('\0'); if (z >= 0) name = name.Substring(0, z);
                name = Path.GetFileName(name);
                if (name.Length == 0) name = "bestand";
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');

                System.Runtime.InteropServices.ComTypes.FORMATETC fmt = new System.Runtime.InteropServices.ComTypes.FORMATETC();
                fmt.cfFormat = cf;
                fmt.dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT;
                fmt.lindex = i;
                fmt.ptd = IntPtr.Zero;
                fmt.tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_ISTREAM | System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL;
                System.Runtime.InteropServices.ComTypes.STGMEDIUM med;
                try { com.GetData(ref fmt, out med); } catch { continue; }
                try
                {
                    string path = UniquePath(name);
                    if (med.tymed == System.Runtime.InteropServices.ComTypes.TYMED.TYMED_ISTREAM)
                    {
                        System.Runtime.InteropServices.ComTypes.IStream st = (System.Runtime.InteropServices.ComTypes.IStream)Marshal.GetObjectForIUnknown(med.unionmember);
                        using (FileStream fs = File.Create(path))
                        {
                            byte[] buf = new byte[65536];
                            IntPtr pRead = Marshal.AllocHGlobal(4);
                            try
                            {
                                while (true)
                                {
                                    st.Read(buf, buf.Length, pRead);
                                    int r = Marshal.ReadInt32(pRead);
                                    if (r <= 0) break;
                                    fs.Write(buf, 0, r);
                                }
                            }
                            finally { Marshal.FreeHGlobal(pRead); }
                        }
                        saved++;
                        paden.Add(path);
                    }
                    else if (med.tymed == System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL)
                    {
                        IntPtr p = GlobalLock(med.unionmember);
                        try
                        {
                            int size = (int)GlobalSize(med.unionmember).ToUInt32();
                            byte[] buf = new byte[size];
                            Marshal.Copy(p, buf, 0, size);
                            File.WriteAllBytes(path, buf);
                            saved++;
                            paden.Add(path);
                        }
                        finally { GlobalUnlock(med.unionmember); }
                    }
                }
                catch { }
                finally { try { ReleaseStgMedium(ref med); } catch { } }
            }
        }
        catch { }
        return saved;
    }

    // Een gesleepte link downloaden in het tabblad waar hij vandaan komt (met je inlog), zodat hij op het klembord komt
    void DownloadVanLink(string url)
    {
        Uri u;
        if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return;
        Tab bron = null;
        foreach (Tab t in order)
        {
            if (t.View == null || t.View.CoreWebView2 == null) continue;
            Uri tu;
            if (Uri.TryCreate(t.View.CoreWebView2.Source, UriKind.Absolute, out tu) && tu.Host == u.Host) { bron = t; break; }
        }
        if (bron == null) { Tab r; if (tabs.TryGetValue("remarkable", out r) && r.View != null && r.View.CoreWebView2 != null) bron = r; }
        if (bron == null) return;
        string js = "(function(){var a=document.createElement('a');a.href=" + JsString(url) + ";a.download='';a.rel='noopener';document.body.appendChild(a);a.click();a.remove();})();";
        try { bron.View.CoreWebView2.ExecuteScriptAsync(js); } catch { }
    }

    static string JsString(string s)
    {
        StringBuilder sb = new StringBuilder("'");
        foreach (char c in s)
        {
            if (c == '\\' || c == '\'') sb.Append('\\').Append(c);
            else if (c < 32 || c == '<' || c == '>') sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append("'").ToString();
    }

    string UniquePath(string name)
    {
        string baseName = Path.GetFileNameWithoutExtension(name);
        string ext = Path.GetExtension(name);
        string p = Path.Combine(clipDir, name);
        int n = 2;
        while (File.Exists(p)) { p = Path.Combine(clipDir, baseName + " (" + n + ")" + ext); n++; }
        return p;
    }

    void OnDownload(object sender, CoreWebView2DownloadStartingEventArgs e)
    {
        string name = Path.GetFileName(e.ResultFilePath);
        if (string.IsNullOrEmpty(name)) name = "download";
        e.ResultFilePath = UniquePath(name);
        e.Handled = true; // geen downloadbalk; het klembord laat het zien
        CoreWebView2DownloadOperation op = e.DownloadOperation;
        op.StateChanged += delegate
        {
            BeginInvoke(new Action(delegate
            {
                RefreshClips();
                if (op.State == CoreWebView2DownloadState.Completed)
                {
                    clip.Visible = true;
                    StyleClipButton();
                }
            }));
        };
    }

    void CleanOldClips()
    {
        try
        {
            foreach (string f in Directory.GetFiles(clipDir))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-1)) { try { File.Delete(f); } catch { } }
        }
        catch { }
    }

    void RefreshClips()
    {
        if (clipList == null) return;
        clipList.SuspendLayout();
        clipList.Controls.Clear();
        string[] files = Directory.GetFiles(clipDir);
        Array.Sort(files, delegate(string a, string b) { return File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)); });
        foreach (string f in files)
        {
            if (f.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)) continue;
            clipList.Controls.Add(ClipItem(f));
        }
        if (clipList.Controls.Count == 0)
        {
            Label l = new Label();
            l.Text = "Nog leeg. Download iets in reMarkable of Outlook, of sleep hier een bestand heen.";
            l.ForeColor = Muted;
            l.Font = new Font("Segoe UI", 9.5f, FontStyle.Italic);
            l.Width = S(248);
            l.Height = S(60);
            clipList.Controls.Add(l);
        }
        clipList.ResumeLayout();
        StyleClipButton();
    }

    Control ClipItem(string path)
    {
        Panel p = new Panel();
        p.Width = S(250);
        p.Height = S(80);
        p.Margin = new Padding(0, 0, 0, S(8));
        p.BackColor = Chip;
        p.Cursor = Cursors.SizeAll;

        Label name = new Label();
        name.Text = Path.GetFileName(path);
        name.Font = new Font("Segoe UI Semibold", 9.5f);
        name.ForeColor = Ink;
        name.AutoEllipsis = true;
        name.Location = new Point(S(10), S(8));
        name.Size = new Size(S(230), S(20));
        p.Controls.Add(name);

        Label meta = new Label();
        meta.Text = "sleep naar James of klik op Open  ·  " + File.GetLastWriteTime(path).ToString("HH:mm");
        meta.Font = new Font("Segoe UI", 8.5f);
        meta.ForeColor = Muted;
        meta.Location = new Point(S(10), S(28));
        meta.Size = new Size(S(230), S(18));
        p.Controls.Add(meta);

        Button open = SmallButton("Open");
        open.Size = new Size(S(56), S(26));
        open.Location = new Point(S(10), S(48));
        open.Click += delegate { try { Process.Start(path); } catch (Exception ex) { MessageBox.Show(this, "Openen lukte niet.\n\n" + ex.Message, "Hart Werkplek"); } };
        p.Controls.Add(open);

        Button copy = SmallButton("Pad");
        copy.Size = new Size(S(50), S(26));
        copy.Location = new Point(S(70), S(48));
        copy.Click += delegate { Clipboard.SetText(path); };
        p.Controls.Add(copy);

        Button done = SmallButton("Klaar");
        done.Size = new Size(S(56), S(26));
        done.Location = new Point(S(124), S(48));
        done.Click += delegate { try { File.Delete(path); } catch { } RefreshClips(); };
        p.Controls.Add(done);

        MouseEventHandler drag = delegate(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            DataObject d = new DataObject(DataFormats.FileDrop, new string[] { path });
            p.DoDragDrop(d, DragDropEffects.Copy);
        };
        p.MouseDown += drag;
        name.MouseDown += drag;
        meta.MouseDown += drag;
        name.Cursor = Cursors.SizeAll;
        meta.Cursor = Cursors.SizeAll;
        return p;
    }

    void StyleClipButton()
    {
        if (btnClip == null) return;
        int n = 0;
        try
        {
            foreach (string f in Directory.GetFiles(clipDir))
                if (!f.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)) n++;
        }
        catch { }
        btnClip.Text = "   Klembord" + (n > 0 ? "  (" + n + ")" : "");
        btnClip.BackColor = clip.Visible ? AccentSoft : Color.White;
        btnClip.ForeColor = clip.Visible ? Accent : Ink;
    }

    // ---------- instellingen ----------
    // ---------- sessieblad: de pagina als pdf op het klembord zetten ----------
    async void PrintSheet(CoreWebView2 core, double widthInch, double heightInch, string name)
    {
        if (core == null) return;
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        name = name.Trim();
        if (name.Length == 0) name = "Sessieblad";
        if (name.Length > 80) name = name.Substring(0, 80);
        string path = UniquePath(name + ".pdf");
        bool ok = false;
        try
        {
            CoreWebView2PrintSettings ps = env.CreatePrintSettings();
            ps.PageWidth = widthInch;
            ps.PageHeight = heightInch;
            ps.MarginTop = 0;
            ps.MarginBottom = 0;
            ps.MarginLeft = 0;
            ps.MarginRight = 0;
            ps.ShouldPrintBackgrounds = true;
            ps.ShouldPrintHeaderAndFooter = false;
            ok = await core.PrintToPdfAsync(path, ps);
        }
        catch { }
        RefreshClips();
        if (ok)
        {
            clip.Visible = true;
            StyleClipButton();
        }
        try { core.PostWebMessageAsString(ok ? "pdf-ok" : "pdf-fout"); } catch { }
    }

    // ---------- bijwerken vanaf GitHub ----------
    async void ZoekUpdate()
    {
        int nieuw = 0;
        try
        {
            string t = await Task.Run(delegate
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("Cache-Control", "no-cache");
                    return wc.DownloadString(VersieUrl + "?t=" + DateTime.Now.Ticks);
                }
            });
            int.TryParse(t.Trim(), out nieuw);
        }
        catch { return; }
        if (nieuw > versie)
        {
            btnUpdate.ForeColor = Color.White;
            btnUpdate.BackColor = Accent;
            btnUpdate.FlatAppearance.MouseOverBackColor = Color.FromArgb(150, 45, 76);
            tips.SetToolTip(btnUpdate, "Bijwerken: er is een nieuwe versie");
        }
    }

    void Bijwerken()
    {
        string script = Path.Combine(appDir, "bijwerken.ps1");
        if (!File.Exists(script))
        {
            MessageBox.Show(this, "Het bijwerkbestand ontbreekt. Installeer Hart Werkplek een keer opnieuw met de hand.", "Hart Werkplek");
            return;
        }
        SetFull(false);
        if (MessageBox.Show(this,
                "Hart Werkplek haalt de nieuwste versie op van GitHub en start daarna opnieuw op.\n\n" +
                "Je instellingen, inlogs en to do's blijven bewaard.\n\nNu bijwerken?",
                "Hart Werkplek (versie " + versie + ")", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            SetFull(true);
            return;
        }
        try
        {
            Process.Start("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -WachtOp " + Process.GetCurrentProcess().Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Bijwerken kon niet starten.\n\n" + ex.Message, "Hart Werkplek");
            SetFull(true);
            return;
        }
        Close();
    }

    void OpenSettings()
    {
        if (!File.Exists(cfgPath)) File.WriteAllText(cfgPath, "", Encoding.UTF8);
        SetFull(false);
        try { Process.Start("notepad.exe", "\"" + cfgPath + "\""); }
        catch { }
        MessageBox.Show(this,
            "De instellingen staan nu open in Kladblok.\n\nPas ze aan, sla op (Ctrl+S) en klik daarna hier op OK.",
            "Hart Werkplek", MessageBoxButtons.OK, MessageBoxIcon.Information);
        ReloadSettings();
    }

    void ReloadSettings()
    {
        LoadConfig();
        SyncChecklist();
        foreach (Tab t in order)
        {
            if (t.Fixed) continue;
            string url = ResolveUrl(t.Key, t.Label);
            if (url == t.Url) continue;
            t.Url = url;
            if (t.View != null && t.View.CoreWebView2 != null) t.View.CoreWebView2.Navigate(url);
        }
        SetFull(true);
        if (current != null) ShowTab(current);
    }
}
