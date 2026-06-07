using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Drives the full-screen premium shop overlay on the main menu.
    /// Reads directly from SaveManager.Current and PremiumShopService.
    ///
    /// Wire-up: Attach as a sibling MonoBehaviour to MainMenuController on the same
    /// GameObject. No serialized fields required.
    /// </summary>
    [RequireComponent(typeof(MainMenuController))]
    public class MainMenuShopController : MonoBehaviour
    {
        private enum ShopTab { SpeedUps, Entropy, PrestigeCurrency, Crystals }

        // ── UXML references ───────────────────────────────────────────────────

        private VisualElement _overlay;
        private Label         _crystalBalance;
        private Label         _contextLabel;
        private ScrollView    _tierList;

        private Button _tabSpeedUps;
        private Button _tabEntropy;
        private Button _tabPrestige;
        private Button _tabCrystals;
        private Button _btnClose;

        private ShopTab _activeTab = ShopTab.SpeedUps;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void Initialize(VisualElement root)
        {
            Cleanup();
            QueryElements(root);
            BindButtons();
            HUDController.SetElementVisible(_overlay, false);
        }

        public void Cleanup()
        {
            if (_btnClose    != null) _btnClose.clicked    -= Close;
            if (_tabSpeedUps != null) _tabSpeedUps.clicked -= OnTabSpeedUps;
            if (_tabEntropy  != null) _tabEntropy.clicked  -= OnTabEntropy;
            if (_tabPrestige != null) _tabPrestige.clicked -= OnTabPrestige;
            if (_tabCrystals != null) _tabCrystals.clicked -= OnTabCrystals;
        }

        private void OnDisable() => Cleanup();

        // ── Public API ────────────────────────────────────────────────────────

        public void Open()
        {
            RefreshBalanceLabel();
            Refresh();
            HUDController.SetElementVisible(_overlay, true);
        }

        public void Close()
            => HUDController.SetElementVisible(_overlay, false);

        // ── Internal ──────────────────────────────────────────────────────────

        private void QueryElements(VisualElement root)
        {
            _overlay        = root.Q("shop-overlay");
            _crystalBalance = root.Q<Label>("shop-crystal-balance");
            _contextLabel   = root.Q<Label>("shop-context-label");
            _tierList       = root.Q<ScrollView>("shop-tier-list");
            _btnClose       = root.Q<Button>("btn-shop-close");
            _tabSpeedUps    = root.Q<Button>("shop-tab-speedups");
            _tabEntropy     = root.Q<Button>("shop-tab-entropy");
            _tabPrestige    = root.Q<Button>("shop-tab-prestige");
            _tabCrystals    = root.Q<Button>("shop-tab-crystals");
        }

        private void BindButtons()
        {
            if (_btnClose    != null) _btnClose.clicked    += Close;
            if (_tabSpeedUps != null) _tabSpeedUps.clicked += OnTabSpeedUps;
            if (_tabEntropy  != null) _tabEntropy.clicked  += OnTabEntropy;
            if (_tabPrestige != null) _tabPrestige.clicked += OnTabPrestige;
            if (_tabCrystals != null) _tabCrystals.clicked += OnTabCrystals;
        }

        private void OnTabSpeedUps()  { _activeTab = ShopTab.SpeedUps;         UpdateTabStyles(); Refresh(); }
        private void OnTabEntropy()   { _activeTab = ShopTab.Entropy;           UpdateTabStyles(); Refresh(); }
        private void OnTabPrestige()  { _activeTab = ShopTab.PrestigeCurrency;  UpdateTabStyles(); Refresh(); }
        private void OnTabCrystals()  { _activeTab = ShopTab.Crystals;          UpdateTabStyles(); Refresh(); }

        private void UpdateTabStyles()
        {
            SetTabActive(_tabSpeedUps, _activeTab == ShopTab.SpeedUps);
            SetTabActive(_tabEntropy,  _activeTab == ShopTab.Entropy);
            SetTabActive(_tabPrestige, _activeTab == ShopTab.PrestigeCurrency);
            SetTabActive(_tabCrystals, _activeTab == ShopTab.Crystals);
        }

        private static void SetTabActive(Button btn, bool active)
        {
            if (btn == null) return;
            if (active) btn.AddToClassList("shop-tab--active");
            else        btn.RemoveFromClassList("shop-tab--active");
        }

        private void RefreshBalanceLabel()
        {
            if (_crystalBalance == null) return;
            long crystals = SaveManager.Instance?.Current?.paidCurrency ?? 0;
            _crystalBalance.text = $"◆ {crystals:N0}";
        }

        private void Refresh()
        {
            if (_tierList == null) return;
            _tierList.Clear();

            RefreshBalanceLabel();

            switch (_activeTab)
            {
                case ShopTab.SpeedUps:        BuildSpeedUpCards();         break;
                case ShopTab.Entropy:         BuildEntropyCards();         break;
                case ShopTab.PrestigeCurrency: BuildPrestigeCurrencyCards(); break;
                case ShopTab.Crystals:        BuildCrystalPackCards();     break;
            }
        }

        // ── Speed Up cards ────────────────────────────────────────────────────

        private void BuildSpeedUpCards()
        {
            bool boostActive = PremiumShopService.Instance?.IsSpeedBoostActive() ?? false;
            string expiryUtc = SaveManager.Instance?.Current?.speedBoostExpiryUtc;

            if (boostActive && !string.IsNullOrEmpty(expiryUtc))
            {
                var expiry  = DateTime.Parse(expiryUtc, null, System.Globalization.DateTimeStyles.RoundtripKind);
                var remaining = expiry - DateTime.UtcNow;
                _contextLabel.text = $"ACTIVE: {FormatDuration(remaining)} remaining";
            }
            else
            {
                _contextLabel.text = "2× offline production speed";
            }

            long crystals = SaveManager.Instance?.Current?.paidCurrency ?? 0;
            for (int i = 0; i < PremiumShopCalculator.SpeedUpTiers.Length; i++)
            {
                int captured = i;
                var tier     = PremiumShopCalculator.SpeedUpTiers[i];
                bool canAfford = crystals >= tier.CrystalCost;

                var card = MakeTierCard(
                    tier.Name,
                    tier.Description,
                    $"◆ {tier.CrystalCost:N0}",
                    canAfford,
                    boostActive,
                    boostActive ? "⚡ Active" : null,
                    () =>
                    {
                        var result = PremiumShopService.Instance?.TryBuySpeedUp(captured) ?? PurchaseResult.InvalidTier;
                        if (result == PurchaseResult.Success) Refresh();
                    });

                _tierList.Add(card);
            }
        }

        // ── Entropy cards ─────────────────────────────────────────────────────

        private void BuildEntropyCards()
        {
            float netWorth = SaveManager.Instance?.Current?.lastKnownNetWorth ?? 0f;
            _contextLabel.text = netWorth > 0f
                ? $"Based on last net worth: {netWorth:N0}◈"
                : "Based on last run's net worth (start a run first)";

            long crystals = SaveManager.Instance?.Current?.paidCurrency ?? 0;
            for (int i = 0; i < PremiumShopCalculator.EntropyTiers.Length; i++)
            {
                int captured = i;
                var tier     = PremiumShopCalculator.EntropyTiers[i];
                long amount  = PremiumShopCalculator.CalcEntropyAmount(i, netWorth);
                bool canAfford = crystals >= tier.CrystalCost;

                var card = MakeTierCard(
                    tier.Name,
                    $"{amount:N0}◈ ({tier.Description})",
                    $"◆ {tier.CrystalCost:N0}",
                    canAfford,
                    false,
                    null,
                    () =>
                    {
                        var result = PremiumShopService.Instance?.TryBuyEntropy(captured) ?? PurchaseResult.InvalidTier;
                        if (result == PurchaseResult.Success) Refresh();
                    });

                _tierList.Add(card);
            }
        }

        // ── Prestige Currency cards ───────────────────────────────────────────

        private void BuildPrestigeCurrencyCards()
        {
            _contextLabel.text = "Adds directly to your prestige currency balance";

            long crystals = SaveManager.Instance?.Current?.paidCurrency ?? 0;
            for (int i = 0; i < PremiumShopCalculator.PrestigeCurrencyTiers.Length; i++)
            {
                int captured = i;
                var tier     = PremiumShopCalculator.PrestigeCurrencyTiers[i];
                bool canAfford = crystals >= tier.CrystalCost;

                var card = MakeTierCard(
                    tier.Name,
                    $"+{tier.PrestigeCurrencyAmount:N0}✦ prestige currency",
                    $"◆ {tier.CrystalCost:N0}",
                    canAfford,
                    false,
                    null,
                    () =>
                    {
                        var result = PremiumShopService.Instance?.TryBuyPrestigeCurrency(captured) ?? PurchaseResult.InvalidTier;
                        if (result == PurchaseResult.Success) Refresh();
                    });

                _tierList.Add(card);
            }
        }

        // ── Crystal Pack cards (IAP) ──────────────────────────────────────────

        private void BuildCrystalPackCards()
        {
            _contextLabel.text = "Purchase crystals with real money";

            for (int i = 0; i < PremiumShopCalculator.CrystalPackTiers.Length; i++)
            {
                int captured = i;
                var tier     = PremiumShopCalculator.CrystalPackTiers[i];

                string price =
#if UNITY_PURCHASING
                    IAPService.Instance?.GetLocalizedPrice(tier.ProductId) ??
#endif
                    tier.DisplayPrice;

                var card = MakeIAPTierCard(
                    tier.Name,
                    $"+{tier.CrystalAmount:N0}◆ crystals",
                    price,
                    () => OnIAPBuyPressed(tier.ProductId));

                _tierList.Add(card);
            }
        }

        private void OnIAPBuyPressed(string productId)
        {
#if UNITY_PURCHASING
            IAPService.Instance?.BuyProduct(productId);
#else
            GameLogger.Debug($"[Shop] IAP not available — product: {productId}");
#endif
        }

        // ── Card factory helpers ──────────────────────────────────────────────

        private VisualElement MakeTierCard(
            string name,
            string desc,
            string costText,
            bool canAfford,
            bool highlightActive,
            string badgeText,
            Action onBuy)
        {
            var card = new VisualElement();
            card.AddToClassList("shop-tier-card");
            if (highlightActive)
                card.AddToClassList("shop-tier-card--boost-active");

            var header = new VisualElement();
            header.AddToClassList("shop-tier-card__header");

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("shop-tier-card__name");
            header.Add(nameLabel);

            if (!string.IsNullOrEmpty(badgeText))
            {
                var badge = new Label(badgeText);
                badge.AddToClassList("shop-tier-card__badge");
                header.Add(badge);
            }

            card.Add(header);

            var descLabel = new Label(desc);
            descLabel.AddToClassList("shop-tier-card__desc");
            card.Add(descLabel);

            var footer = new VisualElement();
            footer.AddToClassList("shop-tier-card__footer");

            var costLabel = new Label(costText);
            costLabel.AddToClassList("shop-tier-card__cost");
            if (!canAfford)
                costLabel.AddToClassList("shop-tier-card__cost--unaffordable");
            footer.Add(costLabel);

            var buyBtn = new Button(onBuy) { text = "Buy" };
            buyBtn.AddToClassList("shop-buy-btn");
            if (!canAfford)
            {
                buyBtn.SetEnabled(false);
                buyBtn.AddToClassList("shop-buy-btn--disabled");
            }
            footer.Add(buyBtn);

            card.Add(footer);
            return card;
        }

        private VisualElement MakeIAPTierCard(
            string name,
            string desc,
            string priceText,
            Action onBuy)
        {
            var card = new VisualElement();
            card.AddToClassList("shop-tier-card");

            var header = new VisualElement();
            header.AddToClassList("shop-tier-card__header");

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("shop-tier-card__name");
            header.Add(nameLabel);
            card.Add(header);

            var descLabel = new Label(desc);
            descLabel.AddToClassList("shop-tier-card__desc");
            card.Add(descLabel);

            var footer = new VisualElement();
            footer.AddToClassList("shop-tier-card__footer");

            var costLabel = new Label(priceText);
            costLabel.AddToClassList("shop-tier-card__cost");
            footer.Add(costLabel);

#if UNITY_PURCHASING
            bool iapReady = IAPService.Instance?.IsInitialized ?? false;
            var buyBtn = new Button(onBuy) { text = iapReady ? "Buy" : "…" };
            buyBtn.AddToClassList("shop-buy-btn");
            buyBtn.AddToClassList("shop-buy-btn--iap");
            if (!iapReady)
            {
                buyBtn.SetEnabled(false);
                buyBtn.AddToClassList("shop-buy-btn--disabled");
            }
            footer.Add(buyBtn);
#else
            var comingSoon = new Label("Coming Soon");
            comingSoon.AddToClassList("shop-tier-card__cost");
            comingSoon.style.color = new UnityEngine.UIElements.StyleColor(new UnityEngine.Color(0.5f, 0.5f, 0.5f));
            footer.Add(comingSoon);
#endif

            card.Add(footer);
            return card;
        }

        // ── Utilities ─────────────────────────────────────────────────────────

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m {ts.Seconds}s";
        }
    }
}
