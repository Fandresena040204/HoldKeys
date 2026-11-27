// Lanceur universel (Win+Alt+Espace) : fenêtre, modèle de résultats, modules, icônes et usage.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HoldKeys
{
    class Result
    {
        public string Title, Subtitle, Glyph;
        public string IconPath;          // chemin ou nom shell (shell:AppsFolder\...) pour l'icône
        public IntPtr IconHwnd;          // fenêtre dont on affiche l'icône
        public int Score;
        public string Key;               // clé d'usage (null = non mémorisé)

        public string EnterLabel = "Ouvrir";
        public Action Run;
        public bool NeedsTarget;         // à exécuter dans la fenêtre d'origine (insertion de texte…)
        public bool KeepOpen;
        public bool Confirm;             // demande un 2e Entrée

        public string AltLabel;
        public Action AltRun;
        public bool AltNeedsTarget, AltKeepOpen;

        public Action DeleteRun;         // Maj+Suppr
        public string FillQuery;         // Entrée remplit la barre au lieu d'exécuter
        public string TabFill;           // Tab complète la barre
    }

    abstract class Provider
    {
        public virtual string Prefix { get { return null; } }
        public virtual bool Global { get { return true; } }
        public virtual bool IsAsync { get { return false; } }
        public virtual string HelpTitle { get { return null; } }
        public virtual string HelpText { get { return null; } }
        public virtual string HelpFill { get { return Prefix ?? ""; } }
        public abstract List<Result> Query(string q, bool prefixed);
        public virtual Result Resolve(string key) { return null; }
        public virtual void OnOpen() { }
    }

    // ---------- Correspondance floue ----------

    static class Matcher
    {
        static readonly char[] Sep = { ' ', '-', '_', '.', '(', ')', '\\', '/', ',', '\'', ':' };

        public static int Score(string query, string text)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text)) return 0;
            string q = SymbolDb.Normalize(query).Trim();
            string t = SymbolDb.Normalize(text);
            if (q.Length == 0) return 0;
            if (t == q) return 100;
            int shortBonus = 10 * q.Length / Math.Max(q.Length, t.Length); // à égalité, les noms courts d'abord
            if (t.StartsWith(q)) return 85 + shortBonus;
            string[] words = t.Split(Sep, StringSplitOptions.RemoveEmptyEntries);
            string[] qw = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool all = true;
            foreach (string w in qw)
            {
                string w2 = w;
                if (!Array.Exists(words, x => x.StartsWith(w2))) { all = false; break; }
            }
            if (all) return 70 + shortBonus;
            if (q.Length >= 2 && words.Length >= 2)
            {
                var ini = new StringBuilder();
                foreach (string w in words) ini.Append(w[0]);
                if (ini.ToString().StartsWith(q.Replace(" ", ""))) return 60;
            }
            if (t.Contains(q)) return 50;
            if (q.Length >= 3)
            {
                int j = 0;
                foreach (char c in t) if (j < q.Length && c == q[j]) j++;
                if (j == q.Length) return 20;
            }
            return 0;
        }
    }

    // ---------- Usage (les plus utilisés remontent) ----------

    static class Usage
    {
        static readonly Dictionary<string, long[]> data = new Dictionary<string, long[]>(); // [nombre, ticks]
        static string path;

        public static void Load(string dir)
        {
            path = Path.Combine(dir, "HoldKeys.usage.txt");
            data.Clear();
            try
            {
                if (!File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string[] p = line.Split('\t');
                    long c, t;
                    if (p.Length == 3 && long.TryParse(p[1], out c) && long.TryParse(p[2], out t)) data[p[0]] = new[] { c, t };
                }
            }
            catch { }
        }

        public static void Record(string key)
        {
            if (key == null || path == null) return;
            long[] v;
            if (!data.TryGetValue(key, out v)) data[key] = v = new long[2];
            v[0]++;
            v[1] = DateTime.Now.Ticks;
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in data) sb.Append(kv.Key).Append('\t').Append(kv.Value[0]).Append('\t').Append(kv.Value[1]).AppendLine();
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        public static int Boost(string key)
        {
            long[] v;
            if (key == null || !data.TryGetValue(key, out v)) return 0;
            int b = (int)Math.Min(v[0], 10) * 4;
            if (DateTime.Now.Ticks - v[1] < TimeSpan.TicksPerDay) b += 8;
            return b;
        }

        public static List<string> Top(int n)
        {
            var keys = new List<string>(data.Keys);
            keys.Sort((a, b) =>
            {
                long[] x = data[a], y = data[b];
                int c = y[0].CompareTo(x[0]);
                return c != 0 ? c : y[1].CompareTo(x[1]);
            });
            return keys.Count > n ? keys.GetRange(0, n) : keys;
        }
    }

    // ---------- Icônes (applications, fichiers, fenêtres) ----------

    static class IconCache
    {
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }

        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [In] ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        public static Image Get(Result r, int size)
        {
            string key = r.IconHwnd != IntPtr.Zero ? "hwnd:" + r.IconHwnd.ToInt64() : r.IconPath;
            if (key == null) return null;
            Image img;
            if (cache.TryGetValue(key, out img)) return img;
            img = r.IconHwnd != IntPtr.Zero ? WindowIcon(r.IconHwnd, size) : ShellIcon(r.IconPath, size);
            if (cache.Count > 500) cache.Clear();
            cache[key] = img;
            return img;
        }

        public static Image ShellIcon(string path, int size)
        {
            try
            {
                Guid iid = typeof(IShellItemImageFactory).GUID;
                IShellItemImageFactory f;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out f);
                IntPtr hbmp;
                int hr = f.GetImage(new SIZE { cx = size, cy = size }, 0x4 /*ICONONLY*/, out hbmp);
                Marshal.ReleaseComObject(f);
                if (hr != 0 || hbmp == IntPtr.Zero) return null;
                try { return WithAlpha(hbmp); }
                finally { DeleteObject(hbmp); }
            }
            catch { return null; }
        }

        // Image.FromHbitmap perd la transparence : on relit les pixels en ARGB prémultiplié.
        static Bitmap WithAlpha(IntPtr hbmp)
        {
            using (var bmp = Image.FromHbitmap(hbmp))
            {
                if (Image.GetPixelFormatSize(bmp.PixelFormat) < 32) return new Bitmap(bmp);
                var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, bmp.PixelFormat);
                try
                {
                    using (var tmp = new Bitmap(data.Width, data.Height, data.Stride, PixelFormat.Format32bppPArgb, data.Scan0))
                        return new Bitmap(tmp);
                }
                finally { bmp.UnlockBits(data); }
            }
        }

        static Image WindowIcon(IntPtr hwnd, int size)
        {
            IntPtr h;
            Native.SendMessageTimeout(hwnd, 0x7F /*WM_GETICON*/, (IntPtr)1, IntPtr.Zero, 2, 100, out h);
            if (h == IntPtr.Zero) Native.SendMessageTimeout(hwnd, 0x7F, (IntPtr)2, IntPtr.Zero, 2, 100, out h);
            if (h == IntPtr.Zero) h = Native.GetClassLongPtrSafe(hwnd, -14 /*GCLP_HICON*/);
            if (h != IntPtr.Zero)
            {
                try { using (var ic = Icon.FromHandle(h)) return ic.ToBitmap(); } catch { }
            }
            string exe = Native.ProcessPath(hwnd);
            return exe != null ? ShellIcon(exe, size) : null;
        }
    }

    // ---------- Fenêtre du lanceur ----------

    class LauncherForm : Form
    {
        static readonly Color Bg = Color.FromArgb(32, 32, 32);
        static readonly Color Field = Color.FromArgb(48, 48, 48);
        static readonly Color Accent = Color.FromArgb(0, 120, 215);
        static readonly Color Dim = Color.FromArgb(150, 150, 150);

        readonly List<Provider> providers;
        readonly TextBox box;
        readonly ListBox list;
        readonly Label hint;
        readonly float scale;
        readonly Font glyphFont, titleFont, subFont;
        readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer();
        List<Result> results = new List<Result>();
        IntPtr previous;
        bool choosing;
        int version;
        Result armed;

        public LauncherForm(List<Provider> providers)
        {
            this.providers = providers;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            BackColor = Bg;
            Text = "HoldKeys — Lanceur";
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            glyphFont = new Font("Segoe UI Symbol", 20 * scale, GraphicsUnit.Pixel);
            titleFont = new Font("Segoe UI", 15 * scale, GraphicsUnit.Pixel);
            subFont = new Font("Segoe UI", 11.5f * scale, GraphicsUnit.Pixel);

            Size = new Size(S(660), S(500));
            Padding = new Padding(S(12));

            box = new TextBox
            {
                Dock = DockStyle.Top,
                BorderStyle = BorderStyle.None,
                BackColor = Field,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 18 * scale, GraphicsUnit.Pixel)
            };
            var boxPanel = new Panel { Dock = DockStyle.Top, Height = box.PreferredHeight + S(18), BackColor = Field, Padding = new Padding(S(12), S(9), S(12), S(9)) };
            boxPanel.Controls.Add(box);

            hint = new Label { Dock = DockStyle.Bottom, Height = S(24), ForeColor = Dim, Font = subFont, TextAlign = ContentAlignment.BottomLeft };

            list = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Bg,
                ForeColor = Color.White,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = S(52),
                IntegralHeight = false
            };
            list.DrawItem += DrawItem;
            list.SelectedIndexChanged += delegate { armed = null; UpdateHint(); };
            list.MouseClick += (s, e) => { int i = list.IndexFromPoint(e.Location); if (i >= 0) { list.SelectedIndex = i; Execute(false); } };
            list.HandleCreated += delegate { Native.SetWindowTheme(list.Handle, "DarkMode_Explorer", null); };

            Controls.Add(list);
            Controls.Add(hint);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = S(8), BackColor = Bg });
            Controls.Add(boxPanel);

            box.TextChanged += delegate { RefreshResults(); };
            debounce.Interval = 180;
            debounce.Tick += delegate { debounce.Stop(); RunAsync(); };
            Deactivate += delegate { if (!choosing) Hide(); };
            Native.SendMessage(box.Handle, 0x1501 /*EM_SETCUEBANNER*/, (IntPtr)1, "Rechercher une appli, un fichier, un calcul, un symbole…");
        }

        int S(float v) { return (int)(v * scale); }

        public void Open()
        {
            previous = Native.GetForegroundWindow();
            if (previous == Handle) previous = IntPtr.Zero;
            foreach (var p in providers) p.OnOpen();
            Screen sc = previous != IntPtr.Zero ? Screen.FromHandle(previous) : Screen.FromPoint(Cursor.Position);
            Rectangle wa = sc.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (int)(wa.Height * 0.16));
            using (var p = Popup.RoundRect(new RectangleF(0, 0, Width, Height), 12 * scale)) Region = new Region(p);
            if (box.Text.Length > 0) box.Text = ""; else RefreshResults();
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
            box.Focus();
        }

        public void SetQuery(string q)
        {
            box.Text = q;
            box.SelectionStart = box.Text.Length;
        }

        // ----- Recherche -----

        Provider PrefixProvider(string text, out string rest)
        {
            Provider best = null;
            rest = text;
            foreach (var p in providers)
            {
                string pre = p.Prefix;
                if (pre == null || !text.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) continue;
                if (best == null || pre.Length > best.Prefix.Length) best = p;
            }
            if (best != null) rest = text.Substring(best.Prefix.Length).Trim();
            return best;
        }

        void RefreshResults()
        {
            version++;
            armed = null;
            string text = box.Text;
            var res = new List<Result>();
            if (text.Trim().Length == 0)
                res = HomeResults();
            else
            {
                string rest;
                Provider pp = PrefixProvider(text, out rest);
                foreach (var p in providers)
                {
                    if (p.IsAsync) continue;
                    if (pp != null && p != pp) continue;
                    if (pp == null && !p.Global) continue;
                    try { res.AddRange(p.Query(pp != null ? rest : text.Trim(), pp != null)); } catch { }
                }
                Rank(res);
            }
            Show(res, null);
            debounce.Stop();
            if (text.Trim().Length > 0) debounce.Start();
        }

        void RunAsync()
        {
            string text = box.Text;
            string rest;
            Provider pp = PrefixProvider(text, out rest);
            int v = version;
            foreach (var p in providers)
            {
                if (!p.IsAsync) continue;
                if (pp != null && p != pp) continue;
                if (pp == null && !p.Global) continue;
                Provider prov = p;
                string q = pp != null ? rest : text.Trim();
                bool prefixed = pp != null;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    List<Result> r;
                    try { r = prov.Query(q, prefixed); } catch { r = new List<Result>(); }
                    if (r.Count == 0 || IsDisposed) return;
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            if (v != version) return;
                            Result sel = Selected;
                            foreach (var x in r) x.Score += Usage.Boost(x.Key);
                            var merged = new List<Result>(results);
                            merged.AddRange(r);
                            Sort(merged);
                            Show(merged, sel);
                        }));
                    }
                    catch { }
                });
            }
        }

        static void Rank(List<Result> res)
        {
            foreach (var r in res) r.Score += Usage.Boost(r.Key);
            // Un outil Windows déjà présent comme application (ex. Gestionnaire des tâches) n'apparaît qu'une fois.
            var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in res) if (r.Key != null && r.Key.StartsWith("app:")) apps.Add(r.Title);
            res.RemoveAll(r => r.Key != null && r.Key.StartsWith("set:") && apps.Contains(r.Title));
            Sort(res);
        }

        static void Sort(List<Result> res)
        {
            var idx = new Dictionary<Result, int>();
            for (int i = 0; i < res.Count; i++) idx[res[i]] = i;
            res.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : idx[a].CompareTo(idx[b]));
            if (res.Count > 60) res.RemoveRange(60, res.Count - 60);
        }

        List<Result> HomeResults()
        {
            var res = new List<Result>();
            foreach (string key in Usage.Top(12))
            {
                foreach (var p in providers)
                {
                    Result r = null;
                    try { r = p.Resolve(key); } catch { }
                    if (r != null) { r.Subtitle = "Récent · " + r.Subtitle; res.Add(r); break; }
                }
                if (res.Count >= 6) break;
            }
            foreach (var p in providers)
            {
                if (p.HelpTitle == null) continue;
                string glyph = p.Prefix != null ? p.Prefix.Trim() : "★";
                string title = p.HelpTitle.StartsWith(glyph) ? p.HelpTitle.Substring(glyph.Length).Trim() : p.HelpTitle;
                res.Add(new Result
                {
                    Title = title,
                    Subtitle = p.HelpText,
                    Glyph = glyph,
                    FillQuery = p.HelpFill,
                    EnterLabel = "Utiliser"
                });
            }
            return res;
        }

        void Show(List<Result> res, Result keep)
        {
            results = res;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var r in res) list.Items.Add(r.Title ?? "");
            list.EndUpdate();
            int i = keep != null ? res.IndexOf(keep) : -1;
            if (list.Items.Count > 0) list.SelectedIndex = i >= 0 ? i : 0;
            UpdateHint();
        }

        Result Selected
        {
            get
            {
                int i = list.SelectedIndex;
                return i >= 0 && i < results.Count ? results[i] : null;
            }
        }

        void UpdateHint()
        {
            var r = Selected;
            if (r == null) { hint.Text = "Échap : fermer"; return; }
            var sb = new StringBuilder("Entrée : " + (r.FillQuery != null ? "utiliser" : r.EnterLabel.ToLowerInvariant()));
            if (r.AltRun != null) sb.Append("   ·   Ctrl+Entrée : " + r.AltLabel.ToLowerInvariant());
            if (r.TabFill != null) sb.Append("   ·   Tab : compléter");
            if (r.DeleteRun != null) sb.Append("   ·   Maj+Suppr : supprimer");
            sb.Append("   ·   Échap");
            hint.Text = sb.ToString();
        }

        // ----- Exécution -----

        void Execute(bool alt)
        {
            var r = Selected;
            if (r == null) return;
            if (!alt && r.FillQuery != null) { SetQuery(r.FillQuery); return; }
            Action act = alt ? r.AltRun : r.Run;
            if (act == null) return;
            if (!alt && r.Confirm && armed != r) { armed = r; list.Invalidate(); return; }
            armed = null;
            Usage.Record(r.Key);
            bool keep = alt ? r.AltKeepOpen : r.KeepOpen;
            bool target = alt ? r.AltNeedsTarget : r.NeedsTarget;
            if (keep)
            {
                Safe(act);
                RefreshResults();
                return;
            }
            choosing = true;
            Hide();
            choosing = false;
            if (target)
            {
                RestoreFocus();
                Delay(80, () => Safe(act));
            }
            else Safe(act);
        }

        static void Safe(Action a)
        {
            try { a(); }
            catch (System.ComponentModel.Win32Exception ex) { if (ex.NativeErrorCode != 1223) TrayContext.Notify(ex.Message); }
            catch (Exception ex) { TrayContext.Notify(ex.Message); }
        }

        public static void Delay(int ms, Action a)
        {
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += delegate { t.Stop(); t.Dispose(); a(); };
            t.Start();
        }

        void RestoreFocus()
        {
            if (previous != IntPtr.Zero) Native.SetForegroundWindow(previous);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Tab)
            {
                var r = Selected;
                if (r != null && (r.TabFill != null || r.FillQuery != null)) SetQuery(r.TabFill ?? r.FillQuery);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int n = list.Items.Count;
            switch (e.KeyCode)
            {
                case Keys.Escape:
                    if (box.Text.Length > 0) box.Text = "";
                    else { Hide(); RestoreFocus(); }
                    break;
                case Keys.Enter: Execute(e.Control); break;
                case Keys.Down: if (n > 0) list.SelectedIndex = (list.SelectedIndex + 1) % n; break;
                case Keys.Up: if (n > 0) list.SelectedIndex = (list.SelectedIndex - 1 + n) % n; break;
                case Keys.PageDown: if (n > 0) list.SelectedIndex = Math.Min(n - 1, list.SelectedIndex + 7); break;
                case Keys.PageUp: if (n > 0) list.SelectedIndex = Math.Max(0, list.SelectedIndex - 7); break;
                case Keys.Delete:
                    if (!e.Shift || Selected == null || Selected.DeleteRun == null) return;
                    Safe(Selected.DeleteRun);
                    RefreshResults();
                    break;
                default: return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        // ----- Dessin -----

        void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= results.Count) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(Bg)) g.FillRectangle(b, e.Bounds);
            var r = Rectangle.Inflate(e.Bounds, 0, -S(2));
            if (selected)
                using (var b = new SolidBrush(Accent))
                using (var p = Popup.RoundRect(r, 7 * scale)) g.FillPath(b, p);

            var res = results[e.Index];
            var iconRect = new Rectangle(r.X + S(8), r.Y + (r.Height - S(36)) / 2, S(36), S(36));
            Image img = null;
            try { img = IconCache.Get(res, S(32)); } catch { }
            if (img != null)
            {
                int sz = S(32);
                g.DrawImage(img, new Rectangle(iconRect.X + (iconRect.Width - sz) / 2, iconRect.Y + (iconRect.Height - sz) / 2, sz, sz));
            }
            else
            {
                string glyph = res.Glyph ?? (string.IsNullOrEmpty(res.Title) ? "" : res.Title.Substring(0, 1).ToUpperInvariant());
                if (res.Glyph == null)
                    using (var b = new SolidBrush(selected ? Color.FromArgb(40, 255, 255, 255) : Field))
                    using (var p = Popup.RoundRect(iconRect, 6 * scale)) g.FillPath(b, p);
                TextRenderer.DrawText(g, glyph, res.Glyph != null ? glyphFont : titleFont, iconRect, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }

            int x = iconRect.Right + S(12);
            int labelW = selected ? S(120) : 0;
            var titleRect = new Rectangle(x, r.Y + S(5), r.Right - x - S(10) - labelW, r.Height / 2);
            var subRect = new Rectangle(x, r.Y + r.Height / 2 + S(1), r.Right - x - S(10) - labelW, r.Height / 2 - S(4));
            TextRenderer.DrawText(g, res.Title, titleFont, titleRect, Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            string sub = res == armed ? "⚠ Appuyez à nouveau sur Entrée pour confirmer" : res.Subtitle;
            Color subColor = res == armed ? Color.FromArgb(255, 210, 120) : selected ? Color.FromArgb(215, 230, 250) : Dim;
            TextRenderer.DrawText(g, sub ?? "", subFont, subRect, subColor,
                TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (selected)
            {
                var lr = new Rectangle(r.Right - labelW - S(10), r.Y, labelW, r.Height);
                TextRenderer.DrawText(g, (res.FillQuery != null ? "Utiliser" : res.EnterLabel) + " ↵", subFont, lr, Color.FromArgb(215, 230, 250),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // TOOLWINDOW : pas dans Alt+Tab
                return cp;
            }
        }
    }
}
