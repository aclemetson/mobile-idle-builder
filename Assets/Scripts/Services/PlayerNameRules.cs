using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MobileIdleBuilder
{
    /// <summary>Why a name was refused (or <see cref="Ok"/>).</summary>
    public enum NameCheck { Ok, Empty, TooShort, TooLong, BadCharacters, NoLetter, Blocked }

    /// <summary>
    /// Pure rules for the public player name shown on tournament leaderboards (PvE phase 1).
    /// The shape rules (length, characters) match what UGS Authentication accepts (no spaces). The word
    /// filter runs <c>game_config.name_filter_patterns</c> (regexes) over the lower-cased name and over a
    /// normalised copy: leetspeak mapped back to letters, separators dropped, letter runs collapsed, so
    /// "B4d_W0rd" and "baaadword" are caught by a pattern for "badword". The PvE server re-checks with the
    /// same list; this copy only gives the player instant feedback.
    /// </summary>
    public static class PlayerNameRules
    {
        public const int MinLength = 3;
        public const int MaxLength = 16;

        static readonly Regex Shape = new("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant);

        /// <summary>Removes surrounding whitespace and turns inner spaces into underscores (UGS refuses spaces).</summary>
        public static string Clean(string raw) =>
            string.IsNullOrEmpty(raw) ? "" : Regex.Replace(raw.Trim(), @"\s+", "_");

        /// <summary>Checks a cleaned name (see <see cref="Clean"/>) against the shape rules and the filter.</summary>
        public static NameCheck Validate(string name, IReadOnlyList<string> blockedPatterns)
        {
            if (string.IsNullOrEmpty(name))        return NameCheck.Empty;
            if (name.Length < MinLength)           return NameCheck.TooShort;
            if (name.Length > MaxLength)           return NameCheck.TooLong;
            if (!Shape.IsMatch(name))              return NameCheck.BadCharacters;
            if (!HasLetter(name))                  return NameCheck.NoLetter;
            if (IsBlocked(name, blockedPatterns))  return NameCheck.Blocked;
            return NameCheck.Ok;
        }

        /// <summary>True when any pattern matches the lower-cased name or its normalised form.</summary>
        public static bool IsBlocked(string name, IReadOnlyList<string> blockedPatterns)
        {
            if (blockedPatterns == null || string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            string norm  = Normalize(name);
            foreach (var p in blockedPatterns)
            {
                var re = Compile(p);
                if (re == null) continue;
                if (re.IsMatch(lower) || re.IsMatch(norm)) return true;
            }
            return false;
        }

        /// <summary>
        /// Lower-case, leetspeak mapped to letters (0 o, 1 i, 3 e, 4 a, 5 s, 7 t, 8 b, 9 g, @ a, $ s, ! i),
        /// separators removed, and runs of the same letter collapsed to one.
        /// </summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new StringBuilder(name.Length);
            char last = '\0';
            foreach (char raw in name.ToLowerInvariant())
            {
                char c = raw switch
                {
                    '0' => 'o', '1' => 'i', '3' => 'e', '4' => 'a', '5' => 's',
                    '7' => 't', '8' => 'b', '9' => 'g', '@' => 'a', '$' => 's', '!' => 'i',
                    _   => raw,
                };
                if (c == '_' || c == '-' || c == '.' || char.IsWhiteSpace(c)) continue;
                if (c == last) continue;
                sb.Append(c);
                last = c;
            }
            return sb.ToString();
        }

        /// <summary>The name without the UGS "#1234" suffix.</summary>
        public static string WithoutTag(string ugsName)
        {
            if (string.IsNullOrEmpty(ugsName)) return "";
            int hash = ugsName.LastIndexOf('#');
            return hash > 0 ? ugsName.Substring(0, hash) : ugsName;
        }

        /// <summary>The UGS "#1234" suffix including the '#', or "" when there is none.</summary>
        public static string Tag(string ugsName)
        {
            if (string.IsNullOrEmpty(ugsName)) return "";
            int hash = ugsName.LastIndexOf('#');
            return hash > 0 ? ugsName.Substring(hash) : "";
        }

        /// <summary>Player-facing reason for a refusal.</summary>
        public static string Message(NameCheck check) => check switch
        {
            NameCheck.Ok            => "",
            NameCheck.Empty         => "Enter a name.",
            NameCheck.TooShort      => $"Use at least {MinLength} characters.",
            NameCheck.TooLong       => $"Use at most {MaxLength} characters.",
            NameCheck.BadCharacters => "Use letters, numbers, _ and - only.",
            NameCheck.NoLetter      => "Include at least one letter.",
            NameCheck.Blocked       => "That name isn't allowed. Try another.",
            _                       => "That name isn't allowed.",
        };

        // Patterns come from data: compile each once, and skip (not throw on) a malformed one.
        static readonly Dictionary<string, Regex> s_cache = new();

        /// <summary>The compiled pattern, or null when it is empty or not a valid regex.</summary>
        public static Regex Compile(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return null;
            lock (s_cache)
            {
                if (s_cache.TryGetValue(pattern, out var cached)) return cached;
                Regex re;
                try { re = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase); }
                catch (ArgumentException) { re = null; }
                s_cache[pattern] = re;
                return re;
            }
        }

        static bool HasLetter(string s)
        {
            foreach (char c in s) if (char.IsLetter(c)) return true;
            return false;
        }
    }
}
