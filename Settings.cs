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
        public bool Clipboard = true;
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
                if (key.Equals("clipboard", StringComparison.OrdinalIgnoreCase))
                { c.Clipboard = val != "0" && !val.Equals("false", StringComparison.OrdinalIgnoreCase); continue; }
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
            sb.AppendLine("# Raccourci du lanceur (vide = désactivé)");
            sb.AppendLine("searchkey = " + SearchKey);
            sb.AppendLine();
            sb.AppendLine("# Historique du presse-papiers (1 = oui, 0 = non)");
            sb.AppendLine("clipboard = " + (Clipboard ? 1 : 0));
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

            ClientSize = new Size(640, 640);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 5) };
            tabs.TabPages.Add(Page("Général", BuildGeneral()));
            tabs.TabPages.Add(Page("Caractères", BuildChars()));
            tabs.TabPages.Add(Page("Texte rapide", BuildSnippets()));
            tabs.TabPages.Add(Page("Lanceur", BuildLauncherHelp()));
            root.Controls.Add(tabs, 0, 0);
            root.Controls.Add(BuildTest(), 0, 1);
            root.Controls.Add(BuildButtons(), 0, 2);

            Fill(Config.Load(path));
            chkStart.Checked = TrayContext.IsAutoStart();
            FillSnippets();
        }

        TabControl tabs;
        CheckBox chkClipboard;
        DataGridView snipGrid;

        public void ShowTab(int i) { if (i >= 0 && i < tabs.TabCount) tabs.SelectedIndex = i; }

        static TabPage Page(string title, Control content)
        {
            var p = new TabPage(title) { Padding = new Padding(8), UseVisualStyleBackColor = true };
            content.Dock = DockStyle.Fill;
            p.Controls.Add(content);
            return p;
        }

        Control BuildGeneral()
        {
            var t = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, AutoScroll = true };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.Controls.Add(BuildBehavior(), 0, 0);

            var gb = new GroupBox { Text = "Presse-papiers", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 6, 10, 8), Margin = new Padding(3, 10, 3, 3) };
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            chkClipboard = new CheckBox { Text = "Mémoriser l'historique du texte copié (lanceur : v + espace)", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            var clear = new LinkLabel { Text = "Effacer", AutoSize = true, Margin = new Padding(12, 7, 3, 3) };
            clear.LinkClicked += delegate
            {
                ClipboardHistory.Clear();
                MessageBox.Show(this, "Historique du presse-papiers effacé (les éléments épinglés sont conservés).", "HoldKeys", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            row.Controls.Add(chkClipboard);
            row.Controls.Add(clear);
            var note = Hint("Stocké chiffré avec votre compte Windows. Les mots de passe copiés depuis un gestionnaire de mots de passe sont ignorés.");
            note.MaximumSize = new Size(560, 0);
            var inner = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1 };
            inner.Controls.Add(row);
            inner.Controls.Add(note);
            gb.Controls.Add(inner);
            t.Controls.Add(gb, 0, 1);
            return t;
        }

        Control BuildSnippets()
        {
            var t = new TableLayoutPanel { ColumnCount = 1, RowCount = 3 };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var help = Hint("Tapez une abréviation n'importe où : elle est remplacée par le texte. Commencez-les par ; pour éviter les déclenchements involontaires.\nDans le texte : \\n = retour à la ligne, {date}, {heure}, {presse-papiers}.");
            help.MaximumSize = new Size(580, 0);
            t.Controls.Add(help, 0, 0);

            snipGrid = new DataGridView
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
            snipGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Abréviation", FillWeight = 25 });
            snipGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Texte", FillWeight = 75 });
            snipGrid.Columns[0].DefaultCellStyle.Font = new Font("Consolas", 10F);
            snipGrid.RowTemplate.Height = (int)(26 * DeviceScale());
            snipGrid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) snipGrid.BeginEdit(true); };
            t.Controls.Add(snipGrid, 0, 1);

            var del = new Button { Text = "Supprimer la ligne", AutoSize = true };
            del.Click += delegate
            {
                var c = snipGrid.CurrentCell;
                if (c != null && !snipGrid.Rows[c.RowIndex].IsNewRow) snipGrid.Rows.RemoveAt(c.RowIndex);
            };
            var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            bar.Controls.Add(del);
            t.Controls.Add(bar, 0, 2);
            return t;
        }

        Control BuildLauncherHelp()
        {
            var t = new TableLayoutPanel { ColumnCount = 2, AutoScroll = true, Padding = new Padding(4) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var bold = new Font("Consolas", 10F, FontStyle.Bold);
            Action<string, string> row = (k, v) =>
            {
                var kl = new Label { Text = k, AutoSize = true, Font = bold, ForeColor = Color.FromArgb(0, 90, 170), Margin = new Padding(3, 5, 14, 3) };
                var vl = new Label { Text = v, AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(3, 5, 3, 3) };
                t.Controls.Add(kl);
                t.Controls.Add(vl);
            };
            var intro = new Label { Text = "Ouvrez le lanceur avec son raccourci (onglet Général, Win+Alt+Espace par défaut), puis tapez :", AutoSize = true, MaximumSize = new Size(580, 0), Margin = new Padding(3, 3, 3, 8) };
            t.Controls.Add(intro);
            t.SetColumnSpan(intro, 2);
            row("(rien)", "applications, fenêtres, calculs, paramètres Windows, commandes système, fichiers, symboles…");
            row("=", "calculatrice et conversions : 2*(3+4), sqrt(2), 15%, 10 km en mi, 30 c en f, 255 en hex");
            row("<", "fenêtres ouvertes (Ctrl+Entrée : fermer)");
            row("?", "fichiers et dossiers de l'index Windows (Ctrl+Entrée : ouvrir l'emplacement)");
            row(">", "commande dans une invite (Ctrl+Entrée : en administrateur)");
            row("!", "recherche web : !g, !yt, !w, !tr, !maps, !gh, !img, !ddg");
            row(":", "symboles et emoji : :flèche, :euro, :e aigu");
            row(";", "texte rapide (liste et insertion)");
            row("v ", "historique du presse-papiers (Ctrl+Entrée : épingler, Maj+Suppr : effacer)");
            row("%", "actions sur le texte sélectionné : majuscules, sans accents, trier, traduire, compter…");
            row("C:\\ ~ %", "parcourir les dossiers (Tab : compléter)");
            row("mdp guid…", "générateurs : mdp 20, guid, date, heure, maintenant, timestamp, sha256 texte, lorem 30, nombre 1 100");
            row("verrouiller…", "commandes : verrouiller, veille, redémarrer, éteindre, vider la corbeille, éteindre l'écran");
            var keys = new Label { Text = "Entrée : action principale · Ctrl+Entrée : action secondaire · Tab : compléter · Échap : effacer puis fermer. Les éléments que vous utilisez le plus remontent en tête ; l'accueil affiche vos récents.", AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(580, 0), Margin = new Padding(3, 12, 3, 3) };
            t.Controls.Add(keys);
            t.SetColumnSpan(keys, 2);
            return t;
        }

        void FillSnippets()
        {
            snipGrid.Rows.Clear();
            foreach (var kv in SnippetStore.Items) snipGrid.Rows.Add(kv.Key, SnippetStore.Escape(kv.Value));
            snipGrid.Sort(snipGrid.Columns[0], System.ComponentModel.ListSortDirection.Ascending);
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
            t.Controls.Add(Lbl("Raccourci du lanceur"), 0, 2);
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
            chkClipboard.Checked = c.Clipboard;
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
            snipGrid.EndEdit();
            var snippets = new List<KeyValuePair<string, string>>();
            var seenSnip = new HashSet<string>();
            foreach (DataGridViewRow r in snipGrid.Rows)
            {
                if (r.IsNewRow) continue;
                string trig = Convert.ToString(r.Cells[0].Value).Trim();
                string text = Convert.ToString(r.Cells[1].Value);
                if (trig.Length == 0 && string.IsNullOrEmpty(text)) continue;
                string err = null;
                if (trig.Length < 2 || trig.Contains(" ")) err = "L'abréviation « " + trig + " » doit faire au moins 2 caractères, sans espace.";
                else if (!seenSnip.Add(trig)) err = "L'abréviation « " + trig + " » apparaît deux fois.";
                else if (string.IsNullOrEmpty(text)) err = "Aucun texte pour l'abréviation « " + trig + " ».";
                if (err != null)
                {
                    tabs.SelectedIndex = 2;
                    snipGrid.CurrentCell = r.Cells[0];
                    MessageBox.Show(this, err, "HoldKeys", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                snippets.Add(new KeyValuePair<string, string>(trig, SnippetStore.Unescape(text)));
            }

            var c = new Config { AutoShow = (int)numAuto.Value, Delay = (int)numDelay.Value, Recent = chkRecent.Checked, SearchKey = txtSearchKey.Text.Trim(), Clipboard = chkClipboard.Checked };
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
                SnippetStore.Save(snippets);
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
