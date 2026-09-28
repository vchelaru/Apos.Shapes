// The rules out of a document's style elements, and which of them an element picks up.

using System;
using System.Collections.Generic;

namespace Apos.Shapes {
    // A stylesheet cut down to what a static drawing can use. Selectors may use tags, classes,
    // ids, attribute tests and :first-child, joined by descendant, child and sibling combinators.
    // @media blocks apply when they're for all or screen. Anything else is counted and left out,
    // so a file never loads worse than it did without the sheet. @import is never followed: it
    // would read a file or reach the network on behalf of whoever handed over the document.
    //
    // Everything resolves while the file loads, into the same style an inline declaration would
    // have produced, so a drawing styled from a sheet draws exactly like one styled inline.
    internal sealed class SvgSheet {
        private readonly List<Rule> _rules = new();
        // What was left out, the same statistic ShapeSvg keeps for elements.
        internal int Skipped;
        internal readonly List<string> SkippedNames = new();

        internal bool IsEmpty => _rules.Count == 0;

        private sealed class Rule {
            // Right to left: Parts[0] is the element itself, each later part is reached from the
            // one before it through that part's Combinator.
            internal Compound[] Parts = null!;
            internal int Specificity;
            internal int Order;
            internal Dictionary<string, string> Declarations = null!;
        }

        private sealed class Compound {
            internal string? Tag;
            internal string? Id;
            internal readonly List<string> Classes = new();
            internal readonly List<AttrTest> Attrs = new();
            internal bool FirstChild;
            // How the element this part matches relates to the part before it: ' ' ancestor,
            // '>' parent, '~' earlier sibling, '+' the sibling right before.
            internal char Combinator;
        }

        private readonly struct AttrTest {
            internal AttrTest(string name, char op, string? value) {
                Name = name;
                Op = op;
                Value = value;
            }
            internal readonly string Name;
            // '\0' for presence, '=' for equality, or the first character of ~= |= ^= $= *=.
            internal readonly char Op;
            internal readonly string? Value;
        }

        internal void Add(string css) {
            css = Uncomment(css);
            int at = 0;
            while (at < css.Length) {
                char c = css[at];
                if (char.IsWhiteSpace(c)) {
                    at++;
                    continue;
                }
                if (c == '@') {
                    // An at-rule ends at its semicolon, or with its block when it has one.
                    int semi = css.IndexOf(';', at);
                    int open = css.IndexOf('{', at);
                    if (open >= 0 && (semi < 0 || open < semi)) {
                        int close = BlockEnd(css, open);
                        string prelude = css.Substring(at, open - at);
                        if (IsScreenMedia(prelude)) {
                            Add(css.Substring(open + 1, Math.Max(0, close - open - 1)));
                        } else {
                            Skip("@rule");
                        }
                        at = close + 1;
                    } else {
                        at = semi < 0 ? css.Length : semi + 1;
                        Skip("@rule");
                    }
                    continue;
                }
                int brace = css.IndexOf('{', at);
                if (brace < 0) break;
                int end = BlockEnd(css, brace);
                string selectors = css.Substring(at, brace - at);
                var decls = new Dictionary<string, string>(StringComparer.Ordinal);
                Declarations(css.Substring(brace + 1, Math.Max(0, end - brace - 1)), decls);
                if (decls.Count > 0) {
                    foreach (string selector in selectors.Split(',')) {
                        Rule? rule = Selector(selector.Trim());
                        if (rule == null) {
                            Skip("css selector");
                            continue;
                        }
                        rule.Order = _rules.Count;
                        rule.Declarations = decls;
                        _rules.Add(rule);
                    }
                }
                at = end + 1;
            }
        }

        // What the sheet says for one element, with the more specific rule winning and the later
        // one winning a tie. Null when nothing matches.
        internal Dictionary<string, string>? Match(SvgAttrs element) {
            if (_rules.Count == 0) return null;
            List<Rule>? hits = null;
            foreach (Rule rule in _rules) {
                if (!Matches(rule.Parts, 0, element)) continue;
                hits ??= new List<Rule>();
                hits.Add(rule);
            }
            if (hits == null) return null;
            hits.Sort((a, b) => a.Specificity != b.Specificity
                ? a.Specificity.CompareTo(b.Specificity)
                : a.Order.CompareTo(b.Order));
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Rule rule in hits) {
                foreach (KeyValuePair<string, string> d in rule.Declarations) result[d.Key] = d.Value;
            }
            return result;
        }

