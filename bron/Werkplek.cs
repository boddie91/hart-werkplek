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
    public Control Host;            // wat getoond/verborgen wordt (de browser)
    public bool HasPane;            // vensters die deze pagina opent komen ernaast (James)
    public Panel PaneHost;          // het venster ernaast, met eigen tabbladen
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
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
    string versie = "0";
    Button btnUpdate;
    const string VersieUrl = "https://raw.githubusercontent.com/boddie91/hart-werkplek/main/bron/versie.txt";

    Dictionary<string, string> cfg = new Dictionary<string, string>();
    readonly List<Tab> order = new List<Tab>();
    readonly Dictionary<string, Tab> tabs = new Dictionary<string, Tab>();

    CoreWebView2Environment env;
    Panel side;
    Panel content;
    Panel viewport;                 // zichtbaar deel van de werkstrook
    Panel links;                    // deel links van de zijvensters, met de schuifbalk van de pagina
    Panel strook;                   // de pagina zelf, op volle breedte
    HScrollBar schuif;
    bool bezigMetIndeling;
    Panel clip;
    FlowLayoutPanel clipList;
    Button btnFull;
    Button btnClip;
    Button btnVerslag;
    Panel verslag;
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
        try { versie = File.ReadAllText(Path.Combine(appDir, "versie.txt")).Trim(); } catch { }

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
        Shown += delegate { Indeling(); };
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

        // zijvensters versmallen de pagina niet: ze komen ernaast en je schuift horizontaal
        // zijvensters vallen rechts over de pagina heen; de pagina houdt haar breedte en krijgt een schuifbalk
        viewport = new Panel();
        viewport.Dock = DockStyle.Fill;
        viewport.BackColor = Line;
        content.Controls.Add(viewport);
        links = new Panel();
        links.BackColor = Bg;
        links.Location = new Point(0, 0);
        viewport.Controls.Add(links);
        schuif = new HScrollBar();
        schuif.Dock = DockStyle.Bottom;
        schuif.Height = SystemInformation.HorizontalScrollBarHeight;
        schuif.Visible = false;
        schuif.ValueChanged += delegate { if (strook != null) strook.Left = -schuif.Value; };
        links.Controls.Add(schuif);
        strook = new Panel();
        strook.BackColor = Bg;
        strook.Location = new Point(0, 0);
        links.Controls.Add(strook);
        viewport.Resize += delegate { Indeling(); };

        tips = new ToolTip();
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

        btnClip = SideButton("Klembord");
        btnClip.Click += delegate { clip.Visible = !clip.Visible; StyleClipButton(); };
        btnVerslag = SideButton("Sessieverslag");
        btnVerslag.Click += delegate { ShowVerslag(!verslag.Visible); };

        // menu in groepen; "#" = kopje. Volgorde van boven naar beneden.
        string[] menu = {
            "start",
            "#Dagelijks", "outlook", "james", "caseload",
            "#Sessie", "sessieblad", "@verslag", "interventies",
            "#Diagnostiek en verslag", "dsm", "rapportages", "sjablonen",
            "#Documenten", "remarkable", "@klembord" };
        List<Control> items = new List<Control>();
        foreach (string m in menu)
        {
            if (m.StartsWith("#")) { items.Add(MenuKop(m.Substring(1))); continue; }
            if (m == "@verslag") { items.Add(btnVerslag); continue; }
            if (m == "@klembord") { items.Add(btnClip); continue; }
            Tab t;
            if (!tabs.TryGetValue(m, out t)) continue;
            Button b = SideButton(t.Label);
            Tab captured = t;
            b.Click += delegate { ShowTab(captured); };
            t.Btn = b;
            items.Add(b);
        }
        foreach (Tab t in order)
        {
            if (t.Btn != null) continue;           // tabblad dat (nog) niet in het menu staat
            Button b = SideButton(t.Label);
            Tab captured = t;
            b.Click += delegate { ShowTab(captured); };
            t.Btn = b;
            items.Add(b);
        }
        // omgekeerd toevoegen bij Dock=Top
        for (int i = items.Count - 1; i >= 0; i--)
        {
            items[i].Dock = DockStyle.Top;
            side.Controls.Add(items[i]);
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

    Label MenuKop(string text)
    {
        Label l = new Label();
        l.Text = text.ToUpper(new CultureInfo("nl-NL"));
        l.Height = S(30);
        l.Padding = new Padding(S(10), S(10), 0, 0);
        l.Font = new Font("Segoe UI Semibold", 8f);
        l.ForeColor = Muted;
        l.TextAlign = ContentAlignment.BottomLeft;
        return l;
    }

    Button SideButton(string text)
    {
        Button b = new Button();
        b.Text = "   " + text;
        b.Height = S(38);
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
        v.DefaultBackgroundColor = Color.White;
        t.View = v;
        t.Host = v;
        if (t.Key == "james") BuildPane(t);
        WebView2 pv = null;
        if (t.Key == "start" && !Cfg("aanmeldpaneel").Equals("uit", StringComparison.OrdinalIgnoreCase))
        {
            // het aanmeldpaneel als eigen browser rechts op de startpagina (werkt dan net als in Edge)
            Panel host = new Panel();
            host.BackColor = Bg;
            v.Dock = DockStyle.Fill;
            host.Controls.Add(v);
            Splitter sp = new Splitter();
            sp.Dock = DockStyle.Right;
            sp.Width = S(6);
            sp.BackColor = Line;
            sp.MinSize = S(320);
            host.Controls.Add(sp);
            pv = new WebView2();
            pv.Dock = DockStyle.Right;
            pv.Width = LeesPaneelBreedte();
            pv.DefaultBackgroundColor = Color.White;
            host.Controls.Add(pv);
            WebView2 pvc = pv;
            sp.SplitterMoved += delegate { try { File.WriteAllText(Path.Combine(dataDir, "paneelbreedte.txt"), ((int)(pvc.Width / k)).ToString()); } catch { } };
            t.Host = host;
        }
        t.Host.Visible = false;
        strook.Controls.Add(t.Host);
        await v.EnsureCoreWebView2Async(env);
        Setup(v.CoreWebView2, t);
        if (t.Key == "outlook")
            await v.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(OutlookScript);
        if (t.Key == "remarkable")
            await v.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(RemarkableScript);
        v.CoreWebView2.Navigate(t.Url);
        if (pv != null)
        {
            try
            {
                await pv.EnsureCoreWebView2Async(env);
                Setup(pv.CoreWebView2, null);
                await pv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(PaneelScript);
                pv.CoreWebView2.Navigate(ResolveUrl("aanmeldpaneel", "Aanmeldpaneel"));
            }
            catch { }
        }
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
        Indeling();
        if (schuif.Visible) schuif.Value = 0;
        t.View.Focus();
    }

    void Setup(CoreWebView2 core, Tab owner)
    {
        core.SetVirtualHostNameToFolderMapping("werkplek.local", webDir, CoreWebView2HostResourceAccessKind.Allow);
        if (owner != null && owner.HasPane)
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

    // ---------- zijvenster: vensters van James openen naast de pagina, met tabbladen ----------
    void BuildPane(Tab t)
    {
        Panel host = new Panel();
        host.BackColor = Color.White;
        host.Visible = false;

        Panel body = new Panel();
        body.Dock = DockStyle.Fill;
        host.Controls.Add(body);

        FlowLayoutPanel strip = new FlowLayoutPanel();
        strip.Dock = DockStyle.Top;
        strip.Height = S(40);
        strip.WrapContents = false;
        strip.BackColor = Chip;
        strip.Padding = new Padding(S(6), S(5), S(6), 0);
        host.Controls.Add(strip);

        viewport.Controls.Add(host);
        host.BringToFront();
        t.HasPane = true;
        t.PaneHost = host;
        t.Strip = strip;
        t.PaneBody = body;
    }

    // versies als 19, 19.4 of 19.4.1 vergelijken (per deel als getal)
    static int VergelijkVersie(string a, string b)
    {
        string[] x = (a ?? "0").Split('.');
        string[] y = (b ?? "0").Split('.');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int p = 0, q = 0;
            if (i < x.Length) int.TryParse(x[i].Trim(), out p);
            if (i < y.Length) int.TryParse(y[i].Trim(), out q);
            if (p != q) return p < q ? -1 : 1;
        }
        return 0;
    }

    // ---------- indelen: pagina op volle breedte links, zijvensters rechts eroverheen ----------
    void Indeling()
    {
        if (bezigMetIndeling || strook == null) return;
        bezigMetIndeling = true;
        try
        {
            int vw = Math.Max(S(300), viewport.ClientSize.Width);
            int vh = viewport.ClientSize.Height;
            List<Control> rechts = new List<Control>();
            List<int> breed = new List<int>();
            foreach (Tab x in order)
            {
                if (x.PaneHost == null) continue;
                bool zien = x == current && x.Pane.Count > 0;
                x.PaneHost.Visible = zien;
                if (zien) { rechts.Add(x.PaneHost); breed.Add(Math.Max(S(520), vw * 55 / 100)); }
            }
            if (verslag != null && verslag.Visible) { rechts.Add(verslag); breed.Add(S(460)); }
            if (clip != null && clip.Visible) { rechts.Add(clip); breed.Add(S(280)); }

            int rb = 0;
            foreach (int w in breed) rb += w + 1;
            int beschikbaar = Math.Max(S(300), vw - rb);
            links.Bounds = new Rectangle(0, 0, beschikbaar, vh);

            int xpos = beschikbaar;
            for (int i = 0; i < rechts.Count; i++)
            {
                xpos += 1;
                int w = Math.Max(0, Math.Min(breed[i], vw - xpos));
                rechts[i].Dock = DockStyle.None;
                rechts[i].Bounds = new Rectangle(xpos, 0, w, vh);
                rechts[i].BringToFront();
                xpos += w;
            }

            bool nodig = vw > beschikbaar;
            if (schuif.Visible != nodig) schuif.Visible = nodig;
            int h = links.ClientSize.Height - (nodig ? schuif.Height : 0);
            if (current != null && current.Host != null)
            {
                current.Host.Dock = DockStyle.None;
                current.Host.Bounds = new Rectangle(0, 0, vw, h);
            }
            strook.Size = new Size(vw, h);

            if (nodig)
            {
                schuif.Minimum = 0;
                schuif.LargeChange = beschikbaar;
                schuif.SmallChange = S(80);
                schuif.Maximum = vw - 1;
                int max = vw - beschikbaar;
                if (schuif.Value > max) schuif.Value = max;
            }
            else if (schuif.Value != 0) schuif.Value = 0;
            strook.Left = -schuif.Value;
        }
        finally { bezigMetIndeling = false; }
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
        SelectPaneTab(t, pt);
        if (first) Indeling();
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
        if (t.Pane.Count == 0) Indeling();
        else SelectPaneTab(t, t.Pane[t.Pane.Count - 1]);
    }

    int LeesPaneelBreedte()
    {
        int b = 440;
        try { int.TryParse(File.ReadAllText(Path.Combine(dataDir, "paneelbreedte.txt")).Trim(), out b); } catch { }
        if (b < 320) b = 440;
        return S(Math.Min(b, 900));
    }

    // ---------- aanmeldpaneel (rechts op de startpagina) ----------
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
        verslag = new Panel();
        verslag.Width = S(460);
        verslag.BackColor = Color.White;
        verslag.Visible = false;
        verslag.VisibleChanged += delegate { Indeling(); };
        viewport.Controls.Add(verslag);
    }

    async void ShowVerslag(bool on)
    {
        if (env == null) return;
        verslag.Visible = on;
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
        clip.VisibleChanged += delegate { Indeling(); };
        viewport.Controls.Add(clip);

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

        // invulbalk voor tekst (alleen in het geheugen, niet op schijf)
        Panel invul = new Panel();
        invul.Dock = DockStyle.Top;
        invul.Height = S(40);
        invul.Padding = new Padding(S(12), S(2), S(12), S(6));
        TextBox tb = new TextBox();
        tb.Dock = DockStyle.Fill;
        tb.Font = new Font("Segoe UI", 10f);
        tb.BorderStyle = BorderStyle.FixedSingle;
        tb.HandleCreated += delegate { SendMessage(tb.Handle, 0x1501, (IntPtr)1, "Typ of plak tekst en druk Enter"); };
        Button plus = KlembordIcoon("\uE710", "Tekst toevoegen aan het klembord");
        plus.Dock = DockStyle.Right;
        plus.Width = S(34);
        Action voeg = delegate
        {
            string tx = tb.Text.Trim();
            if (tx.Length == 0) { try { if (Clipboard.ContainsText()) tx = Clipboard.GetText().Trim(); } catch { } }
            if (tx.Length == 0) return;
            VoegTekstToe(tx);
            tb.Clear();
        };
        plus.Click += delegate { voeg(); };
        tb.KeyDown += delegate(object s2, KeyEventArgs e2) { if (e2.KeyCode == Keys.Enter) { e2.SuppressKeyPress = true; voeg(); } };
        invul.Controls.Add(tb);
        invul.Controls.Add(plus);

        Label hint = new Label();
        clipHint = hint;
        hint.Dock = DockStyle.Top;
        hint.Height = S(76);
        hint.Padding = new Padding(S(14), 0, S(14), 0);
        hint.ForeColor = Muted;
        hint.Font = new Font("Segoe UI", 9f);
        hint.Text = ClipUitleg;
        clip.Controls.Add(hint);
        clip.Controls.Add(invul);

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
        open.Width = S(54);
        open.Location = new Point(S(110), S(12));
        open.Click += delegate { Process.Start("explorer.exe", "\"" + clipDir + "\""); };
        head.Controls.Add(open);

        Button empty = SmallButton("Leeg");
        empty.Width = S(54);
        empty.Location = new Point(S(168), S(12));
        empty.Click += delegate
        {
            if (MessageBox.Show(this, "Alle bestanden van het klembord wissen?", "Hart Werkplek", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                foreach (string f in Directory.GetFiles(clipDir)) { try { File.Delete(f); } catch { } }
                RefreshClips();
            }
        };
        head.Controls.Add(empty);

        Button dicht = SmallButton("\u00D7");
        dicht.Size = new Size(S(30), S(30));
        dicht.Location = new Point(S(236), S(12));
        dicht.Font = new Font("Segoe UI", 11f);
        dicht.FlatAppearance.BorderSize = 0;
        tips.SetToolTip(dicht, "Klembord sluiten");
        dicht.Click += delegate { clip.Visible = false; StyleClipButton(); };
        head.Controls.Add(dicht);
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
    if (links.length) window.__rmSleep = { link: links[0], tijd: Date.now() };
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
    const string ClipUitleg = "Tekst: plak bovenin, klik erop om weer te kopiëren. Bestanden: sleep naar James. Bestanden gaan na 1 dag weg, tekst bij het afsluiten.";

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
                clipHint.Height = S(76);
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
        bool ok = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("FileGroupDescriptorW") || DropUrl(e.Data) != null || e.Data.GetDataPresent(DataFormats.UnicodeText);
        e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
    }

    void ClipDragDrop(object sender, DragEventArgs e)
    {
        // Eerst alles uit de sleep halen zolang die er nog is; daarna rustig verwerken
        List<string> nieuw = new List<string>();
        string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files != null)
        {
            foreach (string f in files)
            {
                if (!File.Exists(f)) continue;
                if (Path.GetDirectoryName(f).TrimEnd('\\') == clipDir.TrimEnd('\\')) continue;
                try { string doel = UniquePath(Path.GetFileName(f)); File.Copy(f, doel); nieuw.Add(doel); } catch { }
            }
        }
        if (nieuw.Count == 0 && e.Data.GetDataPresent("FileGroupDescriptorW")) SaveVirtualFiles(e.Data, nieuw);
        string url = DropUrl(e.Data);
        if (nieuw.Count == 0 && url == null && files == null && e.Data.GetDataPresent(DataFormats.UnicodeText))
        {
            // losse tekst (bijv. geselecteerd in James of Outlook): als tekstknipsel bewaren
            string tx = e.Data.GetData(DataFormats.UnicodeText) as string;
            if (!string.IsNullOrEmpty(tx) && tx.Trim().Length > 0) VoegTekstToe(tx.Trim());
            return;
        }
        VerwerkDrop(nieuw, url);
    }

    static readonly Regex RmDocument = new Regex(@"^https://(app|my)\.remarkable\.com/document/([0-9a-fA-F-]{36})", RegexOptions.Compiled);

    async Task<string> LaatsteRemarkableSleep()
    {
        Tab r;
        if (!tabs.TryGetValue("remarkable", out r) || r.View == null || r.View.CoreWebView2 == null) return null;
        try
        {
            string j = await r.View.CoreWebView2.ExecuteScriptAsync(
                "(function(){var s=window.__rmSleep;window.__rmSleep=null;if(!s||Date.now()-s.tijd>60000)return '';return s.link;})()");
            if (j != null && j.Length > 1 && j[0] == '"') j = Regex.Unescape(j.Substring(1, j.Length - 2));
            return string.IsNullOrEmpty(j) ? null : j;
        }
        catch { return null; }
    }

    async void VerwerkDrop(List<string> nieuw, string url)
    {
        string link = await LaatsteRemarkableSleep();
        if ((link == null || !RmDocument.IsMatch(link)) && url != null && RmDocument.IsMatch(url)) link = url;
        if (link == null || !RmDocument.IsMatch(link))
        {
            foreach (string p in nieuw)
            {
                if (!IsWebpagina(p)) continue;
                try
                {
                    Match m = Regex.Match(File.ReadAllText(p), @"https://(app|my)\.remarkable\.com/document/[0-9a-fA-F-]{36}");
                    if (m.Success) { link = m.Value; break; }
                }
                catch { }
            }
        }

        if (link != null && RmDocument.IsMatch(link))
        {
            // Een document uit reMarkable: wat Windows meegaf (link of plaatje) weggooien en een pdf maken
            foreach (string p in nieuw) { try { File.Delete(p); } catch { } }
            RefreshClips();
            MaakRemarkablePdf(RmDocument.Match(link).Value);
            return;
        }

        bool alleenPagina = nieuw.Count > 0 && nieuw.TrueForAll(IsWebpagina);
        if (alleenPagina) { foreach (string p in nieuw) { try { File.Delete(p); } catch { } } nieuw.Clear(); }
        if (nieuw.Count == 0)
        {
            if (url != null && LijktBestand(url)) DownloadVanLink(url);
            else if (alleenPagina) ToonMelding("Dit was een link naar een pagina, niet een bestand.");
        }
        RefreshClips();
    }

    // ---------- reMarkable-document als pdf: de website tekent de pagina's, het programma bundelt ze ----------
    const string RemarkablePdfScript = @"(async function(){
  var wacht = function(ms){ return new Promise(function(r){ setTimeout(r, ms); }); };
  var verder = null;
  chrome.webview.addEventListener('message', function(e){ if (e.data === 'rmverder' && verder) { var f = verder; verder = null; f(); } });
  var stuur = function(m){ return new Promise(function(r){ verder = r; chrome.webview.postMessage(m); }); };
  function groot(e){ var r = e.getBoundingClientRect(); return r.width >= 250 && r.height >= 250; }
  function visuals(){
    return Array.prototype.filter.call(document.querySelectorAll('canvas,img,svg'), function(e){
      if (e.tagName.toLowerCase() === 'svg' && e.parentElement && e.parentElement.closest('svg')) return false;
      return groot(e);
    });
  }
  // de scrollbare container waar de pagina's zelf in staan (niet bijv. een zijbalk met miniaturen)
  function scroller(){
    var v = visuals();
    for (var i = 0; i < v.length; i++) {
      for (var el = v[i].parentElement; el && el !== document.body; el = el.parentElement) {
        var cs = getComputedStyle(el);
        if (/(auto|scroll)/.test(cs.overflowY) && el.scrollHeight > el.clientHeight + 10) return el;
      }
    }
    return document.scrollingElement;
  }
  function diag(){
    var tel = {};
    ['canvas','img','svg','iframe','video','object','embed'].forEach(function(t){ tel[t] = document.getElementsByTagName(t).length; });
    var grote = [];
    document.querySelectorAll('body *').forEach(function(e){
      var r = e.getBoundingClientRect();
      if (r.width > 250 && r.height > 250) {
        var c = e.className; c = (c && c.baseVal !== undefined) ? c.baseVal : (c || '');
        grote.push({ tag: e.tagName, cls: String(c).slice(0, 90), w: Math.round(r.width), h: Math.round(r.height) });
      }
    });
    return { pad: location.pathname.replace(/[0-9a-f-]{36}/g, '<id>'), tel: tel, grote: grote.slice(-25) };
  }
  var n = 0;
  while (!visuals().length && n < 60) { await wacht(500); n++; }
  if (!visuals().length) { chrome.webview.postMessage('rmfout:' + JSON.stringify(diag())); return; }
  await wacht(2500);
  var sc = scroller();
  var scTop = function(){ return sc === document.scrollingElement ? window.scrollY : sc.scrollTop; };
  // reMarkable opent vaak bij de laatste pagina: eerst helemaal naar boven
  for (var t = 0; t < 8 && scTop() > 2; t++) {
    if (sc === document.scrollingElement) window.scrollTo(0, 0); else sc.scrollTop = 0;
    await wacht(800);
  }
  await wacht(1500);
  var gedaan = {}, urls = {}, idx = 0;
  async function pak(){
    var lijst = visuals().map(function(v){ return { v: v, top: v.getBoundingClientRect().top + scTop() }; }).sort(function(a, b){ return a.top - b.top; });
    for (var i = 0; i < lijst.length; i++) {
      var v = lijst[i].v, key = Math.round(lijst[i].top / 20);
      if (gedaan[key]) continue;
      v.scrollIntoView({ block: 'center' });
      await wacht(1000);
      var url = null, tag = v.tagName.toLowerCase();
      try {
        if (tag === 'canvas') url = v.toDataURL('image/png');
        else if (tag === 'img') {
          var b = await (await fetch(v.currentSrc || v.src)).blob();
          url = await new Promise(function(res){ var fr = new FileReader(); fr.onload = function(){ res(fr.result); }; fr.readAsDataURL(b); });
        } else {
          var xml = new XMLSerializer().serializeToString(v), rr = v.getBoundingClientRect(), c = document.createElement('canvas');
          c.width = Math.round(rr.width * 2); c.height = Math.round(rr.height * 2);
          var im = new Image();
          await new Promise(function(res, rej){ im.onload = res; im.onerror = rej; im.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(xml); });
          var g = c.getContext('2d'); g.fillStyle = '#fff'; g.fillRect(0, 0, c.width, c.height); g.drawImage(im, 0, 0, c.width, c.height);
          url = c.toDataURL('image/png');
        }
      } catch (e) { url = null; }
      gedaan[key] = true;
      if (url && urls[url]) continue;
      if (url && url.indexOf('data:image') === 0) { urls[url] = true; await stuur('rmpagina:' + (idx++) + '|' + url); }
      else {
        var r = v.getBoundingClientRect(), d = window.devicePixelRatio || 1;
        await stuur('rmfoto:' + (idx++) + '|' + [r.left * d, r.top * d, r.width * d, r.height * d].map(Math.round).join(','));
      }
    }
  }
  var stil = 0;
  for (var s = 0; s < 400; s++) {
    await pak();
    var voor = scTop();
    if (sc === document.scrollingElement) window.scrollBy(0, window.innerHeight * 0.6); else sc.scrollTop += sc.clientHeight * 0.6;
    await wacht(1500);
    if (Math.abs(scTop() - voor) < 2) { stil++; if (stil >= 2) break; } else stil = 0;
  }
  await pak();
  var info = { scroller: sc === document.scrollingElement ? 'pagina' : (sc.tagName + ' ' + String(sc.className || '').slice(0, 80)),
               hoogte: sc.scrollHeight, zicht: sc.clientHeight, breed: sc.scrollWidth, zichtBreed: sc.clientWidth, paginas: idx, diag: diag() };
  chrome.webview.postMessage('rminfo:' + JSON.stringify(info));
  chrome.webview.postMessage('rmklaar:' + idx + '|' + (document.title || ''));
})();
";

    void MaakRemarkablePdf(string docUrl)
    {
        Form f = new Form();
        f.Text = "PDF maken van reMarkable-document…";
        f.Icon = Icon;
        f.ShowInTaskbar = false;
        Rectangle wa = Screen.FromControl(this).WorkingArea;
        int h = (int)(wa.Height * 0.9), w = Math.Min(wa.Width - S(40), (int)(h * 0.8));
        f.StartPosition = FormStartPosition.Manual;
        f.Bounds = new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2, w, h);
        WebView2 v = new WebView2();
        v.Dock = DockStyle.Fill;
        v.DefaultBackgroundColor = Color.White;
        f.Controls.Add(v);
        f.Show(this);
        f.TopMost = true;

        SortedDictionary<int, Bitmap> paginas = new SortedDictionary<int, Bitmap>();
        bool klaar = false, gestart = false;
        System.Windows.Forms.Timer timeout = new System.Windows.Forms.Timer();
        timeout.Interval = 240000;

        Action<string> stop = delegate(string melding)
        {
            if (klaar) return;
            klaar = true;
            timeout.Stop();
            foreach (Bitmap b in paginas.Values) { try { b.Dispose(); } catch { } }
            try { f.Close(); } catch { }
            if (melding != null) ToonMelding(melding);
        };
        timeout.Tick += delegate { stop("De pdf maken duurde te lang en is gestopt. Probeer het nog een keer."); };
        f.FormClosed += delegate { if (!klaar) { klaar = true; timeout.Stop(); } };
        timeout.Start();

        StartRemarkablePdf(v, f, docUrl, paginas, timeout, stop, () => gestart, b => gestart = b);
    }

    async void StartRemarkablePdf(WebView2 v, Form f, string docUrl, SortedDictionary<int, Bitmap> paginas,
        System.Windows.Forms.Timer timeout, Action<string> stop, Func<bool> isGestart, Action<bool> zetGestart)
    {
        try
        {
            await v.EnsureCoreWebView2Async(env);
        }
        catch { stop("De pdf maken kon niet starten."); return; }
        CoreWebView2 core = v.CoreWebView2;
        core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };

        core.NavigationCompleted += async delegate(object s, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (isGestart() || !e.IsSuccess) return;
            Uri u;
            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out u) || u.AbsolutePath.IndexOf("/document/", StringComparison.OrdinalIgnoreCase) < 0) return;
            zetGestart(true);
            try { await core.ExecuteScriptAsync(RemarkablePdfScript); } catch { stop("De pdf maken kon niet starten."); }
        };

        core.WebMessageReceived += async delegate(object s, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string m;
            try { m = e.TryGetWebMessageAsString(); } catch { return; }
            if (m == null) return;
            try
            {
                if (m.StartsWith("rmpagina:"))
                {
                    int bar = m.IndexOf('|');
                    int nr = int.Parse(m.Substring(9, bar - 9));
                    string data = m.Substring(bar + 1);
                    int komma = data.IndexOf(',');
                    byte[] bytes = Convert.FromBase64String(data.Substring(komma + 1));
                    using (MemoryStream ms = new MemoryStream(bytes))
                    using (Image img = Image.FromStream(ms))
                        paginas[nr] = OpWit(img);
                    f.Text = "PDF maken… pagina " + paginas.Count;
                    core.PostWebMessageAsString("rmverder");
                }
                else if (m.StartsWith("rmfoto:"))
                {
                    int bar = m.IndexOf('|');
                    int nr = int.Parse(m.Substring(7, bar - 7));
                    string[] d = m.Substring(bar + 1).Split(',');
                    Rectangle rect = new Rectangle(int.Parse(d[0]), int.Parse(d[1]), int.Parse(d[2]), int.Parse(d[3]));
                    using (MemoryStream ms = new MemoryStream())
                    {
                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                        ms.Position = 0;
                        using (Bitmap scherm = new Bitmap(ms))
                        {
                            rect.Intersect(new Rectangle(0, 0, scherm.Width, scherm.Height));
                            if (rect.Width > 50 && rect.Height > 50)
                            {
                                using (Bitmap uit = scherm.Clone(rect, scherm.PixelFormat)) paginas[nr] = OpWit(uit);
                            }
                        }
                    }
                    f.Text = "PDF maken… pagina " + paginas.Count;
                    core.PostWebMessageAsString("rmverder");
                }
                else if (m.StartsWith("rmklaar:"))
                {
                    int bar = m.IndexOf('|');
                    string titel = bar >= 0 ? m.Substring(bar + 1) : "";
                    if (paginas.Count == 0) { stop("Er zijn geen pagina's gevonden in dit document."); return; }
                    List<Bitmap> lijst = new List<Bitmap>(paginas.Values);
                    paginas.Clear();
                    stop(null);
                    BeginInvoke(new Action(delegate { KiesPaginas(lijst, PdfNaam(titel)); }));
                }
                else if (m.StartsWith("rminfo:"))
                {
                    SchrijfSleepInfoTekst("Laatste pdf uit reMarkable: " + m.Substring(7));
                }
                else if (m.StartsWith("rmfout:"))
                {
                    SchrijfSleepInfoTekst("Viewer reMarkable: " + m.Substring(7));
                    stop("Het lukte niet om de pagina's van dit document te vinden. Op je bureaublad staat reMarkable-sleepinfo.txt. Stuur dat naar Claude.");
                }
            }
            catch (Exception ex)
            {
                stop("De pdf maken ging mis: " + ex.Message);
            }
        };

        core.Navigate(docUrl);
    }

    // Na het ophalen: kies welke pagina's in de pdf komen
    void KiesPaginas(List<Bitmap> lijst, string naam)
    {
        try
        {
            using (Form d = new Form())
            {
                d.Text = "Welke pagina's wil je in de pdf?";
                d.Icon = Icon;
                d.StartPosition = FormStartPosition.CenterScreen;
                d.Size = new Size(S(980), S(720));
                d.MinimumSize = new Size(S(600), S(420));
                d.BackColor = Bg;
                d.Font = new Font("Segoe UI", 10f);
                d.TopMost = true;

                Panel boven = new Panel();
                boven.Dock = DockStyle.Top;
                boven.Height = S(56);
                boven.Padding = new Padding(S(14), S(12), S(14), S(8));
                Label ln = new Label();
                ln.Text = "Bestandsnaam";
                ln.AutoSize = true;
                ln.Location = new Point(S(14), S(17));
                boven.Controls.Add(ln);
                TextBox tb = new TextBox();
                tb.Text = naam;
                tb.Location = new Point(S(120), S(13));
                tb.Width = S(380);
                boven.Controls.Add(tb);

                FlowLayoutPanel knoppenRij = new FlowLayoutPanel();
                knoppenRij.Dock = DockStyle.Bottom;
                knoppenRij.Height = S(58);
                knoppenRij.FlowDirection = FlowDirection.RightToLeft;
                knoppenRij.Padding = new Padding(S(12), S(10), S(12), S(10));

                FlowLayoutPanel raster = new FlowLayoutPanel();
                raster.Dock = DockStyle.Fill;
                raster.AutoScroll = true;
                raster.Padding = new Padding(S(10));
                raster.BackColor = Bg;

                List<CheckBox> vinkjes = new List<CheckBox>();
                Label telling = new Label();
                telling.AutoSize = true;
                telling.Margin = new Padding(S(10), S(10), S(10), 0);
                Action tel = delegate
                {
                    int n = 0; foreach (CheckBox c in vinkjes) if (c.Checked) n++;
                    telling.Text = n + " van " + vinkjes.Count + " pagina's gekozen";
                };

                for (int i = 0; i < lijst.Count; i++)
                {
                    Bitmap bmp = lijst[i];
                    Panel kaart = new Panel();
                    kaart.Size = new Size(S(150), S(230));
                    kaart.Margin = new Padding(S(6));
                    kaart.BackColor = Color.White;
                    kaart.Padding = new Padding(S(6));
                    PictureBox pb = new PictureBox();
                    pb.Dock = DockStyle.Fill;
                    pb.SizeMode = PictureBoxSizeMode.Zoom;
                    pb.Image = bmp;
                    pb.Cursor = Cursors.Hand;
                    CheckBox cb = new CheckBox();
                    cb.Dock = DockStyle.Bottom;
                    cb.Height = S(28);
                    cb.Text = "Pagina " + (i + 1);
                    cb.Checked = true;
                    Panel kaartRef = kaart;
                    cb.CheckedChanged += delegate { kaartRef.BackColor = cb.Checked ? Color.White : Color.FromArgb(225, 228, 235); pb.Visible = true; tel(); };
                    pb.Click += delegate { cb.Checked = !cb.Checked; };
                    kaart.Controls.Add(pb);
                    kaart.Controls.Add(cb);
                    raster.Controls.Add(kaart);
                    vinkjes.Add(cb);
                }

                Button ok = new Button(); ok.Text = "Maak pdf"; ok.AutoSize = true; ok.BackColor = Accent; ok.ForeColor = Color.White; ok.FlatStyle = FlatStyle.Flat; ok.FlatAppearance.BorderSize = 0; ok.Padding = new Padding(S(10), S(4), S(10), S(4));
                Button annuleer = new Button(); annuleer.Text = "Annuleren"; annuleer.AutoSize = true; annuleer.DialogResult = DialogResult.Cancel;
                Button alles = new Button(); alles.Text = "Alles"; alles.AutoSize = true;
                Button geen = new Button(); geen.Text = "Geen"; geen.AutoSize = true;
                Button laatste = new Button(); laatste.Text = "Alleen laatste"; laatste.AutoSize = true;
                alles.Click += delegate { foreach (CheckBox c in vinkjes) c.Checked = true; };
                geen.Click += delegate { foreach (CheckBox c in vinkjes) c.Checked = false; };
                laatste.Click += delegate { for (int i = 0; i < vinkjes.Count; i++) vinkjes[i].Checked = i == vinkjes.Count - 1; };
                ok.Click += delegate
                {
                    int n = 0; foreach (CheckBox c in vinkjes) if (c.Checked) n++;
                    if (n == 0) { MessageBox.Show(d, "Kies minstens één pagina.", "Hart Werkplek"); return; }
                    d.DialogResult = DialogResult.OK;
                };
                knoppenRij.Controls.Add(ok);
                knoppenRij.Controls.Add(annuleer);
                knoppenRij.Controls.Add(laatste);
                knoppenRij.Controls.Add(geen);
                knoppenRij.Controls.Add(alles);
                knoppenRij.Controls.Add(telling);
                d.CancelButton = annuleer;

                d.Controls.Add(raster);
                d.Controls.Add(knoppenRij);
                d.Controls.Add(boven);
                tel();

                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    List<Bitmap> gekozen = new List<Bitmap>();
                    for (int i = 0; i < vinkjes.Count; i++) if (vinkjes[i].Checked) gekozen.Add(lijst[i]);
                    string t = tb.Text.Trim();
                    foreach (char c in Path.GetInvalidFileNameChars()) t = t.Replace(c, '-');
                    if (t.Length == 0) t = naam;
                    SchrijfPdf(gekozen, UniquePath(t + ".pdf"));
                    RefreshClips();
                    clip.Visible = true;
                    StyleClipButton();
                }
            }
        }
        catch (Exception ex)
        {
            ToonMelding("De pdf maken ging mis: " + ex.Message);
        }
        finally
        {
            foreach (Bitmap b in lijst) { try { b.Dispose(); } catch { } }
        }
    }

    static string PdfNaam(string titel)
    {
        string t = (titel ?? "").Trim();
        foreach (string sep in new string[] { " | ", " - reMarkable", " – reMarkable", " — reMarkable" })
        {
            int i = t.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
            if (i > 0) t = t.Substring(0, i);
        }
        if (t.Length == 0 || t.Equals("reMarkable", StringComparison.OrdinalIgnoreCase)) t = "reMarkable " + DateTime.Now.ToString("yyyy-MM-dd HH.mm");
        foreach (char c in Path.GetInvalidFileNameChars()) t = t.Replace(c, '-');
        return t.Length > 80 ? t.Substring(0, 80) : t;
    }

    static Bitmap OpWit(Image bron)
    {
        Bitmap b = new Bitmap(bron.Width, bron.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.Clear(Color.White);
            g.DrawImage(bron, 0, 0, bron.Width, bron.Height);
        }
        return b;
    }

    // Een eenvoudige pdf: elke pagina is één afbeelding (JPEG), A4-breedte, hoogte volgens de afbeelding
    static void SchrijfPdf(List<Bitmap> paginas, string pad)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        System.Drawing.Imaging.ImageCodecInfo jpg = null;
        foreach (System.Drawing.Imaging.ImageCodecInfo ci in System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders())
            if (ci.MimeType == "image/jpeg") jpg = ci;
        System.Drawing.Imaging.EncoderParameters ep = new System.Drawing.Imaging.EncoderParameters(1);
        ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);

        using (FileStream fs = File.Create(pad))
        {
            List<long> offsets = new List<long>();
            Action<string> w = delegate(string t) { byte[] b = Encoding.ASCII.GetBytes(t); fs.Write(b, 0, b.Length); };
            w("%PDF-1.4\n");
            int n = paginas.Count;
            // objecten: 1 catalogus, 2 pagina's, daarna per pagina 3 objecten (pagina, inhoud, afbeelding)
            offsets.Add(fs.Position); w("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            StringBuilder kids = new StringBuilder();
            for (int i = 0; i < n; i++) kids.Append((3 + i * 3) + " 0 R ");
            offsets.Add(fs.Position); w("2 0 obj\n<< /Type /Pages /Kids [" + kids + "] /Count " + n + " >>\nendobj\n");
            for (int i = 0; i < n; i++)
            {
                Bitmap bmp = paginas[i];
                double pw = 595.0, ph = 595.0 * bmp.Height / bmp.Width;
                int pObj = 3 + i * 3, cObj = pObj + 1, iObj = pObj + 2;
                offsets.Add(fs.Position);
                w(pObj + " 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + pw.ToString("0.##", inv) + " " + ph.ToString("0.##", inv) +
                  "] /Resources << /XObject << /Im0 " + iObj + " 0 R >> >> /Contents " + cObj + " 0 R >>\nendobj\n");
                string inhoud = "q " + pw.ToString("0.##", inv) + " 0 0 " + ph.ToString("0.##", inv) + " 0 0 cm /Im0 Do Q";
                offsets.Add(fs.Position);
                w(cObj + " 0 obj\n<< /Length " + inhoud.Length + " >>\nstream\n" + inhoud + "\nendstream\nendobj\n");
                byte[] data;
                using (MemoryStream ms = new MemoryStream())
                {
                    if (jpg != null) bmp.Save(ms, jpg, ep); else bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                    data = ms.ToArray();
                }
                offsets.Add(fs.Position);
                w(iObj + " 0 obj\n<< /Type /XObject /Subtype /Image /Width " + bmp.Width + " /Height " + bmp.Height +
                  " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + data.Length + " >>\nstream\n");
                fs.Write(data, 0, data.Length);
                w("\nendstream\nendobj\n");
            }
            long xref = fs.Position;
            w("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
            foreach (long o in offsets) w(o.ToString("0000000000", inv) + " 00000 n \n");
            w("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
        }
    }

    void SchrijfSleepInfoTekst(string tekst)
    {
        try
        {
            string pad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "reMarkable-sleepinfo.txt");
            File.WriteAllText(pad, "Hart Werkplek versie " + versie + " - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\r\nEr staan geen documentnamen of inhoud in dit bestand.\r\n\r\n" + tekst, Encoding.UTF8);
        }
        catch { }
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
        string pad = e.ResultFilePath;
        bool vanWerkplek = (op.Uri ?? "").IndexOf("werkplek.local", StringComparison.OrdinalIgnoreCase) >= 0;
        op.StateChanged += delegate
        {
            BeginInvoke(new Action(delegate
            {
                RefreshClips();
                if (op.State == CoreWebView2DownloadState.Completed)
                {
                    // Een mailconcept uit de checklist: rechtstreeks in Outlook bij Concepten zetten
                    if (vanWerkplek && pad.EndsWith(".eml", StringComparison.OrdinalIgnoreCase) && ZetInConcepten(pad))
                    {
                        RefreshClips();
                        return;
                    }
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
        for (int i = knipsels.Count - 1; i >= 0; i--) clipList.Controls.Add(KnipselItem(knipsels[i]));
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
            l.Text = "Nog leeg. Plak bovenin een tekst, download iets in reMarkable of Outlook, of sleep hier een bestand of tekst heen.";
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

        p.Height = S(64);
        bool pdf = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        Label soort = new Label();
        soort.Text = pdf ? "\uEA90" : "\uE8A5";
        soort.Font = new Font("Segoe MDL2 Assets", 16f);
        soort.ForeColor = pdf ? Accent : Muted;
        soort.TextAlign = ContentAlignment.MiddleCenter;
        soort.Location = new Point(S(4), S(12));
        soort.Size = new Size(S(34), S(34));
        p.Controls.Add(soort);

        Label name = new Label();
        name.Text = Path.GetFileName(path);
        name.Font = new Font("Segoe UI Semibold", 9.5f);
        name.ForeColor = Ink;
        name.AutoEllipsis = true;
        name.Location = new Point(S(40), S(10));
        name.Size = new Size(S(206), S(20));
        p.Controls.Add(name);

        Label meta = new Label();
        meta.Text = "sleep naar James  ·  " + File.GetLastWriteTime(path).ToString("HH:mm");
        meta.Font = new Font("Segoe UI", 8.5f);
        meta.ForeColor = Muted;
        meta.Location = new Point(S(40), S(32));
        meta.Size = new Size(S(110), S(18));
        p.Controls.Add(meta);

        Button open = KlembordIcoon("\uE8A7", "Openen");
        open.Location = new Point(S(150), S(30));
        open.Click += delegate { try { Process.Start(path); } catch (Exception ex) { MessageBox.Show(this, "Openen lukte niet.\n\n" + ex.Message, "Hart Werkplek"); } };
        p.Controls.Add(open);

        Button copy = KlembordIcoon("\uE8C8", "Kopieer de bestandslocatie (plakken in James bij 'bestand kiezen')");
        copy.Location = new Point(S(182), S(30));
        copy.Click += delegate { Clipboard.SetText(path); Bevestig(copy); };
        p.Controls.Add(copy);

        Button done = KlembordIcoon("\uE74D", "Weghalen");
        done.Location = new Point(S(214), S(30));
        done.Click += delegate { try { File.Delete(path); } catch { } RefreshClips(); };
        p.Controls.Add(done);
        soort.MouseDown += delegate(object s3, MouseEventArgs e3) { if (e3.Button == MouseButtons.Left) p.DoDragDrop(new DataObject(DataFormats.FileDrop, new string[] { path }), DragDropEffects.Copy); };
        soort.Cursor = Cursors.SizeAll;

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

    // ---------- tekstknipsels op het klembord (alleen in het geheugen) ----------
    List<string> knipsels = new List<string>();

    void VoegTekstToe(string tekst)
    {
        knipsels.Remove(tekst);
        knipsels.Add(tekst);
        while (knipsels.Count > 20) knipsels.RemoveAt(0);
        if (!clip.Visible) { clip.Visible = true; StyleClipButton(); }
        RefreshClips();
    }

    Button KlembordIcoon(string glyph, string tip)
    {
        Button b = new Button();
        b.Text = glyph;
        b.Font = new Font("Segoe MDL2 Assets", 10.5f);
        b.Size = new Size(S(30), S(28));
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.White;
        b.ForeColor = Ink;
        b.BackColor = Color.Transparent;
        b.Cursor = Cursors.Hand;
        b.AccessibleName = tip;
        tips.SetToolTip(b, tip);
        return b;
    }

    // even een vinkje laten zien na kopiëren
    void Bevestig(Button b)
    {
        string oud = b.Text;
        Color kleur = b.ForeColor;
        b.Text = "\uE73E";
        b.ForeColor = Color.FromArgb(47, 122, 85);
        System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
        t.Interval = 1200;
        t.Tick += delegate { t.Stop(); t.Dispose(); if (!b.IsDisposed) { b.Text = oud; b.ForeColor = kleur; } };
        t.Start();
    }

    Control KnipselItem(string tekst)
    {
        Panel p = new Panel();
        p.Width = S(250);
        p.Height = S(70);
        p.Margin = new Padding(0, 0, 0, S(8));
        p.BackColor = AccentSoft;
        p.Cursor = Cursors.Hand;

        Label icoon = new Label();
        icoon.Text = "\uE70B";
        icoon.Font = new Font("Segoe MDL2 Assets", 14f);
        icoon.ForeColor = Accent;
        icoon.TextAlign = ContentAlignment.MiddleCenter;
        icoon.Location = new Point(S(4), S(8));
        icoon.Size = new Size(S(34), S(30));
        p.Controls.Add(icoon);

        Label l = new Label();
        l.Text = tekst.Replace("\r", "").Replace("\n", " ");
        l.Font = new Font("Segoe UI", 9f);
        l.ForeColor = Ink;
        l.AutoEllipsis = true;
        l.Location = new Point(S(40), S(8));
        l.Size = new Size(S(172), S(54));
        l.Cursor = Cursors.Hand;
        p.Controls.Add(l);
        tips.SetToolTip(l, tekst.Length > 400 ? tekst.Substring(0, 400) + "…" : tekst);

        Button kopie = KlembordIcoon("\uE8C8", "Kopiëren");
        kopie.Location = new Point(S(214), S(6));
        p.Controls.Add(kopie);
        Button weg = KlembordIcoon("\uE74D", "Weghalen");
        weg.Location = new Point(S(214), S(36));
        weg.Click += delegate { knipsels.Remove(tekst); RefreshClips(); };
        p.Controls.Add(weg);

        EventHandler kopieer = delegate { try { Clipboard.SetText(tekst); Bevestig(kopie); } catch { } };
        kopie.Click += kopieer;
        l.Click += kopieer;
        // ook rechtstreeks in een veld in James te slepen
        icoon.MouseDown += delegate(object s5, MouseEventArgs e5)
        {
            if (e5.Button == MouseButtons.Left) p.DoDragDrop(new DataObject(DataFormats.UnicodeText, tekst), DragDropEffects.Copy);
        };
        icoon.Cursor = Cursors.SizeAll;
        tips.SetToolTip(icoon, "Sleep naar een veld, of klik om te kopiëren");
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
    // ---------- mailconcept (.eml uit de checklist) in Outlook bij Concepten zetten ----------
    // Werkt met de klassieke Outlook voor Windows; het concept staat daarna ook in Outlook op het web.
    bool ZetInConcepten(string emlPad)
    {
        Type olType = null;
        try { olType = Type.GetTypeFromProgID("Outlook.Application"); } catch { }
        if (olType == null)
        {
            ToonMelding("Er is geen klassieke Outlook op deze computer gevonden, dus het concept staat hier op het klembord. Klik op Open om het te openen.");
            return false;
        }
        string tmp = Path.Combine(Path.GetTempPath(), "hartwerkplek_mail_" + Guid.NewGuid().ToString("N"));
        try
        {
            string tekst = File.ReadAllText(emlPad, Encoding.ASCII);
            int kopEind = tekst.IndexOf("\r\n\r\n");
            string kop = kopEind > 0 ? tekst.Substring(0, kopEind) : "";
            string aan = KopWaarde(kop, "To");
            string onderwerp = DecodeerKop(KopWaarde(kop, "Subject"));

            string html = null, platteTekst = null;
            byte[] logo = null; string logoCid = null;
            List<KeyValuePair<string, byte[]>> bijlagen = new List<KeyValuePair<string, byte[]>>();
            foreach (string deel in Regex.Split(tekst, @"\r\n--=_[A-Za-z]+_[A-Za-z0-9]+(?:--)?\r\n"))
            {
                int k = deel.IndexOf("\r\n\r\n");
                if (k < 0) continue;
                string dk = deel.Substring(0, k), lijf = deel.Substring(k + 4);
                string ct = KopWaarde(dk, "Content-Type").ToLowerInvariant();
                if (ct.Length == 0 || ct.StartsWith("multipart/")) continue;
                byte[] data;
                if (KopWaarde(dk, "Content-Transfer-Encoding").ToLowerInvariant().Contains("base64"))
                {
                    string b64 = Regex.Replace(lijf, @"\s+", "");
                    int eind = b64.IndexOf("--"); if (eind >= 0) b64 = b64.Substring(0, eind);
                    data = Convert.FromBase64String(b64);
                }
                else data = Encoding.UTF8.GetBytes(lijf);

                if (ct.StartsWith("text/html")) html = Encoding.UTF8.GetString(data);
                else if (ct.StartsWith("text/plain")) platteTekst = Encoding.UTF8.GetString(data);
                else
                {
                    string cid = KopWaarde(dk, "Content-ID").Trim('<', '>', ' ');
                    if (cid.Length > 0 && ct.StartsWith("image/")) { logo = data; logoCid = cid; continue; }
                    string naam = null;
                    Match m = Regex.Match(dk, @"filename\*=UTF-8''([^\s;]+)", RegexOptions.IgnoreCase);
                    if (m.Success) naam = Uri.UnescapeDataString(m.Groups[1].Value);
                    if (naam == null) { m = Regex.Match(dk, "filename=\"([^\"]+)\"", RegexOptions.IgnoreCase); if (m.Success) naam = m.Groups[1].Value; }
                    if (naam == null) naam = "bijlage";
                    foreach (char c in Path.GetInvalidFileNameChars()) naam = naam.Replace(c, '-');
                    bijlagen.Add(new KeyValuePair<string, byte[]>(naam, data));
                }
            }

            Directory.CreateDirectory(tmp);
            dynamic ol = Activator.CreateInstance(olType);
            dynamic mail = ol.CreateItem(0);
            mail.To = aan;
            mail.Subject = onderwerp;
            if (logo != null && html != null)
            {
                string lp = Path.Combine(tmp, "logo.png");
                File.WriteAllBytes(lp, logo);
                dynamic la = mail.Attachments.Add(lp, 1, 0);
                la.PropertyAccessor.SetProperty("http://schemas.microsoft.com/mapi/proptag/0x3712001F", logoCid);
            }
            if (html != null) mail.HTMLBody = html;
            else if (platteTekst != null) mail.Body = platteTekst;
            foreach (KeyValuePair<string, byte[]> b in bijlagen)
            {
                string bp = Path.Combine(tmp, b.Key);
                File.WriteAllBytes(bp, b.Value);
                mail.Attachments.Add(bp);
            }
            mail.Save();
            try { Marshal.ReleaseComObject(mail); Marshal.ReleaseComObject(ol); } catch { }
            try { File.Delete(emlPad); } catch { }
            ToonMelding("Het mailconcept staat in Outlook bij Concepten" + (string.IsNullOrEmpty(aan) ? " (vul daar nog het e-mailadres in)." : "."));
            return true;
        }
        catch (Exception ex)
        {
            ToonMelding("Het concept in Outlook zetten lukte niet (" + ex.Message + "). Het staat hier op het klembord; klik op Open.");
            return false;
        }
        finally
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
        }
    }

    static string KopWaarde(string kop, string naam)
    {
        Match m = Regex.Match(kop, "^" + Regex.Escape(naam) + @":[ \t]*(.*(?:\r\n[ \t].*)*)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return m.Success ? Regex.Replace(m.Groups[1].Value, @"\r\n[ \t]+", " ").Trim() : "";
    }

    static string DecodeerKop(string v)
    {
        return Regex.Replace(v, @"=\?UTF-8\?B\?([A-Za-z0-9+/=]+)\?=", delegate(Match m)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value)); } catch { return m.Value; }
        }, RegexOptions.IgnoreCase);
    }

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
        string nieuw = "0";
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
            nieuw = t.Trim();
        }
        catch { return; }
        if (VergelijkVersie(nieuw, versie) > 0)
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
