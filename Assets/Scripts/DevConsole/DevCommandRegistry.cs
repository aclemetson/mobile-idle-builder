#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Text;

namespace MobileIdleBuilder.Dev
{
    /// <summary>
    /// Registers and dispatches dev console commands.
    /// Patterns use angle-bracket tokens as positional arg captures:
    ///   "give currency <amount>" matches "give currency 500" with args[0]="500"
    /// Matching is case-insensitive; first registered match wins.
    /// </summary>
    internal sealed class DevCommandRegistry
    {
        private readonly List<DevCommandEntry> _commands = new();

        public void Register(string pattern, string description, Func<string[], string> handler)
        {
            _commands.Add(new DevCommandEntry
            {
                Pattern     = pattern.ToLowerInvariant(),
                Description = description,
                Handler     = handler,
            });
        }

        /// <summary>
        /// Parses rawInput, dispatches to matching command, returns result string.
        /// Returns an error string if no command matches.
        /// </summary>
        public string Execute(string rawInput)
        {
            if (string.IsNullOrWhiteSpace(rawInput))
                return string.Empty;

            var tokens = rawInput.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var entry in _commands)
            {
                var patternTokens = entry.Pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length != patternTokens.Length)
                    continue;

                var args    = new List<string>();
                bool match  = true;

                for (int i = 0; i < patternTokens.Length; i++)
                {
                    if (patternTokens[i].StartsWith('<') && patternTokens[i].EndsWith('>'))
                    {
                        args.Add(tokens[i]);
                    }
                    else if (tokens[i] != patternTokens[i])
                    {
                        match = false;
                        break;
                    }
                }

                if (!match) continue;

                try
                {
                    return entry.Handler(args.ToArray()) ?? string.Empty;
                }
                catch (Exception e)
                {
                    return $"Error: {e.Message}";
                }
            }

            return "Unknown command. Type 'help' for a list of commands.";
        }

        public string GetHelpText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Available commands:");
            foreach (var entry in _commands)
                sb.AppendLine($"  {entry.Pattern,-38} {entry.Description}");
            return sb.ToString().TrimEnd();
        }

        private sealed class DevCommandEntry
        {
            public string Pattern;
            public string Description;
            public Func<string[], string> Handler;
        }
    }
}
#endif
