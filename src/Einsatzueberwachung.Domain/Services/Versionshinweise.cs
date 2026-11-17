namespace Einsatzueberwachung.Domain.Services
{
    /// <summary>
    /// Zerlegt die Versionshinweise eines Releases (einfaches Markdown wie in den GitHub-Releases) in Blöcke,
    /// damit die Einstellungen sie ohne HTML-Einbettung anzeigen können: Überschriften, Listenpunkte, Absätze,
    /// darin **fett** und `Code`.
    /// </summary>
    public static class Versionshinweise
    {
        public enum BlockArt { Ueberschrift, Unterueberschrift, Punkt, Absatz }

        public sealed record Teil(string Text, bool Fett, bool Code);

        public sealed record Block(BlockArt Art, IReadOnlyList<Teil> Teile);

        public static List<Block> Zerlegen(string? markdown)
        {
            var bloecke = new List<Block>();
            foreach (var roh in (markdown ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                var zeile = roh.TrimEnd();
                if (zeile.Trim().Length == 0) continue;
                var text = zeile.TrimStart();
                if (text.StartsWith("### ")) bloecke.Add(new Block(BlockArt.Unterueberschrift, Inline(text[4..])));
                else if (text.StartsWith("## ") || text.StartsWith("# ")) bloecke.Add(new Block(BlockArt.Ueberschrift, Inline(text.TrimStart('#').Trim())));
                else if (text.StartsWith("- ") || text.StartsWith("* ")) bloecke.Add(new Block(BlockArt.Punkt, Inline(text[2..])));
                else bloecke.Add(new Block(BlockArt.Absatz, Inline(text)));
            }
            return bloecke;
        }

        /// <summary>**fett** und `Code`; alles andere bleibt Text</summary>
        public static List<Teil> Inline(string text)
        {
            var teile = new List<Teil>();
            var i = 0;
            var puffer = new System.Text.StringBuilder();
            void Leeren()
            {
                if (puffer.Length > 0) teile.Add(new Teil(puffer.ToString(), false, false));
                puffer.Clear();
            }
            while (i < text.Length)
            {
                if (text[i] == '`' && text.IndexOf('`', i + 1) is var ende && ende > i)
                {
                    Leeren();
                    teile.Add(new Teil(text[(i + 1)..ende], false, true));
                    i = ende + 1;
                }
                else if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*' && text.IndexOf("**", i + 2, StringComparison.Ordinal) is var f && f > i)
                {
                    Leeren();
                    teile.Add(new Teil(text[(i + 2)..f], true, false));
                    i = f + 2;
                }
                else
                {
                    puffer.Append(text[i]);
                    i++;
                }
            }
            Leeren();
            return teile;
        }
    }
}
