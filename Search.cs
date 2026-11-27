// Recherche de symboles par nom : base de symboles, fenêtre de recherche et raccourci global.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace HoldKeys
{
    class Sym
    {
        public string Ch, Name, Norm;
        public int Order;
    }

    static class SymbolDb
    {
        // Format : caractère (ou U+XXXX) puis nom et mots-clés. Les lettres accentuées sont générées à part.
        const string Curated = @"
« guillemet ouvrant français, chevron quote
» guillemet fermant français, chevron quote
“ guillemet anglais ouvrant, quote
” guillemet anglais fermant, quote
‘ apostrophe ouvrante, quote
’ apostrophe typographique, quote
„ guillemet bas allemand, quote
‹ guillemet simple ouvrant
› guillemet simple fermant
— tiret cadratin, em dash
– tiret demi-cadratin, en dash
‑ trait d'union insécable, hyphen
… points de suspension, ellipsis
· point médian, interpunct, écriture inclusive
• puce, bullet
‣ puce triangulaire, bullet
¡ point d'exclamation inversé, espagnol
¿ point d'interrogation inversé, espagnol
§ paragraphe, section
¶ pied de mouche, pilcrow, alinéa
† croix, obèle, dagger
‡ double croix, double dagger
‰ pour mille, per mille
‱ pour dix mille
′ prime, minute
″ double prime, seconde
° degré, degree
№ numéro, number
U+00A0 espace insécable, nbsp, space
U+202F espace fine insécable, space
U+200B espace sans chasse, zero width space
€ euro, monnaie, currency
£ livre sterling, pound, monnaie
¥ yen yuan, monnaie
¢ cent, monnaie
$ dollar, monnaie
₹ roupie indienne, rupee, monnaie
₽ rouble, monnaie
₩ won, monnaie
₺ livre turque, monnaie
₿ bitcoin, monnaie
₣ franc, monnaie
¤ symbole monétaire, currency
© copyright, droit d'auteur
® marque déposée, registered
™ marque commerciale, trademark
℗ droit phonographique
± plus ou moins, plus minus, math
∓ moins ou plus, math
× multiplication, fois, times, math
÷ division, divisé, math
≠ différent, not equal, math
≈ environ égal, approximativement, math
≡ identique, équivalent, math
≤ inférieur ou égal, less equal, math
≥ supérieur ou égal, greater equal, math
≪ très inférieur, math
≫ très supérieur, math
∞ infini, infinity, math
√ racine carrée, square root, math
∛ racine cubique, math
∑ somme, sigma, sum, math
∏ produit, product, math
∫ intégrale, integral, math
∂ dérivée partielle, math
∆ delta, différence, math
∇ nabla, math
∈ appartient à, element, math
∉ n'appartient pas, math
∅ ensemble vide, empty, math
∩ intersection, math
∪ union, math
⊂ inclus dans, subset, math
⊃ contient, superset, math
∀ pour tout, for all, math
∃ il existe, exists, math
¬ négation, not, logique
∧ et logique, and
∨ ou logique, or
⊕ ou exclusif, xor
∴ donc, therefore
∵ parce que, because
∝ proportionnel, math
∠ angle, math
⊥ perpendiculaire, math
∥ parallèle, math
ℕ entiers naturels, math
ℤ entiers relatifs, math
ℚ nombres rationnels, math
ℝ nombres réels, math
ℂ nombres complexes, math
µ micro, mu
Ω ohm, oméga majuscule
℃ degré celsius, température
℉ degré fahrenheit, température
½ un demi, fraction
⅓ un tiers, fraction
⅔ deux tiers, fraction
¼ un quart, fraction
¾ trois quarts, fraction
⅛ un huitième, fraction
⁰ exposant zéro, superscript
¹ exposant un, superscript
² exposant deux, carré, superscript
³ exposant trois, cube, superscript
⁴ exposant quatre, superscript
⁵ exposant cinq, superscript
⁶ exposant six, superscript
⁷ exposant sept, superscript
⁸ exposant huit, superscript
⁹ exposant neuf, superscript
⁺ exposant plus, superscript
⁻ exposant moins, superscript
ⁿ exposant n, superscript
₀ indice zéro, subscript
₁ indice un, subscript
₂ indice deux, subscript
₃ indice trois, subscript
₄ indice quatre, subscript
ª ordinal féminin
º ordinal masculin
α alpha, grec greek
β bêta, grec greek
γ gamma, grec greek
δ delta, grec greek
ε epsilon, grec greek
ζ zêta, grec greek
η êta, grec greek
θ thêta, grec greek
λ lambda, grec greek
μ mu, grec greek
π pi, grec greek
ρ rhô, grec greek
σ sigma, grec greek
τ tau, grec greek
φ phi, grec greek
χ chi, grec greek
ψ psi, grec greek
ω oméga, grec greek
Γ gamma majuscule, grec greek
Δ delta majuscule, grec greek
Θ thêta majuscule, grec greek
Λ lambda majuscule, grec greek
Π pi majuscule, grec greek
Σ sigma majuscule, grec greek
Φ phi majuscule, grec greek
Ψ psi majuscule, grec greek
← flèche gauche, arrow left
→ flèche droite, arrow right
↑ flèche haut, arrow up
↓ flèche bas, arrow down
↔ flèche gauche droite, arrow
↕ flèche haut bas, arrow
⇐ double flèche gauche, arrow
⇒ double flèche droite, implique, implies, arrow
⇔ double flèche gauche droite, équivalent, iff, arrow
↗ flèche haut droite, arrow
↘ flèche bas droite, arrow
↙ flèche bas gauche, arrow
↖ flèche haut gauche, arrow
↩ flèche retour, arrow
↪ flèche retour droite, arrow
⟶ longue flèche droite, arrow
➜ flèche épaisse droite, arrow
■ carré plein, square
□ carré vide, square
▪ petit carré plein, square
● cercle plein, rond, circle
○ cercle vide, rond, circle
◆ losange plein, diamond
◇ losange vide, diamond
▲ triangle haut
▼ triangle bas
► triangle droite
◄ triangle gauche
★ étoile pleine, star
☆ étoile vide, star
✓ coche, check, valide, ok
✔ coche épaisse, check, valide
✗ croix, cross, faux
✘ croix épaisse, cross, faux
☐ case vide, checkbox
☑ case cochée, checkbox
☒ case barrée, checkbox
♥ cœur, heart, carte
♦ carreau, carte
♣ trèfle, carte
♠ pique, carte
♪ note de musique, music
♫ notes de musique, music
☀ soleil, sun, météo
☁ nuage, cloud, météo
☂ parapluie, pluie, météo
❄ flocon, neige, météo
⚡ éclair, orage
☎ téléphone, phone
✉ enveloppe, mail, courrier
✂ ciseaux
✈ avion, plane
✏ crayon
⚠ attention, avertissement, warning
☺ visage souriant, smiley
☹ visage triste
☯ yin yang
☮ paix, peace
♀ femme, féminin
♂ homme, masculin
⌘ touche commande mac
⌥ touche option mac
⇧ touche majuscule, shift
⏎ touche entrée, enter
⌫ touche effacer, backspace
⌛ sablier
☕ café, coffee
❤ cœur rouge, heart
😀 grand sourire, emoji
😂 rire aux larmes, emoji
😊 sourire, emoji
😉 clin d'œil, emoji
😍 yeux en cœur, emoji
😎 lunettes de soleil, emoji
🤔 réfléchit, emoji
😢 pleure, triste, emoji
😡 en colère, emoji
👍 pouce levé, ok, like, emoji
👎 pouce baissé, emoji
👏 applaudissements, bravo, emoji
🙏 merci, prière, emoji
💪 muscle, force, emoji
👋 salut, coucou, main, emoji
🎉 fête, bravo, confettis, emoji
🔥 feu, fire, emoji
💯 cent points, emoji
✅ coche verte, ok, emoji
❌ croix rouge, emoji
⭐ étoile, emoji
🚀 fusée, emoji
💡 idée, ampoule, emoji
📌 punaise, emoji
📎 trombone, emoji
æ e dans l'a, ae, ligature
Æ e dans l'a majuscule, ae, ligature
œ e dans l'o, oe, ligature
Œ e dans l'o majuscule, oe, ligature
ß eszett, s dur allemand, ss
ø o barré
Ø o barré majuscule, diamètre
ð eth
Ð eth majuscule
þ thorn
Þ thorn majuscule
ł l barré
Ł l barré majuscule
đ d barré
Đ d barré majuscule
ı i sans point
ŋ eng
";

        static readonly Dictionary<int, string> Marks = new Dictionary<int, string>
        {
            { 0x0300, "accent grave" }, { 0x0301, "accent aigu" }, { 0x0302, "accent circonflexe" },
            { 0x0303, "tilde" }, { 0x0304, "macron" }, { 0x0306, "brève" }, { 0x0307, "point suscrit" },
            { 0x0308, "tréma" }, { 0x030A, "rond en chef" }, { 0x030B, "double accent aigu" },
            { 0x030C, "caron" }, { 0x0327, "cédille" }, { 0x0328, "ogonek" }
        };

        public static List<Sym> Build(IEnumerable<KeyValuePair<string, string[]>> configEntries)
        {
            var list = new List<Sym>();
            var seen = new HashSet<string>();
            Action<string, string> add = (ch, name) =>
            {
                if (string.IsNullOrEmpty(ch) || !seen.Add(ch)) return;
                list.Add(new Sym { Ch = ch, Name = name, Norm = Normalize(name + " " + ch), Order = list.Count });
            };

            // Lettres accentuées (Latin-1 et Latin étendu A), nommées à partir de leur décomposition Unicode.
            for (int cp = 0x00C0; cp <= 0x017F; cp++)
            {
                string s = ((char)cp).ToString();
                string d = s.Normalize(NormalizationForm.FormD);
                if (d.Length < 2 || d[0] > 'z' || !char.IsLetter(d[0])) continue;
                var parts = new List<string>();
                bool ok = true;
                for (int i = 1; i < d.Length; i++)
                {
                    string m;
                    if (Marks.TryGetValue(d[i], out m)) parts.Add(m); else ok = false;
                }
                if (!ok) continue;
                bool upper = char.IsUpper(d[0]);
                add(s, char.ToLowerInvariant(d[0]) + (upper ? " majuscule " : " ") + string.Join(" ", parts) + ", accent lettre");
            }

            foreach (string raw in Curated.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int sp = line.IndexOf(' ');
                if (sp <= 0) continue;
                string ch = line.Substring(0, sp);
                if (ch.StartsWith("U+")) ch = char.ConvertFromUtf32(Convert.ToInt32(ch.Substring(2), 16));
                add(ch, line.Substring(sp + 1).Trim());
            }

            // Caractères de la configuration qui ne sont pas encore connus.
            foreach (var e in configEntries)
                foreach (string ch in e.Value)
                    add(ch, "touche " + e.Key + ", personnalisé");
            return list;
        }

        public static string Normalize(string s)
        {
            string d = s.ToLowerInvariant().Replace("œ", "oe").Replace("æ", "ae").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char c in d)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString();
        }

        public static string NameOf(List<Sym> db, string ch)
        {
            var s = db.Find(x => x.Ch == ch);
            if (s != null) return s.Name;
            return "U+" + char.ConvertToUtf32(ch, 0).ToString("X4");
        }

        public static List<Sym> Search(List<Sym> db, string query, IList<string> recent)
        {
            string q = query.Trim();
            var res = new List<KeyValuePair<int, Sym>>();
            if (q.Length == 0)
            {
                var top = new List<Sym>();
                foreach (string r in recent)
                {
                    var s = db.Find(x => x.Ch == r) ?? new Sym { Ch = r, Name = NameOf(db, r) };
                    top.Add(s);
                }
                foreach (var s in db) if (!recent.Contains(s.Ch)) top.Add(s);
                return top;
            }
            string[] words = Normalize(q).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var s in db)
            {
                if (s.Ch == q) { res.Add(new KeyValuePair<int, Sym>(-100, s)); continue; }
                int score = 0;
                bool all = true;
                string[] nameWords = s.Norm.Split(new[] { ' ', ',', '\'' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string w in words)
                {
                    int best = -1;
                    foreach (string nw in nameWords)
                    {
                        int sc = nw == w ? 0 : nw.StartsWith(w) ? 1 : -1;
                        if (sc >= 0 && (best < 0 || sc < best)) best = sc;
                    }
                    if (best < 0) best = s.Norm.Contains(w) ? 3 : -1;
                    if (best < 0) { all = false; break; }
                    score += best;
                }
                if (!all) continue;
                if (s.Norm.StartsWith(words[0])) score -= 2;
                if (s.Norm.Contains("majuscule") && Array.IndexOf(words, "majuscule") < 0) score += 1;
                if (recent.Contains(s.Ch)) score -= 1;
                res.Add(new KeyValuePair<int, Sym>(score, s));
            }
            res.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.Order.CompareTo(b.Value.Order));
            return res.ConvertAll(x => x.Value);
        }
    }

    // ---------- Raccourci clavier global ----------

    class HotkeyWindow : NativeWindow
    {
        public event Action Pressed, ClipboardChanged;
        bool registered;

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        public void ListenClipboard() { Native.AddClipboardFormatListener(Handle); }

        public bool Register(string spec)
        {
            Unregister();
            uint mod, vk;
            if (string.IsNullOrEmpty(spec) || !Hotkey.TryParse(spec, out mod, out vk)) return false;
            registered = Native.RegisterHotKey(Handle, 1, mod | 0x4000 /*NOREPEAT*/, vk);
            return registered;
        }

        public void Unregister()
        {
            if (registered) Native.UnregisterHotKey(Handle, 1);
            registered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 /*WM_HOTKEY*/ && Pressed != null) Pressed();
            if (m.Msg == 0x031D /*WM_CLIPBOARDUPDATE*/ && ClipboardChanged != null) ClipboardChanged();
            base.WndProc(ref m);
        }
    }

    static class Hotkey
    {
        public static bool TryParse(string spec, out uint mod, out uint vk)
        {
            mod = 0; vk = 0;
            string[] parts = spec.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                string l = p.ToLowerInvariant();
                if (i < parts.Length - 1)
                {
                    if (l == "ctrl" || l == "control") mod |= 2;
                    else if (l == "alt") mod |= 1;
                    else if (l == "shift" || l == "maj") mod |= 4;
                    else if (l == "win") mod |= 8;
                    else return false;
                }
                else
                {
                    Keys k;
                    if (!Enum.TryParse(p, true, out k)) return false;
                    vk = (uint)k;
                }
            }
            return vk != 0;
        }

        public static string Format(Keys key, bool ctrl, bool alt, bool shift, bool win)
        {
            var sb = new StringBuilder();
            if (ctrl) sb.Append("Ctrl+");
            if (alt) sb.Append("Alt+");
            if (shift) sb.Append("Shift+");
            if (win) sb.Append("Win+");
            sb.Append(key.ToString());
            return sb.ToString();
        }
    }
}
