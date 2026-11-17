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

    string ResolveUrl(string key, string label)
    {
        string v = Cfg(key);
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
        AddTab("dsmtool", "DSM-tool", null);
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

        // onderkant: wat het laatst wordt toegevoegd, komt het verst onderaan
        btnUpdate = SideButton("Bijwerken");
        btnUpdate.Dock = DockStyle.Bottom;
        btnUpdate.Click += delegate { Bijwerken(); };
        side.Controls.Add(btnUpdate);

        Button btnCfg = SideButton("Instellingen");
        btnCfg.Dock = DockStyle.Bottom;
        btnCfg.Click += delegate { OpenSettings(); };
        side.Controls.Add(btnCfg);

        btnFull = SideButton("Venster  (F11)");
        btnFull.Dock = DockStyle.Bottom;
        btnFull.Click += delegate { SetFull(!isFull); };
        side.Controls.Add(btnFull);

        Button btnExit = SideButton("Afsluiten");
        btnExit.Dock = DockStyle.Bottom;
        btnExit.Click += delegate { Close(); };
        side.Controls.Add(btnExit);

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
            btnFull.Text = "   Venster  (F11)";
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Maximized;
            btnFull.Text = "   Volledig scherm  (F11)";
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
        hint.Dock = DockStyle.Top;
        hint.Height = S(64);
        hint.Padding = new Padding(S(14), 0, S(14), 0);
        hint.ForeColor = Muted;
        hint.Font = new Font("Segoe UI", 9f);
        hint.Text = "Downloads komen hier binnen. Sleep een bestand naar James om het te uploaden. Na 1 dag wordt het vanzelf gewist.";
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

    void ClipDragEnter(object sender, DragEventArgs e)
    {
        e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    void ClipDragDrop(object sender, DragEventArgs e)
    {
        string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files == null) return;
        foreach (string f in files)
        {
            if (!File.Exists(f)) continue;
            if (Path.GetDirectoryName(f).TrimEnd('\\') == clipDir.TrimEnd('\\')) continue;
            try { File.Copy(f, UniquePath(Path.GetFileName(f))); } catch { }
        }
        RefreshClips();
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
        p.Height = S(72);
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
        meta.Text = "sleep naar James  ·  " + File.GetLastWriteTime(path).ToString("HH:mm");
        meta.Font = new Font("Segoe UI", 8.5f);
        meta.ForeColor = Muted;
        meta.Location = new Point(S(10), S(30));
        meta.Size = new Size(S(160), S(18));
        p.Controls.Add(meta);

        Button copy = SmallButton("Pad");
        copy.Size = new Size(S(44), S(26));
        copy.Location = new Point(S(150), S(40));
        copy.Click += delegate { Clipboard.SetText(path); };
        p.Controls.Add(copy);

        Button done = SmallButton("Klaar");
        done.Size = new Size(S(52), S(26));
        done.Location = new Point(S(196), S(40));
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
            btnUpdate.Text = "   Bijwerken  (nieuw)";
            btnUpdate.ForeColor = Accent;
            btnUpdate.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
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
