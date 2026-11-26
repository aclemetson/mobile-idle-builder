using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Cosmetics bought with Strings (#500, part 3), client side: owning (<see cref="SaveData.pveCosmetics"/>) and
    /// equipping one per kind (<see cref="SaveData.pveNameColor"/>, <see cref="SaveData.pveTitle"/>,
    /// <see cref="SaveData.pveBeltTheme"/>). Name colour and title are pushed to the PvE server
    /// (<see cref="TournamentService.SyncLookIfChanged"/>) so other players see them on the leaderboard; the belt
    /// theme only recolours this player's own belts (<see cref="ConveyorVisualizer"/>).
    /// </summary>
    public static class TournamentCosmetics
    {
        /// <summary>Raised after a purchase or an equip change (main thread).</summary>
        public static event Action Changed;

        public static bool Owns(SaveData save, string id) => save?.pveCosmetics != null && save.pveCosmetics.Contains(id);

        public static string Equipped(SaveData save, CosmeticKind kind) => kind switch
        {
            CosmeticKind.NameColor => save?.pveNameColor,
            CosmeticKind.Title     => save?.pveTitle,
            _                      => save?.pveBeltTheme,
        };

        public static bool IsEquipped(SaveData save, CosmeticDef def) =>
            def != null && !string.IsNullOrEmpty(def.id) && Equipped(save, def.kind) == def.id;

        public static bool CanBuy(SaveData save, CosmeticDef def) =>
            save != null && def != null && !Owns(save, def.id) && save.pveStrings >= def.cost;

        /// <summary>Buys <paramref name="id"/> and equips it. False when unknown, owned or unaffordable.</summary>
        public static bool TryBuy(SaveData save, string id)
        {
            var def = PveCosmetics.Find(id);
            if (!CanBuy(save, def)) return false;
            save.pveStrings -= def.cost;
            save.pveCosmetics ??= new List<string>();
            save.pveCosmetics.Add(id);
            SetEquipped(save, def.kind, id);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Equips an owned cosmetic, or takes it off when it is already equipped. False when not owned.</summary>
        public static bool ToggleEquip(SaveData save, string id)
        {
            var def = PveCosmetics.Find(id);
            if (def == null || !Owns(save, id)) return false;
            SetEquipped(save, def.kind, IsEquipped(save, def) ? null : id);
            Changed?.Invoke();
            return true;
        }

        static void SetEquipped(SaveData save, CosmeticKind kind, string id)
        {
            switch (kind)
            {
                case CosmeticKind.NameColor: save.pveNameColor = id; break;
                case CosmeticKind.Title:     save.pveTitle     = id; break;
                default:                     save.pveBeltTheme = id; break;
            }
        }

        /// <summary>True when the save's name colour / title differ from what the server last reported.</summary>
        public static bool LookDiffers(SaveData save, string serverColor, string serverTitle) =>
            (save?.pveNameColor ?? "") != (serverColor ?? "") || (save?.pveTitle ?? "") != (serverTitle ?? "");

        /// <summary>The equipped belt theme's line and glow colours, or null for the default warm-white belts.</summary>
        public static (Color line, Color glow)? BeltColors(SaveData save)
        {
            var def = PveCosmetics.Find(save?.pveBeltTheme);
            if (def == null || def.kind != CosmeticKind.BeltTheme) return null;
            if (!ColorUtility.TryParseHtmlString(def.value, out var line) || !ColorUtility.TryParseHtmlString(def.value2, out var glow))
                return null;
            return (line, glow);
        }

        /// <summary>A name colour as a Unity colour, or null for none / unknown.</summary>
        public static Color? NameColor(string id)
        {
            string hex = PveCosmetics.ColorHex(id);
            return hex != null && ColorUtility.TryParseHtmlString(hex, out var c) ? c : (Color?)null;
        }
    }
}
