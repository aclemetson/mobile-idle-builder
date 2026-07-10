using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Populates and manages the Prestige Shop (upgrades-panel).
    /// Sits as a component alongside HUDController on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class PrestigeShopSubController : MonoBehaviour
    {
        private VisualElement _panel;
        private Label         _currencyLabel;
        private ScrollView    _list;
        private HUDController _hud;

        private EntityManager _em;
        private EntityQuery   _prestigeQuery;
        private bool          _ecsReady;

        public void Init(VisualElement root, HUDController hud)
        {
            _hud           = hud;
            _panel         = root.Q("upgrades-panel");
            _currencyLabel = root.Q<Label>("prestige-shop-currency");
            _list          = root.Q<ScrollView>("upgrades-list");
        }

        public void SetECSContext(EntityManager em)
        {
            _em            = em;
            _prestigeQuery = em.CreateEntityQuery(ComponentType.ReadWrite<PrestigeData>());
            _ecsReady      = true;
        }

        /// <summary>Rebuilds the panel contents with current state.</summary>
        public void Refresh()
        {
            if (_list == null) return;
            _list.Clear();

            long heldPC = GetHeldPC();
            if (_currencyLabel != null)
                _currencyLabel.text = $"✦ {heldPC:N0}";

            var svc = PersistentUpgradeService.Instance;
            if (svc == null)
            {
                _list.Add(new Label("Prestige upgrades not available."));
                return;
            }

            foreach (var def in PersistentUpgradeService.All)
            {
                int   level       = svc.GetLevel(def.id);
                bool  prereqsMet  = svc.PrereqsMet(def);
                bool  isMaxed     = level >= def.maxLevel;
                int   nextCost    = svc.NextLevelCost(def);
                bool  canAfford   = !isMaxed && heldPC >= nextCost;

                var row = new VisualElement();
                row.AddToClassList("upgrade-row");
                if (!prereqsMet) row.AddToClassList("upgrade-row--locked");
                else if (isMaxed) row.AddToClassList("upgrade-row--maxed");

                // Header row: name + level badge
                var header = new VisualElement();
                header.AddToClassList("upgrade-row-header");

                var nameLabel = new Label(def.displayName);
                nameLabel.AddToClassList("upgrade-row-name");

                var levelLabel = new Label(isMaxed ? $"Lv {level} / {def.maxLevel}  MAX" : $"Lv {level} / {def.maxLevel}");
                levelLabel.AddToClassList("upgrade-row-level");

                header.Add(nameLabel);
                header.Add(levelLabel);
                row.Add(header);

                // Description
                var desc = new Label(def.description);
                desc.AddToClassList("upgrade-row-desc");
                row.Add(desc);

                // Footer: cost / prereq + buy button
                var footer = new VisualElement();
                footer.AddToClassList("upgrade-row-footer");

                if (!prereqsMet)
                {
                    var prereqLines = new System.Text.StringBuilder();
                    foreach (var (reqId, minLevel) in def.prereqs)
                    {
                        if (svc.GetLevel(reqId) < minLevel)
                        {
                            var reqDef = PersistentUpgradeService.FindDef(reqId);
                            prereqLines.Append($"Requires {reqDef?.displayName ?? reqId} Lv {minLevel}  ");
                        }
                    }
                    var prereqLabel = new Label(prereqLines.ToString().TrimEnd());
                    prereqLabel.AddToClassList("upgrade-row-prereq");
                    footer.Add(prereqLabel);
                }
                else if (!isMaxed)
                {
                    var costLabel = new Label($"✦ {nextCost:N0}");
                    costLabel.AddToClassList("upgrade-row-cost");
                    if (!canAfford) costLabel.AddToClassList("upgrade-row-cost--unaffordable");
                    footer.Add(costLabel);

                    var buyBtn = new Button { text = "Buy" };
                    buyBtn.AddToClassList("craft-btn");
                    buyBtn.SetEnabled(canAfford);

                    var capturedDef = def;
                    buyBtn.clicked += () => OnBuyPressed(capturedDef);
                    footer.Add(buyBtn);
                }

                row.Add(footer);
                _list.Add(row);
            }
        }

        private void OnBuyPressed(UpgradeDef def)
        {
            var svc = PersistentUpgradeService.Instance;
            if (svc == null || !_ecsReady || _prestigeQuery.IsEmpty) return;

            int cost = svc.NextLevelCost(def);
            if (cost < 0) return;

            // Read current PC from ECS
            var entity   = _prestigeQuery.GetSingletonEntity();
            var prestige = _em.GetComponentData<PrestigeData>(entity);
            long held    = prestige.PrestigeCurrency - prestige.PrestigeCurrencySpent;
            if (held < cost) return;

            // Deduct currency and advance the upgrade level
            prestige.PrestigeCurrencySpent += cost;
            svc.AddLevel(def.id);

            // Sync aggregated multipliers into ECS in one write
            var save = SaveManager.Instance?.Current;
            if (save != null)
            {
                svc.FlushToSave(save.permanentUpgrades);
                svc.FlushEffectsToSave(save);
                prestige.SpeedMultiplier  = save.prestigeSpeedMultiplier;
                prestige.OutputMultiplier = save.prestigeOutputMultiplier;
                prestige.CostReduction    = save.prestigeCostReduction;
            }

            // Write updated PrestigeCurrencySpent to ECS BEFORE Refresh() so
            // GetHeldPC() reads the correct post-purchase balance.
            _em.SetComponentData(entity, prestige);

            SaveManager.Instance?.SaveLocal();
            _hud?.ShowNotification("✦", $"{def.displayName} upgraded to Lv {svc.GetLevel(def.id)}!");

            // Rebuild the list now that ECS reflects the new balance.
            Refresh();
        }

        private long GetHeldPC()
        {
            if (!_ecsReady || _prestigeQuery.IsEmpty) return 0L;
            var p = _em.GetComponentData<PrestigeData>(_prestigeQuery.GetSingletonEntity());
            return p.PrestigeCurrency - p.PrestigeCurrencySpent;
        }
    }
}
