// Configuration (lecture / écriture de HoldKeys.txt) et fenêtre « Paramètres ».
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace HoldKeys
{
    class Config
    {
        public int Delay = 250;
        public int AutoShow = 400;
        public bool Recent = true;
        public string SearchKey = "Win+Alt+Space";
        public List<KeyValuePair<string, string[]>> Entries = new List<KeyValuePair<string, string[]>>();

        public static Config Default()
        {
            return Parse(TrayContext.DefaultConfig.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
        }

        public static Config Load(string path)
        {
            if (!File.Exists(path)) Default().Save(path);
            return Parse(File.ReadAllLines(path, Encoding.UTF8));
        }

        public static Config Parse(IEnumerable<string> lines)
        {
            var c = new Config();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                int d;
                if (key.Equals("delay", StringComparison.OrdinalIgnoreCase))
                { if (int.TryParse(val, out d)) c.Delay = Math.Max(0, d); continue; }
                if (key.Equals("autoshow", StringComparison.OrdinalIgnoreCase))
                { if (int.TryParse(val, out d)) c.AutoShow = Math.Max(0, d); continue; }
                if (key.Equals("searchkey", StringComparison.OrdinalIgnoreCase))
                { c.SearchKey = val; continue; }
                if (key.Equals("recent", StringComparison.OrdinalIgnoreCase))
                { c.Recent = val != "0" && !val.Equals("false", StringComparison.OrdinalIgnoreCase); continue; }
                if (!IsValidKey(key)) continue;
                string[] items = val.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (items.Length == 0) continue;
                c.Entries.RemoveAll(e => e.Key == key); // une clé en double : la dernière gagne
                c.Entries.Add(new KeyValuePair<string, string[]>(key, items));
            }
            return c;
        }

        public static bool IsValidKey(string k)
        {
            if (k == null || k.Length != 1) return false;
            char c = k[0];
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }

        public void Save(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# HoldKeys — maintenez une lettre : le popup s'ouvre tout seul (ou appuyez sur Espace).");
            sb.AppendLine("# Espace / → = suivant, ← = précédent, 1-9 = choix direct. Échap = annuler.");
            sb.AppendLine("# Fichier géré par la fenêtre Paramètres (clic droit sur l'icône).");
            sb.AppendLine("# Format :  lettre = car1 car2 car3 ...   (minuscule = aussi pour les majuscules,");
            sb.AppendLine("# sauf si la majuscule est définie séparément, ex : E = É È Ê Ë). Touches : a-z, 0-9.");
            sb.AppendLine();
            sb.AppendLine("# Délai minimum (ms) pendant lequel la lettre doit être maintenue avant Espace");
            sb.AppendLine("delay = " + Delay);
            sb.AppendLine();
            sb.AppendLine("# Ouverture automatique du popup après X ms de maintien (0 = seulement avec Espace)");
            sb.AppendLine("autoshow = " + AutoShow);
            sb.AppendLine();
            sb.AppendLine("# Afficher en premier les derniers caractères utilisés (1 = oui, 0 = non)");
            sb.AppendLine("recent = " + (Recent ? 1 : 0));
            sb.AppendLine();
            sb.AppendLine("# Raccourci de la recherche de symboles par nom (vide = désactivé)");
            sb.AppendLine("searchkey = " + SearchKey);
            sb.AppendLine();
            foreach (var e in Entries) sb.AppendLine(e.Key + " = " + string.Join(" ", e.Value));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        public Dictionary<string, string[]> ToMap()
        {
            var m = new Dictionary<string, string[]>();
            foreach (var e in Entries) m[e.Key] = e.Value;
            return m;
        }
    }

    class SettingsForm : Form
    {
        readonly string path;
        readonly Action onSaved, onClearRecent;
        NumericUpDown numAuto, numDelay;
        CheckBox chkStart, chkRecent;
        TextBox txtSearchKey;
        DataGridView grid;

        public SettingsForm(string path, Action onSaved, Action onClearRecent)
        {
            this.path = path;
            this.onSaved = onSaved;
            this.onClearRecent = onClearRecent;

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "HoldKeys — Paramètres";
            Icon = TrayContext.AppIcon;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(600, 620);
            MinimumSize = new Size(500, 480);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            root.Controls.Add(BuildBehavior(), 0, 0);
            root.Controls.Add(BuildChars(), 0, 1);
            root.Controls.Add(BuildTest(), 0, 2);
            root.Controls.Add(BuildButtons(), 0, 3);

            Fill(Config.Load(path));
            chkStart.Checked = TrayContext.IsAutoStart();
        }

        Control BuildBehavior()
        {
            var gb = new GroupBox { Text = "Comportement", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 6, 10, 8) };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            numAuto = Num();
            numDelay = Num();
            chkStart = new CheckBox { Text = "Démarrer HoldKeys avec Windows", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };

            t.Controls.Add(Lbl("Ouvrir le popup automatiquement après"), 0, 0);
            t.Controls.Add(numAuto, 1, 0);
            t.Controls.Add(Hint("ms  (0 = Espace seulement)"), 2, 0);
            t.Controls.Add(Lbl("Accepter Espace / flèches après"), 0, 1);
            t.Controls.Add(numDelay, 1, 1);
            t.Controls.Add(Hint("ms  (frappe rapide)"), 2, 1);
            chkRecent = new CheckBox { Text = "Afficher en premier les derniers caractères utilisés", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };
            var clear = new LinkLabel { Text = "Effacer l'historique", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 9, 3, 3) };
            clear.LinkClicked += delegate
            {
                onClearRecent();
                MessageBox.Show(this, "Historique effacé : l'ordre de la liste est rétabli.", "HoldKeys", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            var recentRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
            recentRow.Controls.Add(chkRecent);
            recentRow.Controls.Add(clear);
            txtSearchKey = new TextBox { ReadOnly = true, Width = 130,Anchor = AnchorStyles.Left, BackColor = SystemColors.Window, Cursor = Cursors.Hand };
            txtSearchKey.KeyDown += CaptureHotkey;
            t.Controls.Add(Lbl("Recherche de symboles par nom"), 0, 2);
            t.Controls.Add(txtSearchKey, 1, 2);
            t.Controls.Add(Hint("(Retour arrière = aucun)"), 2, 2);
            new ToolTip().SetToolTip(txtSearchKey, "Cliquez ici puis tapez la combinaison voulue (Ctrl, Alt ou Win + une touche).");
            t.Controls.Add(recentRow, 0, 3);
            t.SetColumnSpan(recentRow, 3);
            chkStart.Margin = new Padding(3, 4, 3, 3);
            t.Controls.Add(chkStart, 0, 4);
            t.SetColumnSpan(chkStart, 3);
            gb.Controls.Add(t);
            return gb;
        }

        Control BuildChars()
        {
            var gb = new GroupBox { Text = "Caractères par touche", Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 8) };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var help = Hint("Touche : a-z ou 0-9. Une minuscule vaut aussi pour la majuscule, sauf si vous ajoutez celle-ci (ex. E). Caractères séparés par des espaces, dans l'ordre d'affichage. Double-clic pour modifier.");
            help.AutoSize = false;
            help.Dock = DockStyle.Fill;
            help.Height = (int)(36 * DeviceScale());
            t.Controls.Add(help, 0, 0);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                Margin = new Padding(3, 6, 3, 6)
            };
            var colKey = new DataGridViewTextBoxColumn { HeaderText = "Touche", FillWeight = 14, MaxInputLength = 1 };
            colKey.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            colKey.DefaultCellStyle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            var colChars = new DataGridViewTextBoxColumn { HeaderText = "Caractères", FillWeight = 86 };
            colChars.DefaultCellStyle.Font = new Font("Segoe UI", 12F);
            grid.Columns.Add(colKey);
            grid.Columns.Add(colChars);
            grid.RowTemplate.Height = (int)(30 * DeviceScale());
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) grid.BeginEdit(true); };
            grid.KeyDown +=(s, e) => { if (e.KeyCode == Keys.Delete && !grid.IsCurrentCellInEditMode) { DeleteRow(); e.Handled = true; } };
            t.Controls.Add(grid, 0, 1);

            var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            var del = new Button { Text = "Supprimer la ligne", AutoSize = true };
            del.Click += delegate { DeleteRow(); };
            var reset = new Button { Text = "Rétablir les valeurs par défaut", AutoSize = true };
            reset.Click += delegate
            {
                if (MessageBox.Show(this, "Remplacer toute la liste par les valeurs par défaut ?", "HoldKeys",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    Fill(Config.Default());
            };
            bar.Controls.Add(del);
            bar.Controls.Add(reset);
            t.Controls.Add(bar, 0, 2);

            gb.Controls.Add(t);
            return gb;
        }

        Control BuildTest()
        {
            var gb = new GroupBox { Text = "Essai (après enregistrement, maintenez une lettre ici)", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 6, 10, 8) };
            var box = new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 12F) };
            gb.Controls.Add(box);
            return gb;
        }

        Control BuildButtons()
        {
            var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            var cancel = new Button { Text = "Fermer", AutoSize = true, MinimumSize = new Size(90, 0), DialogResult = DialogResult.Cancel };
            var save = new Button { Text = "Enregistrer", AutoSize = true, MinimumSize = new Size(90, 0) };
            save.Click += delegate { Save(); };
            cancel.Click += delegate { Close(); };
            bar.Controls.Add(cancel);
            bar.Controls.Add(save);
            CancelButton = cancel;
            return bar;
        }

        void Fill(Config c)
        {
            numAuto.Value = Math.Min(numAuto.Maximum, c.AutoShow);
            numDelay.Value = Math.Min(numDelay.Maximum, c.Delay);
            chkRecent.Checked = c.Recent;
            txtSearchKey.Text = c.SearchKey;
            grid.Rows.Clear();
            foreach (var e in c.Entries) grid.Rows.Add(e.Key, string.Join(" ", e.Value));
        }

        void DeleteRow()
        {
            var cell = grid.CurrentCell;
            if (cell != null && !grid.Rows[cell.RowIndex].IsNewRow) grid.Rows.RemoveAt(cell.RowIndex);
        }

        void Save()
        {
            grid.EndEdit();
            var c = new Config { AutoShow = (int)numAuto.Value, Delay = (int)numDelay.Value, Recent = chkRecent.Checked, SearchKey = txtSearchKey.Text.Trim() };
            var seen = new HashSet<string>();
            foreach (DataGridViewRow r in grid.Rows)
            {
                if (r.IsNewRow) continue;
                string key = Convert.ToString(r.Cells[0].Value).Trim();
                string val = Convert.ToString(r.Cells[1].Value).Trim();
                if (key.Length == 0 && val.Length == 0) continue;
                string err = null;
                if (!Config.IsValidKey(key)) err = "La touche « " + key + " » n'est pas valide (a-z ou 0-9).";
                else if (!seen.Add(key)) err = "La touche « " + key + " » apparaît deux fois.";
                else if (val.Length == 0) err = "Aucun caractère pour la touche « " + key + " ».";
                if (err != null)
                {
                    grid.CurrentCell = r.Cells[0];
                    MessageBox.Show(this, err, "HoldKeys", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                c.Entries.Add(new KeyValuePair<string, string[]>(key,
                    val.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)));
            }
            try
            {
                c.Save(path);
                TrayContext.SetAutoStart(chkStart.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Enregistrement impossible : " + ex.Message, "HoldKeys", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            onSaved();
            Text = "HoldKeys — Paramètres (enregistré)";
        }

        void CaptureHotkey(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            Keys k = e.KeyCode;
            bool win = (Native.GetKeyState(0x5B) & 0x8000) != 0 || (Native.GetKeyState(0x5C) & 0x8000) != 0;
            if ((k == Keys.Back || k == Keys.Delete) && !e.Control && !e.Alt && !e.Shift && !win) { txtSearchKey.Text = ""; return; }
            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin) return;
            if (!e.Control && !e.Alt && !win) return; // au moins Ctrl, Alt ou Win
            txtSearchKey.Text = Hotkey.Format(k, e.Control, e.Alt, e.Shift, win);
        }

        float DeviceScale() { using (var g = CreateGraphics()) return g.DpiX / 96f; }

        static NumericUpDown Num()
        {
            return new NumericUpDown { Minimum = 0, Maximum = 3000, Increment = 50, Width = 70, Anchor = AnchorStyles.Left };
        }
        static Label Lbl(string s) { return new Label { Text = s, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) }; }
        static Label Hint(string s) { return new Label { Text = s, AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) }; }
    }
}
