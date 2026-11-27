// Modules du lanceur : applications, fenêtres, calcul, conversions, chemins, URL, fichiers, web, système, commandes.
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace HoldKeys
{
    static class Open
    {
        public static void Shell(string target, string args = null, bool admin = false)
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            if (args != null) psi.Arguments = args;
            if (admin) psi.Verb = "runas";
            Process.Start(psi);
        }

        public static void Location(string path)
        {
            Process.Start("explorer.exe", "/select,\"" + path + "\"");
        }
    }

    // ---------- Applications (menu Démarrer + Microsoft Store) ----------

    class AppsProvider : Provider
    {
        class App { public string Name, Path; }
        volatile List<App> apps = new List<App>();
        DateTime loaded = DateTime.MinValue;
        volatile bool loading;

        public AppsProvider() { OnOpen(); }

        public override void OnOpen()
        {
            if (loading || (DateTime.Now - loaded).TotalMinutes < 5) return;
            loading = true;
            var th = new Thread(Load) { IsBackground = true };
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
        }

        void Load()
        {
            try
            {
                var list = new List<App>();
                Type t = Type.GetTypeFromProgID("Shell.Application");
                dynamic sh = Activator.CreateInstance(t);
                dynamic folder = sh.NameSpace("shell:AppsFolder");
                foreach (dynamic it in folder.Items())
                {
                    string name = it.Name;
                    string path = it.Path;
                    if (!Skip(name, path)) list.Add(new App { Name = name, Path = path });
                }
                apps = list;
                loaded = DateTime.Now;
            }
            catch { }
            finally { loading = false; }
        }

        static bool Skip(string name, string path)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) return true;
            string l = SymbolDb.Normalize(name);
            if (l.Contains("uninstall") || l.Contains("desinstall")) return true;
            string p = path.ToLowerInvariant();
            foreach (string ext in new[] { ".url", ".chm", ".txt", ".html", ".htm", ".pdf", ".rtf", ".ini" })
                if (p.EndsWith(ext)) return true;
            return false;
        }

        Result Make(App a, int score)
        {
            string target = "shell:AppsFolder\\" + a.Path;
            return new Result
            {
                Title = a.Name,
                Subtitle = "Application",
                IconPath = target,
                Score = score,
                Key = "app:" + a.Path,
                EnterLabel = "Lancer",
                Run = () => Launch(target, false),
                AltLabel = "Lancer en administrateur",
                AltRun = () => Launch(target, true)
            };
        }

        static void Launch(string target, bool admin)
        {
            try { Open.Shell(target, null, admin); }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) return; // UAC refusé
                if (admin) throw;
                Process.Start("explorer.exe", target);
            }
        }

        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            foreach (var a in apps)
            {
                int s = Matcher.Score(q, a.Name);
                if (s >= 50) res.Add(Make(a, s + 10));
            }
            return res;
        }

        public override Result Resolve(string key)
        {
            if (!key.StartsWith("app:")) return null;
            string path = key.Substring(4);
            var a = apps.Find(x => x.Path == path);
            return a != null ? Make(a, 0) : null;
        }
    }

    // ---------- Fenêtres ouvertes ----------

    class WindowsProvider : Provider
    {
        public override string Prefix { get { return "<"; } }
        public override string HelpTitle { get { return "<  Fenêtres ouvertes"; } }
        public override string HelpText { get { return "basculer vers une fenêtre · Ctrl+Entrée pour la fermer"; } }

        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            if (!prefixed && q.Length < 2) return res;
            int self = Process.GetCurrentProcess().Id;
            var names = new Dictionary<uint, string>();
            foreach (IntPtr h in Native.TopWindows())
            {
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == self) continue;
                string title = Native.WindowText(h);
                string proc;
                if (!names.TryGetValue(pid, out proc))
                {
                    try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { proc = ""; }
                    names[pid] = proc;
                }
                int s = q.Length == 0 ? 50 : Math.Max(Matcher.Score(q, title), Matcher.Score(q, proc));
                if (s == 0 || (!prefixed && s < 50)) continue;
                IntPtr hw = h;
                res.Add(new Result
                {
                    Title = title,
                    Subtitle = "Fenêtre ouverte · " + proc,
                    IconHwnd = h,
                    Score = prefixed ? s : s * 8 / 10,
                    EnterLabel = "Afficher",
                    Run = () => Native.Activate(hw),
                    AltLabel = "Fermer la fenêtre",
                    AltRun = () => Native.PostMessage(hw, 0x10 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero),
                    AltKeepOpen = true
                });
            }
            return res;
        }
    }

    // ---------- Calculatrice, bases et conversions d'unités ----------

    class CalcProvider : Provider
    {
        public override string Prefix { get { return "="; } }
        public override string HelpTitle { get { return "=  Calculatrice et conversions"; } }
        public override string HelpText { get { return "2*(3+4) · sqrt(2) · 15% · 10 km en mi · 30 c en f · 255 en hex"; } }

        static readonly CultureInfo Fr = CultureInfo.CurrentCulture;

        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            string s = q.Trim();
            if (s.Length == 0) return res;

            // Bases : 255 en hex, 0xff en bin…
            var mb = Regex.Match(s, @"^(0x[0-9a-f]+|0b[01]+|-?\d+)\s+(?:en|in|to|vers)\s+(hexa?|hexadecimal|hexadécimal|bin|binaire|oct|octal|dec|décimal|decimal)$", RegexOptions.IgnoreCase);
            if (mb.Success)
            {
                long v = ParseInt(mb.Groups[1].Value);
                string to = mb.Groups[2].Value.ToLowerInvariant();
                string outv = to.StartsWith("hex") ? "0x" + v.ToString("X") : to.StartsWith("bin") ? "0b" + Convert.ToString(v, 2)
                    : to.StartsWith("oct") ? "0o" + Convert.ToString(v, 8) : v.ToString();
                res.Add(Value(outv, s, 96));
                return res;
            }

            string conv;
            string label;
            if (Units.TryConvert(s, out conv, out label)) { res.Add(Value(conv, label, 96)); return res; }

            if (prefixed || LooksLikeExpr(s))
            {
                double v;
                if (Expr.TryEval(s, out v))
                {
                    string shown = Format(v);
                    var r = Value(shown, s + " =", 95);
                    if (v == Math.Floor(v) && Math.Abs(v) < 1e15 && Math.Abs(v) >= 2)
                        r.Subtitle += "   (hex 0x" + ((long)v).ToString("X") + ", bin " + Convert.ToString((long)v, 2) + ")";
                    res.Add(r);
                }
            }
            return res;
        }

        static long ParseInt(string s)
        {
            s = s.ToLowerInvariant();
            if (s.StartsWith("0x")) return Convert.ToInt64(s.Substring(2), 16);
            if (s.StartsWith("0b")) return Convert.ToInt64(s.Substring(2), 2);
            return long.Parse(s);
        }

        static bool LooksLikeExpr(string s)
        {
            if (Regex.IsMatch(s, @"^0[xb][0-9a-f]+$", RegexOptions.IgnoreCase)) return true;
            if (!Regex.IsMatch(s, @"\d|\bpi\b|π")) return false;
            if (Regex.IsMatch(s, @"^-?\d+([.,]\d+)?$")) return false; // nombre seul
            return Regex.IsMatch(s, @"^[\d\s.,+\-*/×÷^%()!°πa-z√]+$", RegexOptions.IgnoreCase)
                && Regex.IsMatch(s, @"[+\-*/×÷^%()!√]|sqrt|racine|sin|cos|tan|log|ln|abs|exp|round|floor|ceil|0x|0b", RegexOptions.IgnoreCase);
        }

        public static string Format(double v)
        {
            if (double.IsNaN(v)) return "indéfini";
            if (double.IsInfinity(v)) return v > 0 ? "∞" : "-∞";
            double r = Math.Round(v, 10);
            if (Math.Abs(r) >= 1e15 || (Math.Abs(r) < 1e-6 && r != 0)) return r.ToString("G10", Fr);
            return r.ToString("#,0.##########", Fr);
        }

        static Result Value(string shown, string sub, int score)
        {
            string raw = shown.Replace(Fr.NumberFormat.NumberGroupSeparator, "").Replace("\u202F", "").Replace("\u00A0", "");
            return new Result
            {
                Title = shown,
                Subtitle = sub,
                Glyph = "=",
                Score = score,
                EnterLabel = "Insérer",
                Run = () => Input.Insert(raw),
                NeedsTarget = true,
                AltLabel = "Copier",
                AltRun = () => Input.Copy(raw)
            };
        }
    }

    // Évaluateur d'expressions : + - * / ^ % ! ( ), fonctions, pi, e, multiplication implicite, degrés (°).
    class Expr
    {
        readonly string s;
        int i;
        Expr(string s) { this.s = s.Replace(" ", "").Replace("×", "*").Replace("÷", "/").Replace("π", "pi").Replace("√", "sqrt").ToLowerInvariant(); }

        public static bool TryEval(string text, out double v)
        {
            v = 0;
            try
            {
                var e = new Expr(text);
                v = e.Sum();
                return e.i == e.s.Length;
            }
            catch { return false; }
        }

        char Peek { get { return i < s.Length ? s[i] : '\0'; } }

        double Sum()
        {
            double v = Product();
            while (Peek == '+' || Peek == '-') { char op = s[i++]; double r = Product(); v = op == '+' ? v + r : v - r; }
            return v;
        }

        double Product()
        {
            double v = Power();
            while (true)
            {
                if (Peek == '*' || Peek == '/') { char op = s[i++]; double r = Power(); v = op == '*' ? v * r : v / r; }
                else if (Peek == '(' || char.IsLetter(Peek)) v *= Power(); // multiplication implicite : 2pi, 3(4+1)
                else return v;
            }
        }

        double Power()
        {
            double b = Unary();
            if (Peek == '^') { i++; return Math.Pow(b, Power()); }
            return b;
        }

        double Unary()
        {
            if (Peek == '-') { i++; return -Unary(); }
            if (Peek == '+') { i++; return Unary(); }
            return Postfix();
        }

        double Postfix()
        {
            double v = Primary();
            while (true)
            {
                if (Peek == '%') { i++; v /= 100; }
                else if (Peek == '!') { i++; v = Fact(v); }
                else if (Peek == '°') { i++; v = v * Math.PI / 180; }
                else return v;
            }
        }

        static double Fact(double n)
        {
            if (n < 0 || n != Math.Floor(n) || n > 170) throw new FormatException();
            double r = 1;
            for (int k = 2; k <= n; k++) r *= k;
            return r;
        }

        double Primary()
        {
            if (Peek == '(') { i++; double v = Sum(); if (Peek != ')') throw new FormatException(); i++; return v; }
            if (char.IsDigit(Peek) || Peek == '.' || Peek == ',')
            {
                if (Peek == '0' && i + 1 < s.Length && (s[i + 1] == 'x' || s[i + 1] == 'b'))
                {
                    bool hex = s[i + 1] == 'x';
                    int st = i += 2;
                    while (i < s.Length && (hex ? Uri.IsHexDigit(s[i]) : (s[i] == '0' || s[i] == '1'))) i++;
                    return Convert.ToInt64(s.Substring(st, i - st), hex ? 16 : 2);
                }
                int start = i;
                while (char.IsDigit(Peek) || Peek == '.' || Peek == ',') i++;
                return double.Parse(s.Substring(start, i - start).Replace(',', '.'), CultureInfo.InvariantCulture);
            }
            if (char.IsLetter(Peek))
            {
                int start = i;
                while (char.IsLetter(Peek)) i++;
                string id = s.Substring(start, i - start);
                if (id == "pi") return Math.PI;
                if (id == "e") return Math.E;
                double a = Postfix();
                switch (id)
                {
                    case "sqrt": case "racine": return Math.Sqrt(a);
                    case "sin": return Math.Sin(a);
                    case "cos": return Math.Cos(a);
                    case "tan": return Math.Tan(a);
                    case "asin": return Math.Asin(a);
                    case "acos": return Math.Acos(a);
                    case "atan": return Math.Atan(a);
                    case "ln": return Math.Log(a);
                    case "log": return Math.Log10(a);
                    case "exp": return Math.Exp(a);
                    case "abs": return Math.Abs(a);
                    case "round": case "arrondi": return Math.Round(a);
                    case "floor": return Math.Floor(a);
                    case "ceil": return Math.Ceiling(a);
                }
            }
            throw new FormatException();
        }
    }

    static class Units
    {
        class U { public string Cat, Label; public double F; }
        static readonly Dictionary<string, U> map = new Dictionary<string, U>();

        static void Add(string cat, double f, string label, params string[] names)
        {
            var u = new U { Cat = cat, F = f, Label = label };
            map[label.ToLowerInvariant()] = u;
            foreach (string n in names) map[n] = u;
        }

        static Units()
        {
            Add("len", 0.001, "mm", "millimetre", "millimetres");
            Add("len", 0.01, "cm", "centimetre", "centimetres");
            Add("len", 1, "m", "metre", "metres", "meter", "meters");
            Add("len", 1000, "km", "kilometre", "kilometres");
            Add("len", 0.0254, "in", "pouce", "pouces", "po", "inch", "inches");
            Add("len", 0.3048, "ft", "pied", "pieds", "foot", "feet");
            Add("len", 0.9144, "yd", "yard", "yards");
            Add("len", 1609.344, "mi", "mile", "miles");
            Add("len", 1852, "nmi", "mille marin", "nautique");
            Add("mass", 1e-6, "mg", "milligramme");
            Add("mass", 0.001, "g", "gramme", "grammes");
            Add("mass", 1, "kg", "kilo", "kilos", "kilogramme", "kilogrammes");
            Add("mass", 1000, "t", "tonne", "tonnes");
            Add("mass", 0.45359237, "lb", "lbs", "livre", "livres", "pound", "pounds");
            Add("mass", 0.028349523125, "oz", "once", "onces", "ounce");
            Add("vol", 0.001, "ml", "millilitre");
            Add("vol", 0.01, "cl", "centilitre");
            Add("vol", 0.1, "dl", "decilitre");
            Add("vol", 1, "l", "litre", "litres");
            Add("vol", 1000, "m3", "metre cube");
            Add("vol", 3.785411784, "gal", "gallon", "gallons");
            Add("vol", 0.0295735295625, "floz", "fl oz");
            Add("area", 0.0001, "cm2");
            Add("area", 1, "m2");
            Add("area", 1e6, "km2");
            Add("area", 1e4, "ha", "hectare", "hectares");
            Add("area", 4046.8564224, "acre", "acres");
            Add("area", 0.09290304, "ft2");
            Add("speed", 1, "m/s", "mps");
            Add("speed", 1 / 3.6, "km/h", "kmh", "kph");
            Add("speed", 0.44704, "mph");
            Add("speed", 0.514444, "nd", "noeud", "noeuds", "kn", "knot", "knots");
            Add("time", 0.001, "ms", "milliseconde", "millisecondes");
            Add("time", 1, "s", "sec", "seconde", "secondes");
            Add("time", 60, "min", "minute", "minutes");
            Add("time", 3600, "h", "heure", "heures", "hour", "hours");
            Add("time", 86400, "j", "jour", "jours", "d", "day", "days");
            Add("time", 604800, "sem", "semaine", "semaines", "week", "weeks");
            Add("time", 31557600, "an", "ans", "annee", "annees", "year", "years");
            Add("data", 1, "o", "octet", "octets", "b", "byte", "bytes");
            Add("data", 1e3, "Ko", "ko", "kb");
            Add("data", 1e6, "Mo", "mo", "mb");
            Add("data", 1e9, "Go", "go", "gb");
            Add("data", 1e12, "To", "to", "tb");
            Add("data", 1024, "Kio", "kio", "kib");
            Add("data", 1048576, "Mio", "mio", "mib");
            Add("data", 1073741824, "Gio", "gio", "gib");
            Add("data", 1099511627776, "Tio", "tio", "tib");
            Add("temp", 1, "°C", "c", "celsius");
            Add("temp", 1, "°F", "f", "fahrenheit");
            Add("temp", 1, "K", "k", "kelvin");
        }

        static U Find(string name)
        {
            string n = name.Trim().ToLowerInvariant().Replace("°", "").Replace("²", "2").Replace("³", "3");
            n = SymbolDb.Normalize(n);
            U u;
            if (map.TryGetValue(n, out u)) return u;
            if (n.EndsWith("s") && map.TryGetValue(n.Substring(0, n.Length - 1), out u)) return u;
            return null;
        }

        public static bool TryConvert(string s, out string value, out string label)
        {
            value = label = null;
            var m = Regex.Match(s, @"^\s*([-+]?\d+(?:[.,]\d+)?)\s*(.+?)\s+(?:en|in|to|vers|->|=>|>)\s+(.+?)\s*$", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            U a = Find(m.Groups[2].Value), b = Find(m.Groups[3].Value);
            if (a == null || b == null || a.Cat != b.Cat) return false;
            double v = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            double r;
            if (a.Cat == "temp")
            {
                double c = a.Label == "°C" ? v : a.Label == "°F" ? (v - 32) * 5 / 9 : v - 273.15;
                r = b.Label == "°C" ? c : b.Label == "°F" ? c * 9 / 5 + 32 : c + 273.15;
            }
            else r = v * a.F / b.F;
            value = CalcProvider.Format(r) + " " + b.Label;
            label = CalcProvider.Format(v) + " " + a.Label + " =";
            return true;
        }
    }

    // ---------- Chemins (C:\…, %APPDATA%, ~) ----------

    class PathProvider : Provider
    {
        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            if (!Regex.IsMatch(q, @"^([a-zA-Z]:[\\/]|%|~|\\\\)")) return res;
            string full = q;
            if (full.StartsWith("~")) full = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + full.Substring(1);
            full = Environment.ExpandEnvironmentVariables(full).Replace('/', '\\');
            if (full.StartsWith("\\\\")) { if (Directory.Exists(full) || File.Exists(full)) res.Add(Item(full, Directory.Exists(full), 99)); return res; }
            try
            {
                if (Directory.Exists(full) && !full.EndsWith("\\")) res.Add(Item(full, true, 99));
                else if (File.Exists(full)) { res.Add(Item(full, false, 99)); return res; }
                string dir = full.EndsWith("\\") ? full : Path.GetDirectoryName(full);
                string filter = full.EndsWith("\\") ? "" : Path.GetFileName(full);
                if (dir == null || !Directory.Exists(dir)) return res;
                int n = 0;
                foreach (string d in Directory.EnumerateDirectories(dir))
                {
                    if (!Path.GetFileName(d).StartsWith(filter, StringComparison.OrdinalIgnoreCase)) continue;
                    res.Add(Item(d, true, 92));
                    if (++n >= 25) break;
                }
                foreach (string f in Directory.EnumerateFiles(dir))
                {
                    if (!Path.GetFileName(f).StartsWith(filter, StringComparison.OrdinalIgnoreCase)) continue;
                    res.Add(Item(f, false, 90));
                    if (++n >= 40) break;
                }
            }
            catch { }
            return res;
        }

        static Result Item(string path, bool dir, int score)
        {
            string name = Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) name = path;
            return new Result
            {
                Title = name,
                Subtitle = (dir ? "Dossier · " : "Fichier · ") + path,
                IconPath = path,
                Score = score,
                Run = () => Open.Shell(path),
                AltLabel = "Ouvrir l'emplacement",
                AltRun = () => Open.Location(path),
                TabFill = dir ? path.TrimEnd('\\') + "\\" : path
            };
        }
    }

    // ---------- Adresses web ----------

    class UrlProvider : Provider
    {
        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            if (!Regex.IsMatch(q, @"^(https?://)?([\w-]+\.)+[a-z]{2,}(:\d+)?(/\S*)?$", RegexOptions.IgnoreCase) || q.Contains(" ")) return res;
            if (Regex.IsMatch(q, @"^[\d.]+$")) return res; // 3.14
            string url = q.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? q : "https://" + q;
            res.Add(new Result
            {
                Title = "Ouvrir " + url,
                Subtitle = "Site web",
                Glyph = "🌐",
                Score = 97,
                Run = () => Open.Shell(url),
                AltLabel = "Copier l'adresse",
                AltRun = () => Input.Copy(url)
            });
            return res;
        }
    }

    // ---------- Fichiers (index de la recherche Windows) ----------

    class FilesProvider : Provider
    {
        public override string Prefix { get { return "?"; } }
        public override bool IsAsync { get { return true; } }
        public override string HelpTitle { get { return "?  Fichiers et dossiers"; } }
        public override string HelpText { get { return "recherche dans l'index Windows · Ctrl+Entrée : ouvrir l'emplacement"; } }

        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            if (q.Length < (prefixed ? 2 : 3)) return res;
            if (Regex.IsMatch(q, @"^([a-zA-Z]:[\\/]|%|~|\\\\)")) return res;
            var words = new List<string>();
            foreach (string w in q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string c = Regex.Replace(w, @"[^\p{L}\p{Nd}_\-.]", "");
                if (c.Length > 0) words.Add("\"" + c + "*\"");
            }
            if (words.Count == 0) return res;
            string sql = "SELECT TOP " + (prefixed ? 40 : 8) + " System.ItemPathDisplay, System.ItemNameDisplay FROM SYSTEMINDEX " +
                         "WHERE SCOPE='file:' AND CONTAINS(System.FileName, '" + string.Join(" AND ", words).Replace("'", "''") + "') " +
                         "ORDER BY System.Search.Rank DESC";
            try
            {
                using (var conn = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties=\"Application=Windows\""))
                {
                    conn.Open();
                    using (var cmd = new OleDbCommand(sql, conn))
                    using (var rd = cmd.ExecuteReader())
                    {
                        int rank = 0;
                        while (rd.Read())
                        {
                            string path = rd[0] as string;
                            string name = rd[1] as string;
                            if (string.IsNullOrEmpty(path)) continue;
                            bool dir = Directory.Exists(path);
                            string p = path;
                            res.Add(new Result
                            {
                                Title = string.IsNullOrEmpty(name) ? Path.GetFileName(path) : name,
                                Subtitle = (dir ? "Dossier · " : "Fichier · ") + Path.GetDirectoryName(path),
                                IconPath = path,
                                Score = (prefixed ? 60 : 18) - rank++,
                                Run = () => Open.Shell(p),
                                AltLabel = "Ouvrir l'emplacement",
                                AltRun = () => Open.Location(p)
                            });
                        }
                    }
                }
            }
            catch
            {
                if (prefixed) res.Add(new Result { Title = "Recherche Windows indisponible", Subtitle = "Le service d'indexation (WSearch) est arrêté ou désactivé", Glyph = "⚠" });
            }
            return res;
        }
    }

    // ---------- Recherche web ----------

    class WebProvider : Provider
    {
        public override string Prefix { get { return "!"; } }
        public override string HelpTitle { get { return "!  Recherche web"; } }
        public override string HelpText { get { return "!g Google · !yt YouTube · !w Wikipédia · !tr traduction · !maps · !gh GitHub · !img images"; } }

        static readonly string[][] Engines =
        {
            new[] { "g", "Google", "https://www.google.com/search?q={0}" },
            new[] { "yt", "YouTube", "https://www.youtube.com/results?search_query={0}" },
            new[] { "w", "Wikipédia", "https://fr.wikipedia.org/w/index.php?search={0}" },
            new[] { "tr", "Google Traduction", "https://translate.google.com/?sl=auto&tl=fr&text={0}&op=translate" },
            new[] { "maps", "Google Maps", "https://www.google.com/maps/search/{0}" },
            new[] { "gh", "GitHub", "https://github.com/search?q={0}" },
            new[] { "img", "Google Images", "https://www.google.com/search?tbm=isch&q={0}" },
            new[] { "ddg", "DuckDuckGo", "https://duckduckgo.com/?q={0}" },
        };

        public static Result Search(string[] e, string q, int score)
        {
            string url = string.Format(e[2], Uri.EscapeDataString(q));
            return new Result
            {
                Title = "Rechercher « " + q + " » sur " + e[1],
                Subtitle = url,
                Glyph = "🔍",
                Score = score,
                Key = null,
                Run = () => Open.Shell(url),
                AltLabel = "Copier le lien",
                AltRun = () => Input.Copy(url)
            };
        }

        public static void OpenEngine(string engine, string q)
        {
            foreach (var e in Engines) if (e[0] == engine) { Open.Shell(string.Format(e[2], Uri.EscapeDataString(q))); return; }
        }

        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            if (!prefixed)
            {
                if (q.Length > 0) res.Add(Search(Engines[0], q, 1));
                return res;
            }
            int sp = q.IndexOf(' ');
            string key = (sp < 0 ? q : q.Substring(0, sp)).ToLowerInvariant();
            string rest = sp < 0 ? "" : q.Substring(sp + 1).Trim();
            foreach (var e in Engines)
            {
                if (e[0] == key && rest.Length > 0) { res.Add(Search(e, rest, 90)); return res; }
            }
            foreach (var e in Engines)
            {
                if (key.Length > 0 && !e[0].StartsWith(key) && Matcher.Score(key, e[1]) == 0) continue;
                res.Add(new Result { Title = "!" + e[0] + "  " + e[1], Subtitle = "Tapez !" + e[0] + " puis votre recherche", Glyph = "🔍", Score = 50, FillQuery = "!" + e[0] + " " });
            }
            if (q.Length > 0) res.Add(Search(Engines[0], q, 40));
            return res;
        }
    }

    // ---------- Commandes système et Paramètres Windows ----------

    class SystemProvider : Provider
    {
        class Cmd { public string Id, Title, Words, Glyph; public Action Run; public bool Confirm; }
        readonly List<Cmd> cmds = new List<Cmd>();
        readonly List<string[]> settings = new List<string[]>();
        readonly Action showSettings;

        public SystemProvider(Action showHoldKeysSettings)
        {
            showSettings = showHoldKeysSettings;
            C("lock", "Verrouiller la session", "verrouiller lock", "🔒", () => Native.LockWorkStation(), false);
            C("sleep", "Mettre en veille", "veille sleep suspendre", "☾", () => Application.SetSuspendState(PowerState.Suspend, false, false), false);
            C("hibernate", "Mettre en veille prolongée", "veille prolongee hibernation hibernate", "☾", () => Application.SetSuspendState(PowerState.Hibernate, false, false), false);
            C("screenoff", "Éteindre l'écran", "ecran moniteur off", "🖵", () => Native.PostMessage((IntPtr)0xFFFF, 0x112, (IntPtr)0xF170, (IntPtr)2), false);
            C("restart", "Redémarrer l'ordinateur", "redemarrer restart reboot", "↻", () => Process.Start("shutdown", "/r /t 0"), true);
            C("shutdown", "Éteindre l'ordinateur", "eteindre arreter shutdown", "⏻", () => Process.Start("shutdown", "/s /t 0"), true);
            C("logoff", "Se déconnecter", "deconnecter logoff fermer session", "⇥", () => Process.Start("shutdown", "/l"), true);
            C("recycle", "Vider la corbeille", "corbeille vider recycle", "🗑", () => Native.SHEmptyRecycleBin(IntPtr.Zero, null, 1 | 2), true);
            C("holdkeys", "Paramètres HoldKeys", "holdkeys parametres reglages options", "à", () => showSettings(), false);

            S("Wi-Fi", "wifi reseau sans fil", "ms-settings:network-wifi");
            S("Bluetooth et appareils", "bluetooth peripheriques", "ms-settings:bluetooth");
            S("Affichage", "ecran resolution luminosite display", "ms-settings:display");
            S("Son", "audio volume micro haut-parleur", "ms-settings:sound");
            S("Notifications", "notifications ne pas deranger", "ms-settings:notifications");
            S("Alimentation et batterie", "batterie energie power", "ms-settings:powersleep");
            S("Stockage", "disque espace stockage", "ms-settings:storagesense");
            S("Applications installées", "programmes desinstaller apps", "ms-settings:appsfeatures");
            S("Applications au démarrage", "demarrage startup", "ms-settings:startupapps");
            S("Applications par défaut", "navigateur par defaut", "ms-settings:defaultapps");
            S("Arrière-plan", "fond d'ecran wallpaper", "ms-settings:personalization-background");
            S("Couleurs", "theme sombre clair mode couleur accent", "ms-settings:colors");
            S("Thèmes", "theme", "ms-settings:themes");
            S("Écran de verrouillage", "lockscreen", "ms-settings:lockscreen");
            S("Barre des tâches", "taskbar", "ms-settings:taskbar");
            S("Langue et région", "langue clavier region", "ms-settings:regionlanguage");
            S("Saisie et clavier", "clavier saisie typing", "ms-settings:typing");
            S("Date et heure", "horloge fuseau", "ms-settings:dateandtime");
            S("Windows Update", "mise a jour update", "ms-settings:windowsupdate");
            S("Confidentialité et sécurité", "vie privee privacy", "ms-settings:privacy");
            S("Imprimantes et scanners", "imprimante printer", "ms-settings:printers");
            S("Souris", "souris pointeur mouse", "ms-settings:mousetouchpad");
            S("Pavé tactile", "touchpad", "ms-settings:devices-touchpad");
            S("Réseau et Internet", "reseau ethernet internet", "ms-settings:network");
            S("VPN", "vpn", "ms-settings:network-vpn");
            S("Mode avion", "avion airplane", "ms-settings:network-airplanemode");
            S("Comptes", "compte utilisateur", "ms-settings:yourinfo");
            S("Options de connexion", "mot de passe pin connexion", "ms-settings:signinoptions");
            S("Accessibilité", "accessibilite loupe", "ms-settings:easeofaccess");
            S("Sécurité Windows", "antivirus defender", "windowsdefender:");
            S("Panneau de configuration", "control panel", "control", "tool");
            S("Gestionnaire des tâches", "task manager processus", "taskmgr", "tool");
            S("Gestionnaire de périphériques", "pilotes drivers", "devmgmt.msc", "tool");
            S("Gestion des disques", "partition disque", "diskmgmt.msc", "tool");
            S("Services", "services windows", "services.msc", "tool");
            S("Éditeur du Registre", "registre regedit", "regedit", "tool");
            S("Observateur d'événements", "logs evenements", "eventvwr.msc", "tool");
            S("Variables d'environnement", "path environnement", "rundll32.exe|sysdm.cpl,EditEnvironmentVariables", "tool");
            S("Programmes et fonctionnalités", "desinstaller programmes", "appwiz.cpl", "tool");
            S("Connexions réseau", "carte reseau adaptateur", "ncpa.cpl", "tool");
            S("Options d'alimentation", "alimentation", "powercfg.cpl", "tool");
            S("Propriétés système", "systeme nom ordinateur", "sysdm.cpl", "tool");
            S("Moniteur de ressources", "resmon performance", "resmon", "tool");
            S("Nettoyage de disque", "cleanmgr nettoyage", "cleanmgr", "tool");
        }

        void C(string id, string title, string words, string glyph, Action run, bool confirm)
        {
            cmds.Add(new Cmd { Id = id, Title = title, Words = words, Glyph = glyph, Run = run, Confirm = confirm });
        }

        void S(string title, string words, string target, string kind = "settings")
        {
            settings.Add(new[] { title, words, target, kind });
        }

        Result Make(Cmd c, int score)
        {
            return new Result { Title = c.Title, Subtitle = "Commande système", Glyph = c.Glyph, Score = score, Key = "sys:" + c.Id, EnterLabel = "Exécuter", Run = c.Run, Confirm = c.Confirm };
        }

        Result Make(string[] s, int score)
        {
            string target = s[2];
            bool tool = s[3] == "tool";
            Action run = () =>
            {
                int bar = target.IndexOf('|');
                if (bar > 0) Open.Shell(target.Substring(0, bar), target.Substring(bar + 1));
                else Open.Shell(target);
            };
            var r = new Result { Title = s[0], Subtitle = tool ? "Outil Windows" : "Paramètres Windows", Glyph = "⚙", Score = score, Key = "set:" + s[0], Run = run };
            if (tool && target.IndexOf('|') < 0)
            {
                r.AltLabel = "Ouvrir en administrateur";
                r.AltRun = () => Open.Shell(target, null, true);
            }
            return r;
        }

        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            foreach (var c in cmds)
            {
                int s = Math.Max(Matcher.Score(q, c.Title), Matcher.Score(q, c.Words) - 10);
                if (s >= 50) res.Add(Make(c, s));
            }
            foreach (var st in settings)
            {
                int s = Math.Max(Matcher.Score(q, st[0]), Matcher.Score(q, st[1]) - 25);
                if (s >= 50) res.Add(Make(st, s - 5));
            }
            return res;
        }

        public override Result Resolve(string key)
        {
            if (key.StartsWith("sys:")) { var c = cmds.Find(x => "sys:" + x.Id == key); return c != null ? Make(c, 0) : null; }
            if (key.StartsWith("set:")) { var s = settings.Find(x => "set:" + x[0] == key); return s != null ? Make(s, 0) : null; }
            return null;
        }
    }

    // ---------- Commandes shell ----------

    class ShellProvider : Provider
    {
        public override string Prefix { get { return ">"; } }
        public override bool Global { get { return false; } }
        public override string HelpTitle { get { return ">  Commande"; } }
        public override string HelpText { get { return "exécuter dans une invite de commandes · Ctrl+Entrée : en administrateur"; } }

        static Result Make(string cmd, int score)
        {
            return new Result
            {
                Title = cmd,
                Subtitle = "Exécuter dans une invite de commandes",
                Glyph = ">_",
                Score = score,
                Key = "sh:" + cmd,
                EnterLabel = "Exécuter",
                Run = () => Open.Shell("cmd.exe", "/k " + cmd),
                AltLabel = "Exécuter en administrateur",
                AltRun = () => Open.Shell("cmd.exe", "/k " + cmd, true)
            };
        }

        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            if (q.Length > 0) res.Add(Make(q, 90));
            return res;
        }

        public override Result Resolve(string key)
        {
            return key.StartsWith("sh:") ? Make(key.Substring(3), 0) : null;
        }
    }
}
