using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Handles per-frame status bar refresh: inventory labels, entropy, power, prestige button.
    /// Sibling MonoBehaviour to HUDController on the HUD GameObject.
    /// Uses dirty flags to skip DOM writes on stable frames.
    /// Call Init(root) then SetECSContext(...) before use.
    /// </summary>
    public class HUDStatusBarController : MonoBehaviour
    {
        private VisualElement _drawerInventory;
        private Label         _entropyLabel;
        private Label         _prestigeTopbarLabel;
        private Label         _powerLabel;
        private Button        _btnPrestige;

        private readonly Dictionary<int, Label> _inventoryLabels = new();

        private EntityManager _em;
        private EntityQuery   _inventoryQuery;
        private EntityQuery   _progressQuery;
        private EntityQuery   _powerQuery;
        private bool          _ecsReady;

        // Dirty-flag state
        private int   _lastInventoryHash      = int.MinValue;
        private long  _lastBaseCurrency       = long.MinValue;
        private float _lastPowerCurrent       = -1f;
        private float _lastPowerMax           = -1f;
        private bool  _lastPrestigeAvailable  = false;
        private long  _lastHeldPC             = long.MinValue;

        public void Init(VisualElement root)
        {
            _drawerInventory     = root.Q("drawer-inventory");
            _entropyLabel        = root.Q<Label>("entropy-label");
            _prestigeTopbarLabel = root.Q<Label>("prestige-topbar-label");
            _powerLabel          = root.Q<Label>("power-label");
            _btnPrestige         = root.Q<Button>("btn-prestige");
        }

        public void SetECSContext(EntityManager em, EntityQuery inventoryQuery,
                                  EntityQuery progressQuery, EntityQuery powerQuery)
        {
            _em             = em;
            _inventoryQuery = inventoryQuery;
            _progressQuery  = progressQuery;
            _powerQuery     = powerQuery;
            _ecsReady       = true;
        }

        public void Tick()
        {
            if (!_ecsReady) return;
            if (!_inventoryQuery.IsEmpty) RefreshInventoryBar();
            if (!_progressQuery.IsEmpty)
            {
                RefreshEntropyLabel();
                RefreshPrestigeButton();
                RefreshPrestigeTopbarLabel();
            }
            if (!_powerQuery.IsEmpty) RefreshPowerLabel();
        }

        private void RefreshInventoryBar()
        {
            if (_drawerInventory == null) return;

            var buffer = _em.GetBuffer<InventorySlot>(
                _inventoryQuery.GetSingletonEntity(), isReadOnly: true);

            // Cheap hash over non-zero slots — skip DOM writes if nothing changed
            int hash = 0;
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].Quantity > 0)
                    hash = hash * 397 ^ (buffer[i].ItemID * 1000 + buffer[i].Quantity);

            if (hash == _lastInventoryHash) return;
            _lastInventoryHash = hash;

            var seen = new HashSet<int>();
            for (int i = 0; i < buffer.Length; i++)
            {
                var slot = buffer[i];
                if (slot.Quantity <= 0) continue;

                seen.Add(slot.ItemID);
                var item = ItemDatabase.GetStatic(slot.ItemID);
                string label = item != null
                    ? $"{item.symbol ?? item.displayName}  ×{slot.Quantity}"
                    : $"#{slot.ItemID}  ×{slot.Quantity}";

                if (!_inventoryLabels.TryGetValue(slot.ItemID, out var lbl))
                {
                    lbl = new Label();
                    lbl.AddToClassList("drawer-inventory-item");
                    _drawerInventory.Add(lbl);
                    _inventoryLabels[slot.ItemID] = lbl;
                }

                lbl.text = label;
                lbl.style.display = DisplayStyle.Flex;
            }

            foreach (var (itemId, lbl) in _inventoryLabels)
            {
                if (!seen.Contains(itemId))
                    lbl.style.display = DisplayStyle.None;
            }
        }

        private void RefreshEntropyLabel()
        {
            if (_entropyLabel == null) return;
            var progress = _em.GetComponentData<PlayerProgressData>(_progressQuery.GetSingletonEntity());
            if (progress.BaseCurrency == _lastBaseCurrency) return;
            _lastBaseCurrency      = progress.BaseCurrency;
            _entropyLabel.text = $"◈ {progress.BaseCurrency:N0}";
        }

        private void RefreshPowerLabel()
        {
            if (_powerLabel == null) return;
            var nodes = _powerQuery.ToComponentDataArray<PowerNodeData>(Allocator.Temp);
            float current = 0f, max = 0f;
            for (int i = 0; i < nodes.Length; i++)
            {
                current += nodes[i].CurrentEV;
                max     += nodes[i].MaxEV;
            }
            nodes.Dispose();

            if (Mathf.Approximately(current, _lastPowerCurrent) &&
                Mathf.Approximately(max, _lastPowerMax)) return;
            _lastPowerCurrent = current;
            _lastPowerMax     = max;
            _powerLabel.text  = HUDController.FormatPowerLabel(current, max);
        }

        private void RefreshPrestigeButton()
        {
            if (_btnPrestige == null) return;
            var progress = _em.GetComponentData<PlayerProgressData>(_progressQuery.GetSingletonEntity());
            if (progress.PrestigeAvailable == _lastPrestigeAvailable) return;
            _lastPrestigeAvailable         = progress.PrestigeAvailable;
            _btnPrestige.style.display = progress.PrestigeAvailable
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private void RefreshPrestigeTopbarLabel()
        {
            if (_prestigeTopbarLabel == null) return;
            var entity   = _progressQuery.GetSingletonEntity();
            var prestige = _em.GetComponentData<PrestigeData>(entity);
            long held    = prestige.PrestigeCurrency - prestige.PrestigeCurrencySpent;
            if (held == _lastHeldPC) return;
            _lastHeldPC                  = held;
            _prestigeTopbarLabel.text    = $"✦ {held:N0}";
        }
    }
}
