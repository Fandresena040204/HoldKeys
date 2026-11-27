// Collage avancé (Win+Shift+V) : texte brut, Markdown, JSON, fichiers, OCR, transformations et IA (Claude).
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace HoldKeys
{
    // ---------- Collage d'un contenu puis restauration du presse-papiers ----------

    static class PasteOps
    {
        // Copie des formats utiles du presse-papiers (pour le restaurer après le collage).
        public static DataObject Snapshot()
        {
            var o = new DataObject();
            bool any = false;
            try
            {
                var d = Clipboard.GetDataObject();
                if (d == null) return null;
                foreach (string f in new[] { DataFormats.UnicodeText, DataFormats.Html, DataFormats.Rtf })
                    if (d.GetDataPresent(f)) { var v = d.GetData(f); if (v != null) { o.SetData(f, v); any = true; } }
                if (Clipboard.ContainsImage()) { var img = Clipboard.GetImage(); if (img != null) { o.SetImage(img); any = true; } }
                if (Clipboard.ContainsFileDropList()) { o.SetFileDropList(Clipboard.GetFileDropList()); any = true; }
            }
            catch { }
            return any ? o : null;
        }

        public static void Paste(DataObject data, int restoreAfterMs = 700)
        {
            DataObject saved = Snapshot();
            ClipboardHistory.Suspend++;
            try { Clipboard.SetDataObject(data, true, 5, 50); }
            catch { ClipboardHistory.Suspend--; TrayContext.Notify("Presse-papiers occupé, réessayez."); return; }
            LauncherForm.Delay(40, () => Input.Chord(0x11, 0x56)); // Ctrl+V
            LauncherForm.Delay(restoreAfterMs, () =>
            {
                try { if (saved != null) Clipboard.SetDataObject(saved, true, 5, 50); } catch { }
                LauncherForm.Delay(200, () => ClipboardHistory.Suspend--);
            });
        }

        public static void PasteText(string s)
        {
            var d = new DataObject();
            d.SetText(s.Length == 0 ? " " : s, TextDataFormat.UnicodeText);
            Paste(d);
        }

        public static void PasteFile(string path)
        {
            var d = new DataObject();
            d.SetFileDropList(new StringCollection { path });
            Paste(d, 1500);
        }

        public static string TempFile(string ext)
        {
            string dir = Path.Combine(Path.GetTempPath(), "HoldKeys", "Collage");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "presse-papiers " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ext);
        }

        public static string SaveToDownloads(string tempPath)
        {
            string dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(dl);
            string dest = Path.Combine(dl, Path.GetFileName(tempPath));
            File.Copy(tempPath, dest, true);
            Open.Location(dest);
            return dest;
        }

        public static byte[] Png(Image img)
        {
            using (var ms = new MemoryStream()) { img.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }
    }

    // ---------- Conversions ----------

    static class Convert2
    {
        // Extrait le fragment d'un presse-papiers « HTML Format ».
        public static string HtmlFragment(string cf)
        {
            if (cf == null) return null;
            if (cf.Contains("Ã") || cf.Contains("Â")) // UTF-8 relu en Latin-1
                try { cf = Encoding.UTF8.GetString(Encoding.GetEncoding(1252).GetBytes(cf)); } catch { }
            int a = cf.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
            int b = cf.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
            if (a >= 0 && b > a) return cf.Substring(a + 20, b - a - 20);
            int h = cf.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
            return h >= 0 ? cf.Substring(h) : cf;
        }

        static string Strip(string s) { return WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", "")).Trim(); }

        public static string HtmlToMarkdown(string html)
        {
            const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.Singleline;
            string s = Regex.Replace(html, @"<(script|style|head)[^>]*>.*?</\1>", "", O);
            s = Regex.Replace(s, @"<!--.*?-->", "", O);
            s = Regex.Replace(s, @"\r?\n", " ");
            s = Regex.Replace(s, @"<pre[^>]*>(.*?)</pre>", m => "\n```\n" + Strip(Regex.Replace(m.Groups[1].Value, @"<br\s*/?>", "\n", O)) + "\n```\n", O);
            s = Regex.Replace(s, @"<table[^>]*>(.*?)</table>", m => Table(m.Groups[1].Value), O);
            for (int i = 6; i >= 1; i--)
            {
                int n = i;
                s = Regex.Replace(s, "<h" + i + @"[^>]*>(.*?)</h" + i + ">", m => "\n\n" + new string('#', n) + " " + Strip(m.Groups[1].Value) + "\n\n", O);
            }
            s = Regex.Replace(s, @"<(strong|b)\b[^>]*>(.*?)</\1>", "**$2**", O);
            s = Regex.Replace(s, @"<(em|i)\b[^>]*>(.*?)</\1>", "*$2*", O);
            s = Regex.Replace(s, @"<(s|del|strike)\b[^>]*>(.*?)</\1>", "~~$2~~", O);
            s = Regex.Replace(s, @"<code[^>]*>(.*?)</code>", "`$1`", O);
            s = Regex.Replace(s, @"<img[^>]*?src=""([^""]*)""[^>]*?(?:alt=""([^""]*)"")?[^>]*>", "![$2]($1)", O);
            s = Regex.Replace(s, @"<a[^>]*?href=""([^""]*)""[^>]*>(.*?)</a>", m => "[" + Strip(m.Groups[2].Value) + "](" + m.Groups[1].Value + ")", O);
            s = Regex.Replace(s, @"<ol[^>]*>(.*?)</ol>", m =>
            {
                int k = 0;
                return "\n" + Regex.Replace(m.Groups[1].Value, @"<li[^>]*>(.*?)</li>", li => (++k) + ". " + Strip(li.Groups[1].Value) + "\n", O) + "\n";
            }, O);
            s = Regex.Replace(s, @"<li[^>]*>(.*?)</li>", m => "- " + Strip(m.Groups[1].Value) + "\n", O);
            s = Regex.Replace(s, @"</?(ul|ol)[^>]*>", "\n", O);
            s = Regex.Replace(s, @"<blockquote[^>]*>(.*?)</blockquote>", m => "\n> " + Strip(m.Groups[1].Value) + "\n", O);
            s = Regex.Replace(s, @"<hr[^>]*>", "\n\n---\n\n", O);
            s = Regex.Replace(s, @"<br\s*/?>", "  \n", O);
            s = Regex.Replace(s, @"</(p|div|section|article|header|footer)>", "\n\n", O);
            s = Regex.Replace(s, "<[^>]+>", "");
            s = WebUtility.HtmlDecode(s).Replace(' ', ' ');
            s = Regex.Replace(s, @"[ \t]+\n", "\n");
            s = Regex.Replace(s, @"\n[ \t]+", "\n");
            s = Regex.Replace(s, @"\n{3,}", "\n\n");
            return s.Trim();
        }

        static string Table(string inner)
        {
            const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.Singleline;
            var sb = new StringBuilder("\n\n");
            bool first = true;
            foreach (Match tr in Regex.Matches(inner, @"<tr[^>]*>(.*?)</tr>", O))
            {
                var cells = Regex.Matches(tr.Groups[1].Value, @"<t[hd][^>]*>(.*?)</t[hd]>", O).Cast<Match>().Select(c => Strip(c.Groups[1].Value).Replace("|", "\\|")).ToList();
                if (cells.Count == 0) continue;
                sb.Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");
                if (first) { sb.Append("|").Append(string.Concat(Enumerable.Repeat(" --- |", cells.Count))).Append("\n"); first = false; }
            }
            return sb.Append("\n").ToString();
        }

        // ----- JSON -----

        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static bool TryParseJson(string s, out object v)
        {
            v = null;
            string t = s.Trim();
            if (!(t.StartsWith("{") && t.EndsWith("}")) && !(t.StartsWith("[") && t.EndsWith("]"))) return false;
            try { v = Js.DeserializeObject(t); return true; } catch { return false; }
        }

        public static string Pretty(object v, int indent = 0)
        {
            string pad = new string(' ', indent * 2), pad2 = new string(' ', (indent + 1) * 2);
            var dict = v as IDictionary<string, object>;
            if (dict != null)
            {
                if (dict.Count == 0) return "{}";
                return "{\n" + string.Join(",\n", dict.Select(kv => pad2 + Js.Serialize(kv.Key) + ": " + Pretty(kv.Value, indent + 1))) + "\n" + pad + "}";
            }
            var arr = v as System.Collections.IEnumerable;
            if (arr != null && !(v is string))
            {
                var items = arr.Cast<object>().ToList();
                if (items.Count == 0) return "[]";
                return "[\n" + string.Join(",\n", items.Select(x => pad2 + Pretty(x, indent + 1))) + "\n" + pad + "]";
            }
            return Js.Serialize(v);
        }

        public static string Minify(object v) { return Js.Serialize(v); }

        // Texte → JSON : JSON (indenté), CSV/TSV (tableau d'objets) ou lignes « clé: valeur » (objet).
        public static string ToJson(string text, out string how)
        {
            how = null;
            object v;
            if (TryParseJson(text, out v)) { how = "JSON indenté"; return Pretty(v); }
            var lines = text.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count >= 2)
            {
                foreach (char sep in new[] { '\t', ';', ',' })
                {
                    var rows = lines.Select(l => SplitCsv(l, sep)).ToList();
                    int n = rows[0].Count;
                    if (n < 2 || rows.Any(r => r.Count != n)) continue;
                    var list = new List<object>();
                    foreach (var r in rows.Skip(1))
                    {
                        var o = new Dictionary<string, object>();
                        for (int i = 0; i < n; i++) o[rows[0][i]] = Typed(r[i]);
                        list.Add(o);
                    }
                    how = (sep == '\t' ? "Tableau (TSV)" : "CSV") + " → JSON · " + list.Count + " lignes";
                    return Pretty(list);
                }
            }
            if (lines.Count >= 1 && lines.All(l => Regex.IsMatch(l, @"^\s*[^:=]+?\s*[:=]\s*.*$")))
            {
                var o = new Dictionary<string, object>();
                foreach (string l in lines)
                {
                    var m = Regex.Match(l, @"^\s*([^:=]+?)\s*[:=]\s*(.*)$");
                    o[m.Groups[1].Value] = Typed(m.Groups[2].Value.Trim());
                }
                how = "Lignes « clé : valeur » → JSON";
                return Pretty(o);
            }
            return null;
        }

        static object Typed(string s)
        {
            double d;
            if (s == "true" || s == "false") return s == "true";
            if (s == "null" || s.Length == 0) return null;
            if (Regex.IsMatch(s, @"^-?\d+(\.\d+)?$") && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            return s;
        }

        static List<string> SplitCsv(string line, char sep)
        {
            var res = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') { if (q && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = !q; }
                else if (c == sep && !q) { res.Add(sb.ToString().Trim()); sb.Length = 0; }
                else sb.Append(c);
            }
            res.Add(sb.ToString().Trim());
            return res;
        }
    }

    // ---------- Texte d'une image (OCR de Windows, hors ligne) ----------

    static class Ocr
    {
        static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            while (op.Status == Windows.Foundation.AsyncStatus.Started) Thread.Sleep(10);
            if (op.Status != Windows.Foundation.AsyncStatus.Completed) throw op.ErrorCode ?? new Exception("OCR interrompu");
            return op.GetResults();
        }

        public static string Recognize(byte[] png)
        {
            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) throw new Exception("Aucune langue de reconnaissance de texte installée dans Windows.");
            var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var w = new Windows.Storage.Streams.DataWriter(ras);
            w.WriteBytes(png);
            Wait(w.StoreAsync());
            ras.Seek(0);
            var dec = Wait(Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras));
            var bmp = Wait(dec.GetSoftwareBitmapAsync());
            var res = Wait(engine.RecognizeAsync(bmp));
            return string.Join(Environment.NewLine, res.Lines.Select(l => l.Text));
        }
    }

    // ---------- IA : API Claude (clé de l'utilisateur, chiffrée avec son compte Windows) ----------

    static class Claude
    {
        public static readonly string[][] Models =
        {
            new[] { "claude-opus-5-5", "Claude Opus 5.5 (recommandé)" },
            new[] { "claude-sonnet-5-5", "Claude Sonnet 5.5" },
            new[] { "claude-haiku-5-5", "Claude Haiku 5.5 (rapide, économique)" },
        };

        public static string Model = "claude-opus-5-5";
        public static List<KeyValuePair<string, string>> Actions = new List<KeyValuePair<string, string>>();
        static string dir;

        const string DefaultActions =
@"Corriger l'orthographe = Corrige l'orthographe, la grammaire et la ponctuation sans changer le sens ni le ton.
Traduire en anglais = Traduis en anglais.
Traduire en français = Traduis en français.
Résumer = Résume en quelques phrases.
Rendre plus formel = Réécris dans un style professionnel et formel.
Rendre plus concis = Réécris de façon plus concise sans perdre d'information.
Tableau Markdown = Convertis en tableau Markdown.
";

        public static void Load(string d)
        {
            dir = d;
            string path = Path.Combine(dir, "HoldKeys.ai.txt");
            try
            {
                if (!File.Exists(path)) File.WriteAllText(path, "# Actions IA du collage avancé : Nom = instruction\r\n" + DefaultActions, new UTF8Encoding(true));
                var list = new List<KeyValuePair<string, string>>();
                foreach (string l in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (l.StartsWith("#")) continue;
                    int eq = l.IndexOf(" = ");
                    if (eq > 0) list.Add(new KeyValuePair<string, string>(l.Substring(0, eq).Trim(), l.Substring(eq + 3).Trim()));
                }
                Actions = list;
            }
            catch { }
        }

        public static void SaveActions(List<KeyValuePair<string, string>> actions)
        {
            var sb = new StringBuilder("# Actions IA du collage avancé : Nom = instruction\r\n");
            foreach (var kv in actions) sb.Append(kv.Key).Append(" = ").Append(kv.Value.Replace("\r", " ").Replace("\n", " ")).Append("\r\n");
            File.WriteAllText(Path.Combine(dir, "HoldKeys.ai.txt"), sb.ToString(), new UTF8Encoding(true));
            Actions = actions;
        }

        static string KeyPath { get { return Path.Combine(dir, "HoldKeys.ai.dat"); } }

        public static string ApiKey
        {
            get
            {
                try { return File.Exists(KeyPath) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), null, DataProtectionScope.CurrentUser)) : ""; }
                catch { return ""; }
            }
            set
            {
                if (string.IsNullOrEmpty(value)) { try { File.Delete(KeyPath); } catch { } return; }
                File.WriteAllBytes(KeyPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
            }
        }

        public static bool Configured { get { return dir != null && ApiKey.Length > 0; } }

        const string System_ =
            "Tu transformes le contenu du presse-papiers de l'utilisateur selon son instruction. " +
            "Réponds uniquement avec le résultat final, prêt à être collé : pas d'introduction, pas d'explication, " +
            "pas de bloc de code Markdown autour, sauf si l'instruction le demande explicitement. " +
            "Conserve la langue du contenu sauf si l'instruction demande une traduction.";

        // Appel non bloquant : le résultat revient sur le fil de l'interface.
        public static void Ask(string instruction, string text, byte[] png, Action<string> ok, Action<string> fail)
        {
            string key = ApiKey, model = Model;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result = null, error = null;
                try { result = Call(key, model, instruction, text, png); }
                catch (Exception ex) { error = ex.Message; }
                TrayContext.Ui(() => { if (error != null) fail(error); else ok(result); });
            });
        }

        static string Call(string key, string model, string instruction, string text, byte[] png)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var content = new List<object>();
            if (png != null)
                content.Add(new Dictionary<string, object> { { "type", "image" }, { "source", new Dictionary<string, object> { { "type", "base64" }, { "media_type", "image/png" }, { "data", Convert.ToBase64String(png) } } } });
            content.Add(new Dictionary<string, object>
            {
                { "type", "text" },
                { "text", "Instruction : " + instruction + (text != null ? "\n\nContenu du presse-papiers :\n<contenu>\n" + text + "\n</contenu>" : "\n\n(Le contenu est l'image ci-dessus.)") }
            });
            var body = new Dictionary<string, object>
            {
                { "model", model },
                { "max_tokens", 16000 },
                { "system", System_ },
                { "output_config", new Dictionary<string, object> { { "effort", "low" } } }, // transformation courte : réponse rapide
                { "messages", new object[] { new Dictionary<string, object> { { "role", "user" }, { "content", content } } } }
            };
            bool fallback = !model.Contains("haiku"); // repli serveur en cas de refus (non disponible sur Haiku)
            if (fallback) body["fallbacks"] = "default";

            var req = (HttpWebRequest)WebRequest.Create("https://api.anthropic.com/v1/messages");
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = 180000;
            req.Headers["x-api-key"] = key;
            req.Headers["anthropic-version"] = "2023-06-01";
            if (fallback) req.Headers["anthropic-beta"] = "server-side-fallback-2026-07-01";
            byte[] data = Encoding.UTF8.GetBytes(js.Serialize(body));
            using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);

            string json;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = rd.ReadToEnd();
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                if (r == null) throw new Exception("Connexion à l'API Claude impossible : " + ex.Message);
                string err;
                using (var rd = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) err = rd.ReadToEnd();
                string msg = err;
                try
                {
                    var e = js.DeserializeObject(err) as Dictionary<string, object>;
                    var inner = e != null && e.ContainsKey("error") ? e["error"] as Dictionary<string, object> : null;
                    if (inner != null && inner.ContainsKey("message")) msg = inner["message"] as string;
                }
                catch { }
                int code = (int)r.StatusCode;
                if (code == 401) msg = "Clé API invalide (vérifiez-la dans Paramètres > Collage avancé).";
                else if (code == 429) msg = "Limite de requêtes atteinte, réessayez dans un instant.";
                throw new Exception("API Claude (" + code + ") : " + msg);
            }

            var res = js.DeserializeObject(json) as Dictionary<string, object>;
            if (res == null) throw new Exception("Réponse inattendue de l'API Claude.");
            if ((res.ContainsKey("stop_reason") ? res["stop_reason"] as string : null) == "refusal")
                throw new Exception("Claude a refusé cette demande.");
            var sb = new StringBuilder();
            var blocks = res.ContainsKey("content") ? res["content"] as object[] : null;
            if (blocks != null)
                foreach (var b in blocks.OfType<Dictionary<string, object>>())
                    if ((b["type"] as string) == "text") sb.Append(b["text"] as string);
            string text2 = sb.ToString().Trim();
            if (text2.Length == 0) throw new Exception("L'IA n'a renvoyé aucun texte.");
            return text2;
        }
    }

    // ---------- Module du lanceur ----------

    class AdvancedPasteProvider : Provider
    {
        public override string Prefix { get { return "+"; } }
        public override bool Global { get { return false; } }
        public override string HelpTitle { get { return "+  Collage avancé"; } }
        public override string HelpText { get { return "texte brut, Markdown, JSON, fichier, texte d'une image, IA… · raccourci Win+Shift+V"; } }

        static string Preview(string s)
        {
            string one = Regex.Replace(s.Trim(), @"\s+", " ");
            return one.Length > 90 ? one.Substring(0, 90) + "…" : one;
        }

        Result R(string title, string sub, string glyph, Action run, string altLabel = null, Action alt = null)
        {
            return new Result
            {
                Title = title, Subtitle = sub, Glyph = glyph, EnterLabel = "Coller",
                Run = run, NeedsTarget = true, AltLabel = altLabel, AltRun = alt
            };
        }

        static Action Copy(Func<string> f) { return () => { string s = f(); if (s != null) { Input.Copy(s); TrayContext.Notify("Résultat copié dans le presse-papiers."); } }; }

        public override List<Result> Query(string q, bool prefixed)
        {
            var res = new List<Result>();
            string text = null, html = null;
            Image img = null;
            StringCollection files = null;
            try
            {
                if (Clipboard.ContainsText()) text = Clipboard.GetText();
                if (Clipboard.ContainsData(DataFormats.Html)) html = Convert2.HtmlFragment(Clipboard.GetData(DataFormats.Html) as string);
                if (Clipboard.ContainsImage()) img = Clipboard.GetImage();
                if (Clipboard.ContainsFileDropList()) files = Clipboard.GetFileDropList();
            }
            catch { }

            if (text != null)
            {
                string t = text;
                res.Add(R("Coller en texte brut", "« " + Preview(t) + " » · " + t.Length + " caractères", "T", () => PasteOps.PasteText(t), "Copier en texte brut", Copy(() => t)));
                if (!string.IsNullOrEmpty(html))
                {
                    string h = html;
                    res.Add(R("Coller en Markdown", "Mise en forme convertie : titres, gras, liens, listes, tableaux", "M↓", () => PasteOps.PasteText(Convert2.HtmlToMarkdown(h)), "Copier le Markdown", Copy(() => Convert2.HtmlToMarkdown(h))));
                }
                string how;
                string json = Convert2.ToJson(t, out how);
                if (json != null)
                {
                    res.Add(R("Coller en JSON", how, "{ }", () => PasteOps.PasteText(json), "Copier le JSON", Copy(() => json)));
                    object v;
                    if (Convert2.TryParseJson(t, out v)) { string min = Convert2.Minify(v); res.Add(R("Coller en JSON minifié", "Sur une seule ligne · " + min.Length + " caractères", "{ }", () => PasteOps.PasteText(min), "Copier", Copy(() => min))); }
                }
                res.Add(R("Coller comme fichier .txt", "Crée un fichier texte (explorateur, messagerie, pièce jointe…)", "📄", () => { string p = PasteOps.TempFile(".txt"); File.WriteAllText(p, t, new UTF8Encoding(true)); PasteOps.PasteFile(p); },
                    "Enregistrer dans Téléchargements", () => { string p = PasteOps.TempFile(".txt"); File.WriteAllText(p, t, new UTF8Encoding(true)); PasteOps.SaveToDownloads(p); }));
                if (!string.IsNullOrEmpty(html))
                {
                    string h = html;
                    res.Add(R("Coller comme fichier .html", "Conserve la mise en forme", "📄", () => { string p = PasteOps.TempFile(".html"); File.WriteAllText(p, "<!doctype html><meta charset=\"utf-8\">" + h, new UTF8Encoding(false)); PasteOps.PasteFile(p); },
                        "Enregistrer dans Téléchargements", () => { string p = PasteOps.TempFile(".html"); File.WriteAllText(p, "<!doctype html><meta charset=\"utf-8\">" + h, new UTF8Encoding(false)); PasteOps.SaveToDownloads(p); }));
                    res.Add(R("Coller comme fichier .md", "Markdown converti depuis la mise en forme", "📄", () => { string p = PasteOps.TempFile(".md"); File.WriteAllText(p, Convert2.HtmlToMarkdown(h), new UTF8Encoding(false)); PasteOps.PasteFile(p); },
                        "Enregistrer dans Téléchargements", () => { string p = PasteOps.TempFile(".md"); File.WriteAllText(p, Convert2.HtmlToMarkdown(h), new UTF8Encoding(false)); PasteOps.SaveToDownloads(p); }));
                }
                foreach (var a in TextActionsProvider.All.Where(x => x.T != null))
                {
                    var act = a;
                    res.Add(R(a.PasteTitle, "Transformation du texte copié", "Aa", () => PasteOps.PasteText(act.T(t)), "Copier le résultat", Copy(() => act.T(t))));
                }
            }

            if (img != null)
            {
                byte[] png = PasteOps.Png(img);
                string size = img.Width + " × " + img.Height + " px";
                res.Add(R("Coller le texte de l'image (OCR)", "Reconnaissance de texte de Windows, hors ligne · image " + size, "🔍", () => RunOcr(png, true), "Copier le texte", () => RunOcr(png, false)));
                res.Add(R("Coller comme fichier .png", "Image " + size, "🖼", () => { string p = PasteOps.TempFile(".png"); File.WriteAllBytes(p, png); PasteOps.PasteFile(p); },
                    "Enregistrer dans Téléchargements", () => { string p = PasteOps.TempFile(".png"); File.WriteAllBytes(p, png); PasteOps.SaveToDownloads(p); }));
                string b64 = "data:image/png;base64," + Convert.ToBase64String(png);
                res.Add(R("Coller en Base64 (data URI)", "Pour HTML/CSS · " + (b64.Length / 1024) + " Ko", "🖼", () => PasteOps.PasteText(b64), "Copier", Copy(() => b64)));
            }

            if (files != null && files.Count > 0)
            {
                var list = files.Cast<string>().ToList();
                string paths = string.Join(Environment.NewLine, list);
                res.Add(R("Coller les chemins", list.Count + " élément(s) · " + Preview(paths), "📁", () => PasteOps.PasteText(paths), "Copier les chemins", Copy(() => paths)));
                string names = string.Join(Environment.NewLine, list.Select(Path.GetFileName));
                res.Add(R("Coller les noms de fichiers", Preview(names), "📁", () => PasteOps.PasteText(names), "Copier", Copy(() => names)));
                if (list.Count == 1 && File.Exists(list[0]) && new FileInfo(list[0]).Length < 2000000)
                {
                    string f = list[0];
                    res.Add(R("Coller le contenu du fichier", Path.GetFileName(f), "📄", () => PasteOps.PasteText(File.ReadAllText(f)), "Copier le contenu", Copy(() => File.ReadAllText(f))));
                }
            }

            // IA
            bool hasContent = text != null || img != null;
            if (hasContent && !Claude.Configured)
            {
                res.Add(new Result
                {
                    Title = "Actions IA (Claude) : corriger, traduire, résumer, instruction libre…",
                    Subtitle = "Optionnel · ajoutez votre clé API dans Paramètres > Collage avancé",
                    Glyph = "✦", EnterLabel = "Configurer",
                    Run = () => { TrayContext.Instance.ShowSettings(); TrayContext.Instance.SettingsTab(3); }
                });
            }
            else if (hasContent)
            {
                string t = text;
                byte[] png = img != null && text == null ? PasteOps.Png(img) : null;
                foreach (var kv in Claude.Actions)
                {
                    string instr = kv.Value;
                    res.Add(AiResult(kv.Key, instr, t, png, "IA · " + instr));
                }
                if (q.Length > 0) res.Add(AiResult("Avec l'IA : " + q, q, t, png, "Instruction libre envoyée à Claude avec le contenu copié", true));
            }

            if (res.Count == 0)
                res.Add(new Result { Title = "Le presse-papiers est vide", Subtitle = "Copiez du texte, une image ou des fichiers, puis rouvrez le collage avancé", Glyph = "📋" });

            // Filtre par la saisie (l'action IA libre reste toujours proposée).
            if (q.Length > 0)
            {
                var filtered = new List<Result>();
                foreach (var r in res)
                {
                    int s = r.Title.StartsWith("Avec l'IA") || r.Title.StartsWith("Actions IA") ? 1 : Matcher.Score(q, r.Title);
                    if (s > 0) { r.Score = s; filtered.Add(r); }
                }
                return filtered;
            }
            for (int i = 0; i < res.Count; i++) res[i].Score = 1000 - i;
            return res;
        }

        Result AiResult(string title, string instruction, string text, byte[] png, string sub, bool free = false)
        {
            return new Result
            {
                Title = title, Subtitle = sub, Glyph = "✦", EnterLabel = "Coller",
                Run = () => RunAi(instruction, text, png, true), NeedsTarget = true,
                AltLabel = "Copier le résultat", AltRun = () => RunAi(instruction, text, png, false)
            };
        }

        static void RunAi(string instruction, string text, byte[] png, bool paste)
        {
            IntPtr target = Native.GetForegroundWindow();
            TrayContext.Notify("✦ Claude prépare le résultat…");
            Claude.Ask(instruction, text, png, result => Deliver(result, target, paste), err => TrayContext.Notify("IA : " + err));
        }

        static void RunOcr(byte[] png, bool paste)
        {
            IntPtr target = Native.GetForegroundWindow();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string text = null, err = null;
                try { text = Ocr.Recognize(png); } catch (Exception ex) { err = ex.Message; }
                TrayContext.Ui(() =>
                {
                    if (err != null) TrayContext.Notify("OCR : " + err);
                    else if (string.IsNullOrWhiteSpace(text)) TrayContext.Notify("Aucun texte détecté dans l'image.");
                    else Deliver(text, target, paste);
                });
            });
        }

        // Colle dans la fenêtre d'origine si elle est toujours active, sinon copie le résultat.
        static void Deliver(string result, IntPtr target, bool paste)
        {
            if (paste && Native.GetForegroundWindow() == target) PasteOps.PasteText(result);
            else
            {
                Input.Copy(result);
                TrayContext.Notify(paste ? "Résultat copié (la fenêtre d'origine n'est plus active)." : "Résultat copié dans le presse-papiers.");
            }
        }
    }
}