        // A declaration list, the body of a rule or a style attribute. !important is dropped: it
        // only matters when two sources disagree, and the order they're layered in already
        // settles that for anything a drawing tool writes.
        internal static void Declarations(string text, Dictionary<string, string> into) {
            int at = 0;
            while (at < text.Length) {
                int end = text.IndexOf(';', at);
                if (end < 0) end = text.Length;
                int colon = text.IndexOf(':', at);
                if (colon > at && colon < end) {
                    string name = text.Substring(at, colon - at).Trim();
                    string value = text.Substring(colon + 1, end - colon - 1).Trim();
                    int bang = value.IndexOf('!');
                    if (bang >= 0) value = value.Substring(0, bang).TrimEnd();
                    if (name.Length > 0) into[name] = value;
                }
                at = end + 1;
            }
        }

        // Whether parts[index..] match with parts[index] on this element. A descendant or later
        // sibling combinator tries every candidate, so a.b c matches however deep the a.b is.
        private static bool Matches(Compound[] parts, int index, SvgAttrs element) {
            Compound part = parts[index];
            if (!Matches(part, element)) return false;
            if (index + 1 == parts.Length) return true;
            switch (parts[index + 1].Combinator) {
                case '>':
                    return element.Parent != null && Matches(parts, index + 1, element.Parent);
                case '+':
                    return element.Previous != null && Matches(parts, index + 1, element.Previous);
                case '~':
                    for (SvgAttrs? s = element.Previous; s != null; s = s.Previous) {
                        if (Matches(parts, index + 1, s)) return true;
                    }
                    return false;
                default:
                    for (SvgAttrs? a = element.Parent; a != null; a = a.Parent) {
                        if (Matches(parts, index + 1, a)) return true;
                    }
                    return false;
            }
        }

        private static bool Matches(Compound part, SvgAttrs element) {
            if (part.Tag != null && !string.Equals(part.Tag, element.Tag, StringComparison.Ordinal)) return false;
            if (part.Id != null && !string.Equals(part.Id, element.Raw("id"), StringComparison.Ordinal)) return false;
            if (part.FirstChild && element.Previous != null) return false;
            foreach (string c in part.Classes) {
                if (Array.IndexOf(element.Classes, c) < 0) return false;
            }
            foreach (AttrTest t in part.Attrs) {
                string? v = element.Raw(t.Name);
                if (v == null || !Test(t, v)) return false;
            }
            return true;
        }

