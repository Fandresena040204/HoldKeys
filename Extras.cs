// Extras : saisie, actions sur le texte, presse-papiers, texte rapide (snippets), générateurs, symboles.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace HoldKeys
{
    // ---------- Saisie dans l'application active ----------

    static class Input
    {
        public static void Insert(string s) { TrayContext.SendText(s, false); }

        public static void Copy(string s)
        {
            try { Clipboard.SetText(s); } catch { }
        }

        public static void Chord(ushort mod, ushort key)
        {
            var list = new[]
            {
                Native.KeyInput(mod, '\0', 0), Native.KeyInput(key, '\0', 0),
                Native.KeyInput(key, '\0', 2), Native.KeyInput(mod, '\0', 2)
            };
            Native.SendInput((uint)list.Length, list, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT)));
        }

        public static void Paste(string s)
        {
            try { Clipboard.SetText(s); }
            catch { Insert(s); return; }
            LauncherForm.Delay(60, () => Chord(0x11, 0x56)); // Ctrl+V
        }
    }

    // Lit la sélection de l'application active (Ctrl+C), la transforme et la recolle (Ctrl+V).
    static class TextOps
    {
        public static void Run(Func<string, string> transform, Action<string> other)
        {
            string saved = null;
            try { if (Clipboard.ContainsText()) saved = Clipboard.GetText(); } catch { }
            ClipboardHistory.Suspend++;
            try { Clipboard.Clear(); } catch { }
            Input.Chord(0x11, 0x43); // Ctrl+C
            int tries = 0;
            var t = new Timer { Interval = 40 };
            t.Tick += delegate
            {
                string sel = null;
                try { if (Clipboard.ContainsText()) sel = Clipboard.GetText(); } catch { }
                if (sel == null && ++tries < 15) return;
                t.Stop();
                t.Dispose();
                if (sel == null)
                {
                    Restore(saved);
                    TrayContext.Notify("Aucun texte sélectionné.");
                    return;
                }
                if (transform == null)
                {
                    Restore(saved);
                    other(sel);
                    return;
                }
                string result = transform(sel);
                try { Clipboard.SetText(result.Length == 0 ? " " : result); } catch { }
                Input.Chord(0x11, 0x56); // Ctrl+V
                LauncherForm.Delay(400, () => Restore(saved));
            };
            t.Start();
        }

        static void Restore(string saved)
        {
            try { if (saved != null) Clipboard.SetText(saved); else Clipboard.Clear(); } catch { }
            LauncherForm.Delay(200, () => ClipboardHistory.Suspend--);
        }

        public static string RemoveAccents(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC).Replace("œ", "oe").Replace("Œ", "OE").Replace("æ", "ae").Replace("Æ", "AE");
        }
    }

    // ---------- Historique du presse-papiers (chiffré avec le compte Windows) ----------

    class ClipItem { public string Text; public bool Pinned; public DateTime Time; }

    static class ClipboardHistory
    {
        public static readonly List<ClipItem> Items = new List<ClipItem>();
        public static bool Enabled = true;
        public static int Suspend;
        static string path;
        const int Max = 100;

        public static void Init(string dir)
        {
            path = Path.Combine(dir, "HoldKeys.clipboard.dat");
            try
            {
                if (!File.Exists(path)) return;
                byte[] data = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
                foreach (string line in Encoding.UTF8.GetString(data).Split('\n'))
                {
                    string[] p = line.Split('\t');
                    if (p.Length != 3) continue;
                    Items.Add(new ClipItem { Text = Encoding.UTF8.GetString(Convert.FromBase64String(p[0])), Pinned = p[1] == "1", Time = new DateTime(long.Parse(p[2])) });
                }
            }
            catch { }
        }

        public static void Save()
        {
            if (path == null) return;
            try
            {
                var sb = new StringBuilder();
                foreach (var it in Items)
                    sb.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(it.Text))).Append('\t').Append(it.Pinned ? "1" : "0").Append('\t').Append(it.Time.Ticks).Append('\n');
                File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(sb.ToString()), null, DataProtectionScope.CurrentUser));
            }
            catch { }
        }

        public static void OnChanged()
        {
            if (!Enabled || Suspend > 0) return;
            try
            {
                var d = Clipboard.GetDataObject();
                if (d == null) return;
                // Les gestionnaires de mots de passe demandent à ne pas être mémorisés.
                if (d.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || d.GetDataPresent("Clipboard Viewer Ignore")) return;
                if (d.GetDataPresent("CanIncludeInClipboardHistory"))
                {
                    var ms = d.GetData("CanIncludeInClipboardHistory") as MemoryStream;
                    if (ms != null && ms.Length >= 4 && BitConverter.ToInt32(ms.ToArray(), 0) == 0) return;
                }
                if (!d.GetDataPresent(DataFormats.UnicodeText)) return;
                string t = d.GetData(DataFormats.UnicodeText) as string;
                if (string.IsNullOrEmpty(t) || t.Trim().Length == 0 || t.Length > 200000) return;
                var existing = Items.Find(x => x.Text == t);
                if (existing != null) Items.Remove(existing);
                else existing = new ClipItem { Text = t };
                existing.Time = DateTime.Now;
                Items.Insert(0, existing);
                int unpinned = 0;
                for (int i = 0; i < Items.Count; i++)
                    if (!Items[i].Pinned && ++unpinned > Max) { Items.RemoveAt(i); i--; }
                Save();
            }
            catch { }
        }

        public static void Clear()
        {
            Items.RemoveAll(x => !x.Pinned);
            Save();
        }
    }

    // ---------- Texte rapide (snippets) ----------

    static class SnippetStore
    {
        public static List<KeyValuePair<string, string>> Items = new List<KeyValuePair<string, string>>();
        static string path;

        const string Defaults =
@";date = {date}
;heure = {heure}
;mail = prenom.nom@exemple.com
;sig = Cordialement,\nPrénom Nom
;merci = Merci beaucoup pour votre retour !
;shrug = ¯\\_(ツ)_/¯
";

        public static void Load(string dir)
        {
            path = Path.Combine(dir, "HoldKeys.snippets.txt");
            try
            {
                if (!File.Exists(path)) File.WriteAllText(path, "# Texte rapide : abréviation = texte   (\\n = retour à la ligne ; {date} {heure} {presse-papiers})\r\n" + Defaults, new UTF8Encoding(true));
                var list = new List<KeyValuePair<string, string>>();
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (raw.StartsWith("#")) continue;
                    int eq = raw.IndexOf(" = ");
                    if (eq <= 0) continue;
                    string trig = raw.Substring(0, eq).Trim();
                    if (trig.Length < 2 || trig.Contains(" ")) continue;
                    list.Add(new KeyValuePair<string, string>(trig, Unescape(raw.Substring(eq + 3))));
                }
                list.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length)); // les plus longues d'abord
                Items = list;
            }
            catch { }
        }

        public static void Save(List<KeyValuePair<string, string>> items)
        {
            var sb = new StringBuilder("# Texte rapide : abréviation = texte   (\\n = retour à la ligne ; {date} {heure} {presse-papiers})\r\n");
            foreach (var kv in items) sb.Append(kv.Key).Append(" = ").Append(Escape(kv.Value)).Append("\r\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Load(Path.GetDirectoryName(path));
        }

        public static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\n", "\\n").Replace("\t", "\\t"); }

        public static string Unescape(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static string Expand(string text)
        {
            var now = DateTime.Now;
            text = text.Replace("{date}", now.ToShortDateString()).Replace("{heure}", now.ToString("HH:mm"));
            if (text.Contains("{presse-papiers}"))
            {
                string c = "";
                try { if (Clipboard.ContainsText()) c = Clipboard.GetText(); } catch { }
                text = text.Replace("{presse-papiers}", c);
            }
            return text;
        }

        public static string Find(string typed)
        {
            foreach (var kv in Items) if (typed.EndsWith(kv.Key, StringComparison.Ordinal)) return kv.Key;
            return null;
        }

        public static string TextOf(string trigger)
        {
            foreach (var kv in Items) if (kv.Key == trigger) return kv.Value;
            return null;
        }
    }

    // ---------- Modules du lanceur ----------

    class GeneratorsProvider : Provider
    {
        public override string HelpTitle { get { return "★  Générateurs"; } }
        public override string HelpText { get { return "mdp 20 · guid · date · heure · timestamp · sha256 texte · lorem 30 · nombre 1 100"; } }
        public override string HelpFill { get { return "mdp "; } }

        static readonly string[] Keywords = { "mdp", "motdepasse", "password", "guid", "uuid", "date", "heure", "maintenant", "now", "timestamp", "unix", "md5", "sha1", "sha256", "sha512", "lorem", "nombre", "random", "aleatoire" };

        static Result Gen(string title, string value, string sub)
        {
            return new Result
            {
                Title = title,
                Subtitle = sub,
                Glyph = "✱",
                Score = 93,
                EnterLabel = "Insérer",
                Run = () => Input.Insert(value),
                NeedsTarget = true,
                AltLabel = "Copier",
                AltRun = () => Input.Copy(value)
            };
        }

        static int RandomInt(int max)
        {
            var b = new byte[4];
            using (var rng = new RNGCryptoServiceProvider())
            {
                uint limit = uint.MaxValue - uint.MaxValue % (uint)max, v;
                do { rng.GetBytes(b); v = BitConverter.ToUInt32(b, 0); } while (v >= limit);
                return (int)(v % (uint)max);
            }
        }

        static string Password(int n, string alphabet)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(alphabet[RandomInt(alphabet.Length)]);
            return sb.ToString();
        }

        static string Hash(HashAlgorithm h, string s)
        {
            using (h) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").ToLowerInvariant();
        }

        const string Lorem = "lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore magna aliqua ut enim ad minim veniam quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur excepteur sint occaecat cupidatat non proident sunt in culpa qui officia deserunt mollit anim id est laborum";

        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            int sp = q.IndexOf(' ');
            string w = SymbolDb.Normalize(sp < 0 ? q : q.Substring(0, sp));
            string arg = sp < 0 ? "" : q.Substring(sp + 1).Trim();
            string kw = null;
            foreach (string k in Keywords) if (k == w || (w.Length >= 3 && k.StartsWith(w))) { kw = k; break; }
            if (kw == null) return res;
            int n;
            var now = DateTime.Now;
            switch (kw)
            {
                case "mdp": case "motdepasse": case "password":
                    if (!int.TryParse(arg, out n)) n = 16;
                    n = Math.Max(4, Math.Min(128, n));
                    string strong = Password(n, "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%&*-_=+?");
                    string alnum = Password(n, "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789");
                    res.Add(Gen(strong, strong, "Mot de passe fort · " + n + " caractères"));
                    res.Add(Gen(alnum, alnum, "Mot de passe lettres et chiffres · " + n + " caractères"));
                    string pin = Password(6, "0123456789");
                    res.Add(Gen(pin, pin, "Code PIN · 6 chiffres"));
                    break;
                case "guid": case "uuid":
                    var g = Guid.NewGuid();
                    res.Add(Gen(g.ToString(), g.ToString(), "GUID"));
                    res.Add(Gen(g.ToString("N"), g.ToString("N"), "GUID sans tirets"));
                    res.Add(Gen(g.ToString("B").ToUpperInvariant(), g.ToString("B").ToUpperInvariant(), "GUID entre accolades"));
                    break;
                case "date":
                    foreach (string f in new[] { now.ToShortDateString(), now.ToString("yyyy-MM-dd"), now.ToLongDateString() })
                        res.Add(Gen(f, f, "Date du jour"));
                    break;
                case "heure":
                    foreach (string f in new[] { now.ToString("HH:mm"), now.ToString("HH:mm:ss") }) res.Add(Gen(f, f, "Heure actuelle"));
                    break;
                case "maintenant": case "now":
                    foreach (string f in new[] { now.ToString("yyyy-MM-dd HH:mm:ss"), now.ToString("o"), now.ToString("F") }) res.Add(Gen(f, f, "Date et heure"));
                    break;
                case "timestamp": case "unix":
                    long ts;
                    if (long.TryParse(arg, out ts))
                    {
                        var d = (ts > 99999999999 ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ts) : new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(ts)).ToLocalTime();
                        res.Add(Gen(d.ToString("F"), d.ToString("yyyy-MM-dd HH:mm:ss"), "Timestamp " + ts + " en date locale"));
                    }
                    else
                    {
                        long s = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                        res.Add(Gen(s.ToString(), s.ToString(), "Timestamp Unix (secondes)"));
                        res.Add(Gen((s * 1000).ToString(), (s * 1000).ToString(), "Timestamp Unix (millisecondes)"));
                    }
                    break;
                case "md5": case "sha1": case "sha256": case "sha512":
                    if (arg.Length == 0) { res.Add(new Result { Title = kw + " <texte>", Subtitle = "Tapez le texte à hacher après " + kw, Glyph = "✱", Score = 93 }); break; }
                    HashAlgorithm h = kw == "md5" ? (HashAlgorithm)MD5.Create() : kw == "sha1" ? (HashAlgorithm)SHA1.Create() : kw == "sha256" ? (HashAlgorithm)SHA256.Create() : SHA512.Create();
                    string hv = Hash(h, arg);
                    res.Add(Gen(hv, hv, kw.ToUpperInvariant() + " de « " + arg + " »"));
                    break;
                case "lorem":
                    if (!int.TryParse(arg, out n)) n = 50;
                    n = Math.Max(1, Math.Min(1000, n));
                    string[] words = Lorem.Split(' ');
                    var sb = new StringBuilder();
                    for (int i = 0; i < n; i++) sb.Append(i == 0 ? "Lorem" : words[i % words.Length]).Append(i == n - 1 ? "." : " ");
                    res.Add(Gen(sb.ToString(), sb.ToString(), "Lorem ipsum · " + n + " mots"));
                    break;
                case "nombre": case "random": case "aleatoire":
                    int a = 1, b = 100;
                    string[] p = arg.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 2) { int.TryParse(p[0], out a); int.TryParse(p[1], out b); }
                    else if (p.Length == 1) int.TryParse(p[0], out b);
                    if (b < a) { int t = a; a = b; b = t; }
                    string r = (a + RandomInt(b - a + 1)).ToString();
                    res.Add(Gen(r, r, "Nombre aléatoire entre " + a + " et " + b));
                    break;
            }
            return res;
        }
    }

    class TextActionsProvider : Provider
    {
        public override string Prefix { get { return "%"; } }
        public override string HelpTitle { get { return "%  Actions sur le texte sélectionné"; } }
        public override string HelpText { get { return "sélectionnez du texte puis : majuscules, sans accents, trier, traduire, compter…"; } }

        public class Act { public string Id, Title, PasteTitle, Words; public Func<string, string> T; public Action<string> O; }
        internal static readonly List<Act> All = new List<Act>();
        readonly List<Act> acts = All;
        static readonly CultureInfo Cul = CultureInfo.CurrentCulture;

        // Transformations partagées avec le collage avancé (PasteTitle).
        static TextActionsProvider()
        {
            A("upper", "MAJUSCULES", "Coller en MAJUSCULES", "majuscules maj upper", s => s.ToUpper(Cul));
            A("lower", "minuscules", "Coller en minuscules", "minuscules min lower", s => s.ToLower(Cul));
            A("title", "Première Lettre En Majuscule", "Coller avec une majuscule à chaque mot", "titre title capitaliser mots", s => Cul.TextInfo.ToTitleCase(s.ToLower(Cul)));
            A("sentence", "Phrase normale (majuscule au début)", "Coller en phrase normale", "phrase sentence", Sentence);
            A("swap", "iNVERSER LA cASSE", "Coller avec la casse inversée", "inverser casse swap", s => new string(s.Select(c => char.IsUpper(c) ? char.ToLower(c) : char.ToUpper(c)).ToArray()));
            A("noaccent", "Supprimer les accents", "Coller sans accents", "sans accents enlever", TextOps.RemoveAccents);
            A("spaces", "Supprimer les espaces en trop", "Coller sans espaces en trop", "espaces trim nettoyer", s => string.Join("\n", s.Replace("\r\n", "\n").Split('\n').Select(l => Regex.Replace(l, @"[ \t]+", " ").Trim())));
            A("sort", "Trier les lignes", "Coller avec les lignes triées", "trier lignes sort alphabetique", s => string.Join("\r\n", s.Replace("\r\n", "\n").Split('\n').OrderBy(l => l, StringComparer.Create(Cul, true))));
            A("dedup", "Supprimer les lignes en double", "Coller sans lignes en double", "doublons lignes unique", s => string.Join("\r\n", s.Replace("\r\n", "\n").Split('\n').Distinct()));
            A("urlenc", "Encoder pour une URL", "Coller encodé pour une URL", "url encoder encode", Uri.EscapeDataString);
            A("urldec", "Décoder une URL", "Coller l'URL décodée", "url decoder decode", Uri.UnescapeDataString);
            O("count", "Compter mots et caractères", "compter mots caracteres count", Count);
            O("translate", "Traduire la sélection", "traduire traduction translate", s => WebProvider.OpenEngine("tr", s));
            O("search", "Rechercher la sélection sur le web", "rechercher google web", s => WebProvider.OpenEngine("g", s));
        }

        static void A(string id, string title, string pasteTitle, string words, Func<string, string> t) { All.Add(new Act { Id = id, Title = title, PasteTitle = pasteTitle, Words = words, T = t }); }
        static void O(string id, string title, string words, Action<string> o) { All.Add(new Act { Id = id, Title = title, Words = words, O = o }); }

        static string Sentence(string s)
        {
            var sb = new StringBuilder(s.ToLower(Cul));
            bool start = true;
            for (int i = 0; i < sb.Length; i++)
            {
                if (start && char.IsLetter(sb[i])) { sb[i] = char.ToUpper(sb[i], Cul); start = false; }
                else if (".!?\n".IndexOf(sb[i]) >= 0) start = true;
            }
            return sb.ToString();
        }

        static void Count(string s)
        {
            int words = Regex.Matches(s, @"[\p{L}\p{Nd}'’-]+").Count;
            int chars = s.Replace("\r\n", "\n").Length;
            int noSpace = Regex.Replace(s, @"\s", "").Length;
            int lines = s.Replace("\r\n", "\n").Split('\n').Length;
            TrayContext.Notify(words + " mots · " + chars + " caractères (" + noSpace + " sans espaces) · " + lines + " ligne(s)");
        }

        Result Make(Act a, int score)
        {
            Act act = a;
            return new Result
            {
                Title = a.Title,
                Subtitle = "Action sur le texte sélectionné",
                Glyph = "Aa",
                Score = score,
                Key = "txt:" + a.Id,
                EnterLabel = "Appliquer",
                Run = () => TextOps.Run(act.T, act.O),
                NeedsTarget = true
            };
        }

        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            foreach (var a in acts)
            {
                int s = q.Length == 0 && prefixed ? 60 : Math.Max(Matcher.Score(q, a.Title), Matcher.Score(q, a.Words) - 5);
                if (prefixed ? s > 0 : s >= 70) res.Add(Make(a, prefixed ? s : s - 20));
            }
            return res;
        }

        public override Result Resolve(string key)
        {
            var a = acts.Find(x => "txt:" + x.Id == key);
            return a != null ? Make(a, 0) : null;
        }
    }

    class SymbolsProvider : Provider
    {
        readonly Func<List<Sym>> db;
        readonly Func<IList<string>> recent;
        readonly Action<string> remember;

        public SymbolsProvider(Func<List<Sym>> db, Func<IList<string>> recent, Action<string> remember)
        {
            this.db = db; this.recent = recent; this.remember = remember;
        }

        public override string Prefix { get { return ":"; } }
        public override string HelpTitle { get { return ":  Symboles et emoji"; } }
        public override string HelpText { get { return ":flèche · :euro · :cœur · :e aigu · :pouce — plus de 400 symboles"; } }

        Result Make(Sym s, int score)
        {
            string ch = s.Ch;
            string name = s.Name;
            int comma = name.IndexOf(',');
            return new Result
            {
                Title = comma > 0 ? name.Substring(0, comma) : name,
                Subtitle = "Symbole " + ch + " · U+" + char.ConvertToUtf32(ch, 0).ToString("X4"),
                Glyph = ch,
                Score = score,
                Key = "sym:" + ch,
                EnterLabel = "Insérer",
                Run = () => { Input.Insert(ch); remember(ch); },
                NeedsTarget = true,
                AltLabel = "Copier",
                AltRun = () => { Input.Copy(ch); remember(ch); }
            };
        }

        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            if (!prefixed && q.Length < 3) return res;
            var found = SymbolDb.Search(db(), q, recent());
            int max = prefixed ? 80 : 3;
            for (int i = 0; i < found.Count && res.Count < max; i++)
            {
                if (!prefixed && Matcher.Score(q, found[i].Name) < 70) continue;
                res.Add(Make(found[i], prefixed ? 80 - i / 2 : 38 - i));
            }
            return res;
        }

        public override Result Resolve(string key)
        {
            if (!key.StartsWith("sym:")) return null;
            string ch = key.Substring(4);
            var s = db().Find(x => x.Ch == ch);
            return s != null ? Make(s, 0) : null;
        }
    }

    class ClipboardProvider : Provider
    {
        public override string Prefix { get { return "v "; } }
        public override bool Global { get { return false; } }
        public override string HelpTitle { get { return "v  Presse-papiers"; } }
        public override string HelpText { get { return "historique de ce que vous copiez · Ctrl+Entrée : épingler · Maj+Suppr : effacer"; } }

        static string Age(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "à l'instant";
            if (d.TotalHours < 1) return "il y a " + (int)d.TotalMinutes + " min";
            if (d.TotalDays < 1) return "il y a " + (int)d.TotalHours + " h";
            return t.ToString("d MMM HH:mm");
        }

        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            if (!ClipboardHistory.Enabled)
            {
                res.Add(new Result { Title = "Historique du presse-papiers désactivé", Subtitle = "Activez-le dans les Paramètres HoldKeys", Glyph = "📋" });
                return res;
            }
            string nq = SymbolDb.Normalize(q);
            int i = 0;
            foreach (var it in ClipboardHistory.Items.OrderByDescending(x => x.Pinned))
            {
                ClipItem item = it;
                if (nq.Length > 0 && !SymbolDb.Normalize(it.Text).Contains(nq)) { i++; continue; }
                string first = it.Text.Trim().Replace("\r", "").Split('\n')[0];
                if (first.Length > 150) first = first.Substring(0, 150) + "…";
                res.Add(new Result
                {
                    Title = first,
                    Subtitle = (it.Pinned ? "📌 Épinglé · " : "") + Age(it.Time) + " · " + it.Text.Length + " caractères" + (it.Text.Contains("\n") ? " · plusieurs lignes" : ""),
                    Glyph = "📋",
                    Score = 1000 - i++,
                    EnterLabel = "Coller",
                    Run = () => Input.Paste(item.Text),
                    NeedsTarget = true,
                    AltLabel = it.Pinned ? "Désépingler" : "Épingler",
                    AltRun = () => { item.Pinned = !item.Pinned; ClipboardHistory.Save(); },
                    AltKeepOpen = true,
                    DeleteRun = () => { ClipboardHistory.Items.Remove(item); ClipboardHistory.Save(); }
                });
            }
            if (res.Count == 0) res.Add(new Result { Title = nq.Length > 0 ? "Aucun élément ne correspond" : "Rien copié pour l'instant", Subtitle = "Copiez du texte (Ctrl+C) pour le retrouver ici", Glyph = "📋" });
            return res;
        }
    }

    class SnippetsProvider : Provider
    {
        readonly Action showSettings;
        public SnippetsProvider(Action showSettings) { this.showSettings = showSettings; }

        public override string Prefix { get { return ";"; } }
        public override bool Global { get { return false; } }
        public override string HelpTitle { get { return ";  Texte rapide"; } }
        public override string HelpText { get { return "tapez ;mail ou ;sig n'importe où pour le remplacer par votre texte"; } }

        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            string nq = SymbolDb.Normalize(q);
            int i = 0;
            foreach (var kv in SnippetStore.Items.OrderBy(x => x.Key))
            {
                string text = kv.Value;
                if (nq.Length > 0 && !SymbolDb.Normalize(kv.Key).Contains(nq) && !SymbolDb.Normalize(text).Contains(nq)) continue;
                string preview = text.Replace("\n", " ⏎ ");
                res.Add(new Result
                {
                    Title = kv.Key,
                    Subtitle = preview.Length > 120 ? preview.Substring(0, 120) + "…" : preview,
                    Glyph = "✎",
                    Score = 500 - i++,
                    EnterLabel = "Insérer",
                    Run = () => Input.Insert(SnippetStore.Expand(text)),
                    NeedsTarget = true,
                    AltLabel = "Copier",
                    AltRun = () => Input.Copy(SnippetStore.Expand(text))
                });
            }
            res.Add(new Result { Title = "Gérer le texte rapide…", Subtitle = "Ajouter ou modifier des abréviations dans les Paramètres", Glyph = "⚙", Score = 0, Run = showSettings });
            return res;
        }
    }
}
