using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MobileIdleBuilder.PveModule
{
    /// <summary>
    /// game_config from the game's own Assets/Data/game_data.json, embedded into the module at build time,
    /// so the league table, rewards and name filter are the ones the client ships with. Redeploy the module
    /// after changing game_config.pve or name_filter_patterns.
    /// </summary>
    public sealed class PveGameConfig
    {
        public PveConfig Pve { get; private set; }
        public IReadOnlyList<string> NamePatterns { get; private set; } = Array.Empty<string>();

        static readonly Lazy<PveGameConfig> s_embedded = new Lazy<PveGameConfig>(LoadEmbedded);

        public static PveGameConfig Embedded => s_embedded.Value;

        static PveGameConfig LoadEmbedded()
        {
            var asm = typeof(PveGameConfig).Assembly;
            using var stream = asm.GetManifestResourceStream("game_data.json")
                               ?? throw new InvalidOperationException("game_data.json is not embedded in the module");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }

        /// <summary>Reads game_config.pve and game_config.name_filter_patterns out of a game_data.json document.</summary>
        public static PveGameConfig Parse(string gameDataJson)
        {
            using var doc = JsonDocument.Parse(gameDataJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            var cfg = new PveGameConfig();
            if (!doc.RootElement.TryGetProperty("game_config", out var gc)) return cfg;
            if (gc.TryGetProperty("pve", out var pve))
                cfg.Pve = JsonSerializer.Deserialize<PveConfig>(pve.GetRawText(), PveStore.Json);
            if (gc.TryGetProperty("name_filter_patterns", out var pats) && pats.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var p in pats.EnumerateArray()) if (p.ValueKind == JsonValueKind.String) list.Add(p.GetString());
                cfg.NamePatterns = list;
            }
            return cfg;
        }
    }
}