        private static bool Test(in AttrTest t, string v) {
            string want = t.Value ?? string.Empty;
            switch (t.Op) {
                case '\0': return true;
                case '=': return v == want;
                case '~': return Array.IndexOf(v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), want) >= 0;
                case '|': return v == want || v.StartsWith(want + "-", StringComparison.Ordinal);
                case '^': return want.Length > 0 && v.StartsWith(want, StringComparison.Ordinal);
                case '$': return want.Length > 0 && v.EndsWith(want, StringComparison.Ordinal);
                case '*': return want.Length > 0 && v.Contains(want, StringComparison.Ordinal);
                default: return false;
            }
        }

        // One complex selector, or null for anything this doesn't read.
        private static Rule? Selector(string s) {
            if (s.Length == 0) return null;
            var parts = new List<Compound>();
            int at = 0;
            char combinator = '\0';
            while (true) {
                Compound? part = ReadCompound(s, ref at);
                if (part == null) return null;
                part.Combinator = combinator;
                parts.Add(part);

                bool space = false;
                while (at < s.Length && char.IsWhiteSpace(s[at])) {
                    at++;
                    space = true;
                }
                if (at >= s.Length) break;
                char c = s[at];
                if (c == '>' || c == '+' || c == '~') {
                    combinator = c;
                    at++;
                    while (at < s.Length && char.IsWhiteSpace(s[at])) at++;
                } else if (space) {
                    combinator = ' ';
                } else {
                    return null;
                }
            }

            int ids = 0, classes = 0, tags = 0;
            foreach (Compound p in parts) {
                if (p.Id != null) ids++;
                classes += p.Classes.Count + p.Attrs.Count + (p.FirstChild ? 1 : 0);
                if (p.Tag != null) tags++;
            }
            // Matching runs from the element outward, so the parts are stored the same way round,
            // each holding the combinator that reaches it from the part before.
            var joins = new char[parts.Count];
            for (int i = 0; i < parts.Count; i++) joins[i] = parts[i].Combinator;
            var reversed = new Compound[parts.Count];
            for (int i = 0; i < parts.Count; i++) {
                reversed[i] = parts[parts.Count - 1 - i];
                reversed[i].Combinator = i == 0 ? '\0' : joins[parts.Count - i];
            }
            return new Rule { Parts = reversed, Specificity = ids * 10000 + classes * 100 + tags };
        }

        // A tag, classes, an id, attribute tests and :first-child written together, like
        // rect.a[fill]:first-child. Null for anything else.
        private static Compound? ReadCompound(string s, ref int at) {
            var part = new Compound();
            int start = at;
            if (at < s.Length && s[at] == '*') {
                at++;
            } else if (at < s.Length && IsName(s[at])) {
                part.Tag = Name(s, ref at);
            }
            while (at < s.Length) {
                char c = s[at];
                if (c == '.' || c == '#') {
                    at++;
                    string name = Name(s, ref at);
                    if (name.Length == 0) return null;
                    if (c == '.') {
                        part.Classes.Add(name);
                    } else if (part.Id == null) {
                        part.Id = name;
                    } else {
                        return null;
                    }
                } else if (c == '[') {
                    if (!Attribute(s, ref at, out AttrTest test)) return null;
                    part.Attrs.Add(test);
                } else if (c == ':') {
                    const string first = ":first-child";
                    if (string.CompareOrdinal(s, at, first, 0, first.Length) != 0) return null;
                    at += first.Length;
                    if (at < s.Length && IsName(s[at])) return null;
                    part.FirstChild = true;
                } else {
                    break;
                }
            }
            return at > start ? part : null;
        }

        // [name], [name=value] or [name op= value], with the value bare or quoted.
        private static bool Attribute(string s, ref int at, out AttrTest test) {
            test = default;
            int close = s.IndexOf(']', at);
            if (close < 0) return false;
            string body = s.Substring(at + 1, close - at - 1).Trim();
            at = close + 1;
            int eq = body.IndexOf('=');
            if (eq < 0) {
                if (body.Length == 0) return false;
                test = new AttrTest(body, '\0', null);
                return true;
            }
            char op = '=';
            int nameEnd = eq;
            if (eq > 0 && "~|^$*".IndexOf(body[eq - 1]) >= 0) {
                op = body[eq - 1];
                nameEnd = eq - 1;
            }
            string name = body.Substring(0, nameEnd).Trim();
            string value = body.Substring(eq + 1).Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0]) {
                value = value.Substring(1, value.Length - 2);
            } else if (value.Length == 0 || value.IndexOf(' ') >= 0 || value[0] == '"' || value[0] == '\'') {
                // A case flag like [a=b i], or a quote that never closes.
                return false;
            }
            if (name.Length == 0) return false;
            test = new AttrTest(name, op, value);
            return true;
        }

        // @media for every medium or for screens, with no feature tests. Print and anything with
        // a condition in it doesn't describe a drawing on a screen, so its rules are skipped.
        private static bool IsScreenMedia(string prelude) {
            const string media = "@media";
            if (!prelude.StartsWith(media, StringComparison.OrdinalIgnoreCase)) return false;
            foreach (string query in prelude.Substring(media.Length).Split(',')) {
                string q = query.Trim().ToLowerInvariant();
                if (q.StartsWith("only ", StringComparison.Ordinal)) q = q.Substring(5).Trim();
                if (q == "all" || q == "screen") return true;
            }
            return false;
        }

        private static string Name(string s, ref int at) {
            int start = at;
            while (at < s.Length && IsName(s[at])) at++;
            return s.Substring(start, at - start);
        }

        private static bool IsName(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c > 0x7f;

        private static int BlockEnd(string css, int open) {
            int depth = 0;
            for (int i = open; i < css.Length; i++) {
                if (css[i] == '{') depth++;
                else if (css[i] == '}' && --depth == 0) return i;
            }
            return css.Length;
        }

        private static string Uncomment(string css) {
            int open = css.IndexOf("/*", StringComparison.Ordinal);
            if (open < 0) return css;
            var sb = new System.Text.StringBuilder(css.Length);
            int at = 0;
            while (open >= 0) {
                sb.Append(css, at, open - at);
                int close = css.IndexOf("*/", open + 2, StringComparison.Ordinal);
                at = close < 0 ? css.Length : close + 2;
                open = at < css.Length ? css.IndexOf("/*", at, StringComparison.Ordinal) : -1;
            }
            if (at < css.Length) sb.Append(css, at, css.Length - at);
            return sb.ToString();
        }

        private void Skip(string what) {
            Skipped++;
            if (!SkippedNames.Contains(what)) SkippedNames.Add(what);
        }
    }
}
