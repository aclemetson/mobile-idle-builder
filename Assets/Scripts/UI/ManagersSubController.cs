using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Populates and manages the Managers panel (managers-panel): the hire list. Assigning a
    /// manager to a building happens from the building inspector; this panel hires and shows status.
    /// Sits as a component alongside HUDController on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class ManagersSubController : MonoBehaviour
    {
        private VisualElement _panel;
        private Label         _currencyLabel;
        private ScrollView    _list;
        private HUDController _hud;

        public void Init(VisualElement root, HUDController hud)
        {
            _hud           = hud;
            _panel         = root.Q("managers-panel");
            _currencyLabel = root.Q<Label>("managers-currency");
            _list          = root.Q<ScrollView>("managers-list");
        }

        /// <summary>Rebuilds the panel contents with current state.</summary>
        public void Refresh()
        {
            if (_list == null) return;
            _list.Clear();

            var svc = ManagerService.Instance;
            if (svc == null)
            {
                _list.Add(new Label("Managers not available."));
                return;
            }

            long held = svc.HeldPrestige();
            if (_currencyLabel != null)
                _currencyLabel.text = $"✦ {held:N0}";

            foreach (var mgr in svc.AllManagers)
            {
                if (mgr == null) continue;

                bool hired      = svc.IsHired(mgr.id);
                bool assigned   = hired && svc.IsAssigned(mgr.id);
                bool canAfford  = !hired && held >= mgr.hireCostPrestige;
                int  stars      = hired ? svc.GetStars(mgr.id) : 1;
                float effValue  = mgr.EffectiveValueAt(stars);

                var row = new VisualElement();
                row.AddToClassList("upgrade-row");
                if (hired) row.AddToClassList("upgrade-row--maxed");

                var header = new VisualElement();
                header.AddToClassList("upgrade-row-header");

                var nameLabel = new Label(mgr.displayName);
                nameLabel.AddToClassList("upgrade-row-name");

                string status = !hired
                    ? ""
                    : $"{StarString(stars, mgr.MaxStar)}  {(assigned ? "Assigned" : "Hired")}";
                var statusLabel = new Label(status);
                statusLabel.AddToClassList("upgrade-row-level");

                header.Add(nameLabel);
                header.Add(statusLabel);
                row.Add(header);

                var desc = new Label($"{mgr.description}  ({BonusSummary(mgr.bonusType, effValue)})");
                desc.AddToClassList("upgrade-row-desc");
                row.Add(desc);

                var footer = new VisualElement();
                footer.AddToClassList("upgrade-row-footer");

                if (!hired)
                {
                    var costLabel = new Label($"✦ {mgr.hireCostPrestige:N0}");
                    costLabel.AddToClassList("upgrade-row-cost");
                    if (!canAfford) costLabel.AddToClassList("upgrade-row-cost--unaffordable");
                    footer.Add(costLabel);

                    var hireBtn = new Button { text = "Hire" };
                    hireBtn.AddToClassList("craft-btn");
                    hireBtn.SetEnabled(canAfford);
                    var capturedId = mgr.id;
                    hireBtn.clicked += () => OnHirePressed(capturedId);
                    footer.Add(hireBtn);
                }
                else
                {
                    var hint = new Label(assigned
                        ? "Open a building to reassign."
                        : "Open a building to assign.");
                    hint.AddToClassList("upgrade-row-prereq");
                    footer.Add(hint);

                    if (svc.CanUpgradeStar(mgr.id))
                    {
                        int upCost = svc.NextStarCost(mgr.id);
                        bool canUp = held >= upCost;

                        var costLabel = new Label($"✦ {upCost:N0}");
                        costLabel.AddToClassList("upgrade-row-cost");
                        if (!canUp) costLabel.AddToClassList("upgrade-row-cost--unaffordable");
                        footer.Add(costLabel);

                        var upBtn = new Button { text = "Upgrade ★" };
                        upBtn.AddToClassList("craft-btn");
                        upBtn.SetEnabled(canUp);
                        var capturedId = mgr.id;
                        upBtn.clicked += () => OnUpgradePressed(capturedId);
                        footer.Add(upBtn);
                    }
                }

                row.Add(footer);
                _list.Add(row);
            }
        }

        /// <summary>Compact star meter, e.g. "★★☆☆☆".</summary>
        private static string StarString(int stars, int maxStar)
        {
            stars   = Mathf.Clamp(stars, 1, Mathf.Max(1, maxStar));
            return new string('★', stars) + new string('☆', Mathf.Max(0, maxStar - stars));
        }

        private static string BonusSummary(ManagerBonusType type, float value)
        {
            switch (type)
            {
                case ManagerBonusType.CraftSpeed:     return $"{value:0.##}x speed";
                case ManagerBonusType.OutputQuantity: return $"{value:0.##}x output";
                case ManagerBonusType.PowerDiscount:  return $"-{(1f - value) * 100f:0}% power";
                default:                              return "";
            }
        }

        private void OnHirePressed(string managerId)
        {
            var svc = ManagerService.Instance;
            if (svc == null) return;
            if (svc.Hire(managerId))
            {
                var mgr = svc.Find(managerId);
                _hud?.ShowNotification("✦", $"Hired {mgr?.displayName ?? managerId}!");
            }
            Refresh();
        }

        private void OnUpgradePressed(string managerId)
        {
            var svc = ManagerService.Instance;
            if (svc == null) return;
            if (svc.UpgradeStar(managerId))
            {
                var mgr = svc.Find(managerId);
                _hud?.ShowNotification("★", $"{mgr?.displayName ?? managerId} is now {svc.GetStars(managerId)}★!");
            }
            Refresh();
        }
    }
}
