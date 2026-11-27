// HoldKeys : maintenir une lettre + Espace / flèches pour choisir un caractère spécial.
// Compilé avec le csc de .NET Framework 4 (C# 5) — voir build.ps1.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HoldKeys
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool background = Array.IndexOf(args, "--background") >= 0;
            bool created;
            var mutex = new System.Threading.Mutex(true, "HoldKeys_SingleInstance", out created);
            var showEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "HoldKeys_ShowSettings");
            if (!created)
            {
                // Déjà lancé : relancer HoldKeys (menu Démarrer) ouvre les paramètres.
                if (!background) showEvent.Set();
                return;
            }
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.Run(new TrayContext(showEvent, !background));
            GC.KeepAlive(mutex);
        }
    }

    class TrayContext : ApplicationContext
    {
        const uint VK_BACK = 0x08, VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_LEFT = 0x25, VK_RIGHT = 0x27;
        const uint VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_CAPITAL = 0x14;

        readonly string cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HoldKeys.txt");
        Dictionary<string, string[]> map = new Dictionary<string, string[]>();
        int delayMs = 250;
        int autoShowMs = 400; // 0 = désactivé
        readonly Timer holdTimer = new Timer();
        uint timerVk, timerTime;

        NotifyIcon tray;
        Popup popup;
        Native.LowLevelKeyboardProc proc;
        IntPtr hook;
        bool paused;

        uint heldVk;          // lettre actuellement maintenue (0 = aucune)
        uint heldTime;        // instant du premier appui
        bool heldUpper;
        bool popupOn;
        string[] cur;
        int sel;
        readonly HashSet<uint> swallowUp = new HashSet<uint>();

        public TrayContext(System.Threading.WaitHandle showEvent, bool openSettings)
        {
            Instance = this;
            string dir = Path.GetDirectoryName(cfgPath);
            Usage.Load(dir);
            ClipboardHistory.Init(dir);
            LoadConfig();
            popup = new Popup();
            var h = popup.Handle; // force la création du handle (BeginInvoke)
            holdTimer.Tick += delegate { holdTimer.Stop(); AutoShow(); };
            popup.ItemHovered += i => { if (popupOn) sel = i; };
            popup.ItemClicked += i => { if (popupOn && i < cur.Length) Commit(cur[i]); };

            AppIcon = MakeIcon();
            var menu = new ContextMenuStrip();
            var pause = new ToolStripMenuItem("Pause");
            pause.Click += delegate { paused = !paused; pause.Checked = paused; Cancel(); };
            var startup = new ToolStripMenuItem("Démarrer avec Windows");
            startup.Click += delegate { SetAutoStart(!startup.Checked); };
            menu.Opening += delegate { startup.Checked = IsAutoStart(); };
            var settingsItem = new ToolStripMenuItem("Paramètres…", null, delegate { ShowSettings(); });
            settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
            searchItem = new ToolStripMenuItem("Ouvrir le lanceur…", null, delegate { ShowSearch(); });
            menu.Items.Add(settingsItem);
            menu.Items.Add(searchItem);
            menu.Items.Add("Ouvrir le fichier de configuration", null, delegate { System.Diagnostics.Process.Start("notepad.exe", "\"" + cfgPath + "\""); });
            menu.Items.Add("Recharger la configuration", null, delegate { LoadConfig(); tray.ShowBalloonTip(1500, "HoldKeys", "Configuration rechargée", ToolTipIcon.Info); });
            menu.Items.Add(pause);
            menu.Items.Add(startup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quitter", null, delegate { ExitThread(); });

            tray = new NotifyIcon();
            tray.Icon = new Icon(AppIcon, SystemInformation.SmallIconSize);
            tray.DoubleClick += delegate { ShowSettings(); };
            tray.Text = "HoldKeys — maintenir une lettre + Espace";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;

            proc = HookProc;
            hook = Native.SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, proc, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
                MessageBox.Show("Impossible d'installer le hook clavier.", "HoldKeys");

            hotkeyWnd.Pressed += ShowSearch;
            hotkeyWnd.ClipboardChanged += () => LauncherForm.Delay(60, ClipboardHistory.OnChanged);
            hotkeyWnd.ListenClipboard();
            RegisterSearchKey(false);

            System.Threading.ThreadPool.RegisterWaitForSingleObject(showEvent,
                delegate { popup.BeginInvoke((Action)ShowSettings); }, null, -1, false);
            if (openSettings) popup.BeginInvoke((Action)ShowSettings);
        }

        internal static Icon AppIcon;
        SettingsForm settings;

        internal static TrayContext Instance;

        internal static void Notify(string text)
        {
            if (Instance != null && Instance.tray != null)
                Instance.tray.ShowBalloonTip(4000, "HoldKeys", text, ToolTipIcon.Info);
        }

        // ---------- Lanceur ----------

        readonly HotkeyWindow hotkeyWnd = new HotkeyWindow();
        ToolStripMenuItem searchItem;
        LauncherForm search;
        List<Sym> symbols;
        string searchKey = "Win+Alt+Space";

        void RegisterSearchKey(bool notifyOk)
        {
            bool ok = hotkeyWnd.Register(searchKey);
            if (searchItem != null) searchItem.ShortcutKeyDisplayString = ok ? searchKey : "";
            if (!ok && !string.IsNullOrEmpty(searchKey) && tray != null)
                tray.ShowBalloonTip(3000, "HoldKeys", "Le raccourci " + searchKey + " est déjà utilisé par une autre application. Choisissez-en un autre dans Paramètres.", ToolTipIcon.Warning);
        }

        void ShowSearch()
        {
            if (search == null || search.IsDisposed)
            {
                Action settingsAct = () => popup.BeginInvoke((Action)ShowSettings);
                var providers = new List<Provider>
                {
                    new AppsProvider(),
                    new WindowsProvider(),
                    new CalcProvider(),
                    new PathProvider(),
                    new UrlProvider(),
                    new FilesProvider(),
                    new SystemProvider(settingsAct),
                    new ShellProvider(),
                    new GeneratorsProvider(),
                    new TextActionsProvider(),
                    new SymbolsProvider(
                        () => symbols ?? (symbols = SymbolDb.Build(EntriesFromMap())),
                        () => { List<string> r; return recent.TryGetValue("*", out r) ? (IList<string>)r : new string[0]; },
                        s => Remember("*", s)),
                    new ClipboardProvider(),
                    new SnippetsProvider(() => popup.BeginInvoke((Action)(() => { ShowSettings(); settings.ShowTab(2); }))),
                    new WebProvider(),
                };
                search = new LauncherForm(providers);
            }
            search.Open();
        }

        IEnumerable<KeyValuePair<string, string[]>> EntriesFromMap()
        {
            foreach (var kv in map) yield return kv;
        }

        internal void ShowSettings()
        {
            if (settings == null || settings.IsDisposed)
            {
                settings = new SettingsForm(cfgPath, LoadConfig, ClearRecent);
                settings.Show();
            }
            if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
            settings.Activate();
        }

        protected override void ExitThreadCore()
        {
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            hotkeyWnd.Unregister();
            hotkeyWnd.DestroyHandle();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }

        // ---------- Clavier ----------

        IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !paused)
            {
                var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                bool injected = (k.flags & 0x10) != 0;
                if (!injected && HandleKey(k, (int)wParam)) return (IntPtr)1;
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        bool HandleKey(Native.KBDLLHOOKSTRUCT k, int msg)
        {
            bool down = msg == 0x100 || msg == 0x104;
            uint vk = k.vkCode;

            if (!down)
            {
                if (swallowUp.Remove(vk)) return true;
                if (vk == heldVk)
                {
                    heldVk = 0;
                    holdTimer.Stop();
                    if (popupOn)
                    {
                        if (sel < 0) Cancel(); // rien de choisi : on garde la lettre
                        else Commit(cur[sel]);
                    }
                }
                return false;
            }

            // Répétition automatique de la lettre maintenue : on l'avale si elle a des variantes.
            if (vk == heldVk) return popupOn || Lookup(vk, heldUpper) != null;
            if (!popupOn) TrackSnippet(k);

            if (popupOn)
            {
                if (IsActivation(vk))
                {
                    int n = cur.Length;
                    if (sel < 0) sel = vk == VK_LEFT ? n - 1 : 0;
                    else sel = vk == VK_LEFT ? (sel - 1 + n) % n : (sel + 1) % n;
                    int s = sel;
                    popup.BeginInvoke((Action)(() => popup.Select(s)));
                    swallowUp.Add(vk);
                    return true;
                }
                if (vk >= '1' && vk <= '9' && vk - '1' < cur.Length)
                {
                    Commit(cur[vk - '1']); // choix direct par numéro
                    swallowUp.Add(vk);
                    return true;
                }
                if (vk == VK_ESCAPE)
                {
                    Cancel();
                    heldVk = 0;
                    swallowUp.Add(vk);
                    return true;
                }
                if (IsModifier(vk)) return false;
                Cancel(); // autre touche : on ferme et on laisse passer
            }

            if (heldVk != 0 && IsActivation(vk))
            {
                string[] items = Lookup(heldVk, heldUpper);
                if (items != null && unchecked(k.time - heldTime) >= (uint)delayMs)
                {
                    popupOn = true;
                    curKey = RecKey(heldVk, heldUpper);
                    items = Ordered(curKey, items);
                    cur = items;
                    sel = vk == VK_LEFT ? items.Length - 1 : 0;
                    int s = sel;
                    popup.BeginInvoke((Action)(() => popup.ShowItems(items, s)));
                    swallowUp.Add(vk);
                    return true;
                }
                heldVk = 0; // frappe rapide normale (ex. "la maison")
                return false;
            }

            if (IsModifier(vk)) return false;

            if (KeyName(vk) != null && !Down(VK_CONTROL) && !Down(VK_MENU))
            {
                heldVk = vk;
                heldTime = k.time;
                heldUpper = Down(VK_SHIFT) ^ ((Native.GetKeyState((int)VK_CAPITAL) & 1) != 0);
                holdTimer.Stop();
                if (autoShowMs > 0 && Lookup(vk, heldUpper) != null)
                {
                    timerVk = vk; timerTime = k.time;
                    holdTimer.Interval = autoShowMs;
                    holdTimer.Start();
                }
            }
            else { heldVk = 0; holdTimer.Stop(); }
            return false;
        }

        // Popup ouvert automatiquement quand la lettre reste maintenue (aucune sélection au départ).
        void AutoShow()
        {
            if (paused || popupOn || heldVk == 0 || heldVk != timerVk || heldTime != timerTime) return;
            string[] items = Lookup(heldVk, heldUpper);
            if (items == null) return;
            popupOn = true;
            curKey = RecKey(heldVk, heldUpper);
            items = Ordered(curKey, items);
            cur = items;
            sel = -1;
            popup.ShowItems(items, -1);
        }

        void Commit(string s)
        {
            popupOn = false;
            typed.Length = 0;
            string key = curKey;
            popup.BeginInvoke((Action)(() => { popup.HideNow(); SendReplace(s); Remember(key, s); Remember("*", s); }));
        }

        // ---------- Derniers caractères utilisés ----------

        string RecentPath { get { return Path.Combine(Path.GetDirectoryName(cfgPath), "HoldKeys.recent.txt"); } }
        Dictionary<string, List<string>> recent = new Dictionary<string, List<string>>();
        bool useRecent = true;
        string curKey;

        static string RecKey(uint vk, bool upper)
        {
            string k = KeyName(vk);
            return upper ? k.ToUpperInvariant() : k;
        }

        // Les caractères récents (du plus récent au plus ancien) d'abord, puis le reste dans l'ordre de la config.
        string[] Ordered(string key, string[] items)
        {
            List<string> r;
            if (!useRecent || key == null || !recent.TryGetValue(key, out r)) return items;
            var res = new List<string>();
            foreach (string s in r) if (Array.IndexOf(items, s) >= 0 && !res.Contains(s)) res.Add(s);
            foreach (string s in items) if (!res.Contains(s)) res.Add(s);
            return res.ToArray();
        }

        void Remember(string key, string s)
        {
            if (key == null) return;
            List<string> r;
            if (!recent.TryGetValue(key, out r)) recent[key] = r = new List<string>();
            r.Remove(s);
            r.Insert(0, s);
            if (r.Count > 20) r.RemoveRange(20, r.Count - 20);
            SaveRecent();
        }

        void LoadRecent()
        {
            recent = new Dictionary<string, List<string>>();
            try
            {
                if (!File.Exists(RecentPath)) return;
                foreach (string line in File.ReadAllLines(RecentPath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    var items = line.Substring(eq + 1).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (key.Length == 1 && items.Length > 0) recent[key] = new List<string>(items);
                }
            }
            catch { }
        }

        void SaveRecent()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in recent) sb.AppendLine(kv.Key + " = " + string.Join(" ", kv.Value));
                File.WriteAllText(RecentPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        void ClearRecent()
        {
            recent.Clear();
            try { File.Delete(RecentPath); } catch { }
        }

        void Cancel()
        {
            if (!popupOn) return;
            popupOn = false;
            popup.BeginInvoke((Action)(() => popup.HideNow()));
        }

        static bool IsActivation(uint vk) { return vk == VK_SPACE || vk == VK_LEFT || vk == VK_RIGHT; }
        static bool IsModifier(uint vk) { return (vk >= 0x10 && vk <= 0x12) || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C || vk == VK_CAPITAL; }
        static bool Down(uint vk) { return (Native.GetKeyState((int)vk) & 0x8000) != 0; }

        static string KeyName(uint vk)
        {
            if ((vk >= 'A' && vk <= 'Z') || (vk >= '0' && vk <= '9')) return ((char)vk).ToString().ToLowerInvariant();
            return null;
        }

        string[] Lookup(uint vk, bool upper)
        {
            string key = KeyName(vk);
            if (key == null) return null;
            string[] items;
            if (upper)
            {
                if (map.TryGetValue(key.ToUpperInvariant(), out items)) return items;
                if (map.TryGetValue(key, out items))
                    return Array.ConvertAll(items, x => x.ToUpperInvariant());
                return null;
            }
            return map.TryGetValue(key, out items) ? items : null;
        }

        static void SendReplace(string s) { SendText(s, true); }

        internal static void SendText(string s, bool backspace) { SendText(s, backspace ? 1 : 0); }

        // Efface `backspaces` caractères puis tape le texte (retours à la ligne = touche Entrée).
        internal static void SendText(string s, int backspaces)
        {
            var list = new List<Native.INPUT>();
            for (int i = 0; i < backspaces; i++)
            {
                list.Add(Native.KeyInput((ushort)VK_BACK, '\0', 0));
                list.Add(Native.KeyInput((ushort)VK_BACK, '\0', 2 /*KEYUP*/));
            }
            foreach (char c in s)
            {
                if (c == '\r') continue;
                if (c == '\n')
                {
                    list.Add(Native.KeyInput(0x0D, '\0', 0));
                    list.Add(Native.KeyInput(0x0D, '\0', 2));
                    continue;
                }
                list.Add(Native.KeyInput(0, c, 4 /*UNICODE*/));
                list.Add(Native.KeyInput(0, c, 4 | 2));
            }
            Native.SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf(typeof(Native.INPUT)));
        }

        // ---------- Texte rapide : suivi des dernières frappes ----------

        readonly StringBuilder typed = new StringBuilder();
        IntPtr typedWindow;

        void TrackSnippet(Native.KBDLLHOOKSTRUCT k)
        {
            if (SnippetStore.Items.Count == 0) return;
            uint vk = k.vkCode;
            if (IsModifier(vk)) return;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != typedWindow) { typed.Length = 0; typedWindow = fg; }
            if (vk == VK_BACK) { if (typed.Length > 0) typed.Length--; return; }
            bool altGr = Down(VK_CONTROL) && Down(VK_MENU);
            if ((Down(VK_CONTROL) || Down(VK_MENU) || Down(0x5B) || Down(0x5C)) && !altGr) { typed.Length = 0; return; }
            string ch = Native.CharFor(k, fg);
            if (string.IsNullOrEmpty(ch) || ch[0] < 0x20) { typed.Length = 0; return; }
            typed.Append(ch);
            if (typed.Length > 64) typed.Remove(0, typed.Length - 64);
            string trig = SnippetStore.Find(typed.ToString());
            if (trig == null) return;
            typed.Length = 0;
            string text = SnippetStore.TextOf(trig);
            int n = trig.Length;
            // Après le hook : la dernière touche de l'abréviation est déjà dans la file, on l'efface aussi.
            popup.BeginInvoke((Action)(() => SendText(SnippetStore.Expand(text), n)));
        }

        // ---------- Configuration ----------

        internal const string DefaultConfig =
@"# HoldKeys — maintenez une lettre : le popup s'ouvre tout seul (ou appuyez sur Espace).
# Espace / → = suivant, ← = précédent, 1-9 = choix direct.
# Relâchez la lettre pour insérer le caractère sélectionné. Échap = annuler.
# Après modification : clic droit sur l'icône > Recharger la configuration.
#
# Format :  lettre = car1 car2 car3 ...   (séparés par des espaces)
# Une clé en minuscule sert aussi pour les majuscules (Maj / Verr. Maj),
# sauf si vous définissez la majuscule séparément (ex :  E = É È Ê Ë).
# Touches possibles : a-z et 0-9.

# Délai minimum (ms) pendant lequel la lettre doit être maintenue avant Espace
delay = 250

# Ouverture automatique du popup après X ms de maintien (0 = seulement avec Espace)
autoshow = 400

# Afficher en premier les derniers caractères utilisés (1 = oui, 0 = non)
recent = 1

# Raccourci de la recherche de symboles par nom (vide = désactivé)
searchkey = Win+Alt+Space

a = à â ä á ã å æ ª @
c = ç ć č ©
d = ð
e = é è ê ë ē ę €
g = ğ
i = î ï í ì ī
l = ł
m = µ
n = ñ ń
o = ô ö ó ò õ ø œ º °
p = ¶ π
r = ®
s = ß š ś §
t = ™ þ
u = ù û ü ú ū
x = ×
y = ÿ ý
z = ž ź ż
1 = ¹ ½ ¼ ¡
2 = ²
3 = ³ ¾
0 = ° ∅
";

        void LoadConfig()
        {
            try
            {
                var c = Config.Load(cfgPath);
                delayMs = c.Delay;
                autoShowMs = c.AutoShow;
                useRecent = c.Recent;
                ClipboardHistory.Enabled = c.Clipboard;
                SnippetStore.Load(Path.GetDirectoryName(cfgPath));
                map = c.ToMap();
                symbols = null; // reconstruit à la prochaine recherche (nouveaux caractères de la config)
                LoadRecent();
                if (c.SearchKey != searchKey)
                {
                    searchKey = c.SearchKey;
                    if (tray != null) RegisterSearchKey(true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur de configuration : " + ex.Message, "HoldKeys");
            }
        }

        // ---------- Démarrage auto ----------

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        internal static bool IsAutoStart()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("HoldKeys") != null;
        }

        internal static void SetAutoStart(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("HoldKeys", "\"" + Application.ExecutablePath + "\" --background");
                else k.DeleteValue("HoldKeys", false);
            }
        }

        // Le logo est généré par make-icon.ps1 et intégré à l'exe (HoldKeys.ico).
        static Icon MakeIcon()
        {
            using (var st = typeof(TrayContext).Assembly.GetManifestResourceStream("HoldKeys.ico"))
                return new Icon(st);
        }
    }

    // ---------- Fenêtre de sélection ----------

    class Popup : Form
    {
        string[] items = new string[0];
        int sel;
        float scale = 1f;
        Font font, small;

        static readonly Color Bg = Color.FromArgb(32, 32, 32);
        static readonly Color Cell = Color.FromArgb(48, 48, 48);
        static readonly Color Accent = Color.FromArgb(0, 120, 215);

        public Popup()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Bg;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            font = new Font("Segoe UI", 26 * scale, GraphicsUnit.Pixel);
            small = new Font("Segoe UI", 10 * scale, GraphicsUnit.Pixel);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /*NOACTIVATE*/ | 0x80 /*TOOLWINDOW*/ | 0x8 /*TOPMOST*/;
                return cp;
            }
        }

        int CellSize { get { return (int)(56 * scale); } }
        int Pad { get { return (int)(8 * scale); } }
        int Gap { get { return (int)(4 * scale); } }

        public void ShowItems(string[] list, int selected)
        {
            items = list;
            sel = selected;
            shownAt = Cursor.Position;
            int w = Pad * 2 + items.Length * CellSize + (items.Length - 1) * Gap;
            int h = Pad * 2 + CellSize;
            Rectangle r = Place(w, h);
            Native.SetWindowPos(Handle, new IntPtr(-1) /*TOPMOST*/, r.X, r.Y, w, h, 0x0010 /*NOACTIVATE*/ | 0x0040 /*SHOWWINDOW*/);
            using (var p = RoundRect(new RectangleF(0, 0, w, h), 10 * scale)) Region = new Region(p);
            Invalidate();
        }

        public void Select(int i) { sel = i; Invalidate(); }

        // ----- Souris : survol = sélection, clic = insertion -----
        public event Action<int> ItemHovered, ItemClicked;
        Point shownAt;

        Rectangle CellRect(int i) { return new Rectangle(Pad + i * (CellSize + Gap), Pad, CellSize, CellSize); }

        int HitTest(Point p)
        {
            for (int i = 0; i < items.Length; i++) if (CellRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Cursor.Position == shownAt) return; // la fenêtre est apparue sous la souris : pas un vrai survol
            int i = HitTest(e.Location);
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            if (i >= 0 && i != sel) { sel = i; Invalidate(); if (ItemHovered != null) ItemHovered(i); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = HitTest(e.Location);
            if (i >= 0 && e.Button == MouseButtons.Left && ItemClicked != null) ItemClicked(i);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21 /*WM_MOUSEACTIVATE*/) { m.Result = (IntPtr)3 /*MA_NOACTIVATE*/; return; }
            base.WndProc(ref m);
        }

        public void HideNow() { Native.ShowWindow(Handle, 0); }

        // Près du curseur texte si l'application le signale, sinon en haut au centre de l'écran actif.
        Rectangle Place(int w, int h)
        {
            IntPtr fg = Native.GetForegroundWindow();
            Screen sc = fg != IntPtr.Zero ? Screen.FromHandle(fg) : Screen.PrimaryScreen;
            Rectangle wa = sc.WorkingArea;
            int x = wa.Left + (wa.Width - w) / 2, y = wa.Top + (int)(wa.Height * 0.08);

            uint pid;
            uint tid = Native.GetWindowThreadProcessId(fg, out pid);
            var gi = new Native.GUITHREADINFO();
            gi.cbSize = Marshal.SizeOf(gi);
            if (fg != IntPtr.Zero && Native.GetGUIThreadInfo(tid, ref gi) && gi.hwndCaret != IntPtr.Zero)
            {
                var p = new Native.POINT { x = gi.rcCaret.left, y = gi.rcCaret.bottom };
                Native.ClientToScreen(gi.hwndCaret, ref p);
                wa = Screen.FromPoint(new Point(p.x, p.y)).WorkingArea;
                x = p.x - w / 2;
                y = p.y + (int)(10 * scale);
                if (y + h > wa.Bottom) y = p.y - (gi.rcCaret.bottom - gi.rcCaret.top) - h - (int)(10 * scale);
            }
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - w));
            y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - h));
            return new Rectangle(x, y, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < items.Length; i++)
            {
                var r = new Rectangle(Pad + i * (CellSize + Gap), Pad, CellSize, CellSize);
                using (var b = new SolidBrush(i == sel ? Accent : Cell))
                using (var p = RoundRect(r, 6 * scale))
                    g.FillPath(b, p);
                TextRenderer.DrawText(g, items[i], font, r, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                if (i < 9)
                {
                    var ir = new Rectangle(r.X + (int)(4 * scale), r.Y + (int)(2 * scale), r.Width, (int)(14 * scale));
                    TextRenderer.DrawText(g, (i + 1).ToString(), small, ir, i == sel ? Color.FromArgb(200, 225, 255) : Color.Gray, TextFormatFlags.NoPadding);
                }
            }
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            float d = rad * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ---------- Win32 ----------

    static class Native
    {
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public InputUnion u; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize, flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public RECT rcCaret;
        }

        public static INPUT KeyInput(ushort vk, char scan, uint flags)
        {
            var i = new INPUT { type = 1 /*KEYBOARD*/ };
            i.u.ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags };
            return i;
        }

        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO gui);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr w, IntPtr l, int flags, int timeout, out IntPtr result);
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, int flags);
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hwnd);
        delegate bool EnumProc(IntPtr hwnd, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc fn, IntPtr l);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr GetClassLongPtr64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetClassLongW")] static extern uint GetClassLong32(IntPtr hwnd, int index);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint tid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder sb, int size, uint flags, IntPtr hkl);

        public static IntPtr GetClassLongPtrSafe(IntPtr hwnd, int index)
        {
            return IntPtr.Size == 8 ? GetClassLongPtr64(hwnd, index) : new IntPtr((int)GetClassLong32(hwnd, index));
        }

        public static string WindowText(IntPtr hwnd)
        {
            var sb = new StringBuilder(GetWindowTextLength(hwnd) + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        // Fenêtres principales visibles (comme Alt+Tab).
        public static List<IntPtr> TopWindows()
        {
            var res = new List<IntPtr>();
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h) || GetWindowTextLength(h) == 0 || GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return true;
                int ex = GetWindowLong(h, -20 /*GWL_EXSTYLE*/);
                if ((ex & 0x80) != 0 && (ex & 0x40000) == 0) return true; // TOOLWINDOW sans APPWINDOW
                int cloaked;
                if (DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out cloaked, 4) == 0 && cloaked != 0) return true;
                res.Add(h);
                return true;
            }, IntPtr.Zero);
            return res;
        }

        public static void Activate(IntPtr hwnd)
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, 9 /*SW_RESTORE*/);
            SetForegroundWindow(hwnd);
        }

        public static string ProcessPath(IntPtr hwnd)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            IntPtr h = OpenProcess(0x1000 /*QUERY_LIMITED_INFORMATION*/, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        // Caractère produit par une touche dans la disposition clavier de la fenêtre active, sans perturber les touches mortes.
        public static string CharFor(KBDLLHOOKSTRUCT k, IntPtr fg)
        {
            uint pid;
            IntPtr hkl = GetKeyboardLayout(GetWindowThreadProcessId(fg, out pid));
            var state = new byte[256];
            if ((GetKeyState(0x10) & 0x8000) != 0) state[0x10] = 0x80;
            if ((GetKeyState(0x14) & 1) != 0) state[0x14] = 1;
            if ((GetKeyState(0x11) & 0x8000) != 0) state[0x11] = 0x80;
            if ((GetKeyState(0x12) & 0x8000) != 0) state[0x12] = 0x80;
            var sb = new StringBuilder(8);
            int n = ToUnicodeEx(k.vkCode, k.scanCode, state, sb, sb.Capacity, 4 /*ne pas modifier l'état*/, hkl);
            return n > 0 ? sb.ToString(0, n) : null;
        }
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mod, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
