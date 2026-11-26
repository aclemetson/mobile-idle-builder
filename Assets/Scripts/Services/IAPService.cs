// IAP integration requires com.unity.purchasing in Packages/manifest.json.
// Add the package and define UNITY_PURCHASING to activate this code.
// Product IDs must be registered identically in Google Play Console and App Store Connect.
#if UNITY_PURCHASING
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Handles Unity IAP initialisation and consumable crystal pack purchases.
    /// Persists across scenes; wire up alongside PremiumShopService in SplashScene or GameScene.
    /// Product IDs must match entries in Google Play Console and App Store Connect.
    /// </summary>
    public class IAPService : SingletonMonoBehaviour<IAPService>, IDetailedStoreListener
    {
        protected override bool PersistAcrossScenes => true;

        // ── Product IDs — must match store console entries ────────────────────
        public const string ProductCrystals600   = "crystals_tier1";
        public const string ProductCrystals3200  = "crystals_tier2";
        public const string ProductCrystals7500  = "crystals_tier3";
        public const string ProductCrystals20000 = "crystals_tier4";

        public static readonly Dictionary<string, long> CrystalAmounts = new()
        {
            { ProductCrystals600,   600   },
            { ProductCrystals3200,  3200  },
            { ProductCrystals7500,  7500  },
            { ProductCrystals20000, 20000 },
        };

        public event Action<string, bool> OnPurchaseComplete; // (productId, success)

        /// <summary>True when the store reports the ad-free purchase (#503) as owned on this account.</summary>
        public bool StoreOwnsAdFree =>
            IsInitialized && (_controller.products.WithID(AdFree.ProductId)?.hasReceipt ?? false);
        public bool IsInitialized { get; private set; }

        private IStoreController   _controller;
        private IExtensionProvider _extensions;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
        }

        private void Start()
        {
            // Remote kill-switch: skip store initialization so purchases are impossible without a rebuild.
            // BuyProduct already no-ops while IsInitialized is false.
            if (!FeatureFlags.IapEnabled)
            {
                GameLogger.Info("[IAP] Disabled by feature flag — store not initialized.");
                return;
            }
            InitializePurchasing();
        }

        private void InitializePurchasing()
        {
            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            builder.AddProduct(ProductCrystals600,   ProductType.Consumable);
            builder.AddProduct(ProductCrystals3200,  ProductType.Consumable);
            builder.AddProduct(ProductCrystals7500,  ProductType.Consumable);
            builder.AddProduct(ProductCrystals20000, ProductType.Consumable);
            builder.AddProduct(AdFree.ProductId,     ProductType.NonConsumable);
            UnityPurchasing.Initialize(this, builder);
        }

        public void BuyProduct(string productId)
        {
            if (!IsInitialized)
            {
                GameLogger.Warning("[IAP] Store not initialized — cannot buy.");
                return;
            }
            // Ad-free is bought once per store account (#503). Already owned here or on the store (bought on
            // another device, cloud save not synced yet): restore it instead of starting a second purchase.
            if (productId == AdFree.ProductId && !AdFree.CanBuy(SaveManager.Instance?.Current, StoreOwnsAdFree))
            {
                GameLogger.Info("[IAP] Ad-free already owned: restoring instead of buying.");
                RestoreAdFreeFromReceipt();
                OnPurchaseComplete?.Invoke(productId, AdFree.Owned(SaveManager.Instance?.Current));
                return;
            }
            _controller.InitiatePurchase(productId);
        }

        /// <summary>
        /// Returns the store's localised price string, falling back to our hardcoded price.
        /// In the Editor the Unity IAP fake store reports a placeholder "$0.01" for every product,
        /// so we always use the fallback there; on a real device the live Google Play / App Store
        /// price is used (and we still fall back if it is missing/empty).
        /// </summary>
        public string GetLocalizedPrice(string productId)
        {
#if UNITY_EDITOR
            return GetFallbackPrice(productId);
#else
            if (!IsInitialized) return GetFallbackPrice(productId);
            var product = _controller.products.WithID(productId);
            var price   = product?.metadata?.localizedPriceString;
            return string.IsNullOrEmpty(price) ? GetFallbackPrice(productId) : price;
#endif
        }

        // ── IDetailedStoreListener ────────────────────────────────────────────

        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _controller  = controller;
            _extensions  = extensions;
            IsInitialized = true;
            GameLogger.Info("[IAP] Store initialized.");
            RestoreAdFreeFromReceipt();
        }

        /// <summary>
        /// A reinstall or a new device: the store still holds the ad-free receipt, so the save gets it back.
        /// Google redelivers owned non-consumables at init; iOS needs <see cref="RestorePurchases"/>.
        /// </summary>
        void RestoreAdFreeFromReceipt()
        {
            var save = SaveManager.Instance?.Current;
            if (!StoreOwnsAdFree || !AdFree.Grant(save)) return;
            SaveManager.Instance.SaveLocal();
            GameLogger.Info("[IAP] Ad-free restored from the store receipt.");
            OnPurchaseComplete?.Invoke(AdFree.ProductId, true);
        }

        /// <summary>iOS "Restore Purchases" (App Store requirement for non-consumables). No-op elsewhere.</summary>
        public void RestorePurchases(Action<bool> onDone = null)
        {
#if UNITY_IOS
            if (!IsInitialized) { onDone?.Invoke(false); return; }
            _extensions.GetExtension<IAppleExtensions>().RestoreTransactions((ok, error) =>
            {
                if (!ok) GameLogger.Warning($"[IAP] Restore failed: {error}");
                RestoreAdFreeFromReceipt();
                onDone?.Invoke(ok);
            });
#else
            RestoreAdFreeFromReceipt();
            onDone?.Invoke(true);
#endif
        }

        public void OnInitializeFailed(InitializationFailureReason error)
            => GameLogger.Warning($"[IAP] Init failed: {error}");

        public void OnInitializeFailed(InitializationFailureReason error, string message)
            => GameLogger.Warning($"[IAP] Init failed: {error} — {message}");

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            string id = args.purchasedProduct.definition.id;
            if (id == AdFree.ProductId)
            {
                var save = SaveManager.Instance?.Current;
                AdFree.Grant(save);
                SaveManager.Instance?.SaveLocal();
                GameLogger.Info("[IAP] Ad-free purchased.");
                OnPurchaseComplete?.Invoke(id, true);
            }
            else if (CrystalAmounts.TryGetValue(id, out long amount))
            {
                PremiumShopService.Instance?.AwardCrystals(amount);
                GameLogger.Info($"[IAP] Purchase complete: {id} → +{amount}◆");
                OnPurchaseComplete?.Invoke(id, true);
            }
            else
            {
                GameLogger.Warning($"[IAP] Unknown product: {id}");
            }
            return PurchaseProcessingResult.Complete;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
        {
            GameLogger.Warning($"[IAP] Purchase failed: {product?.definition.id} — {failureReason}");
            OnPurchaseComplete?.Invoke(product?.definition.id ?? "", false);
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
        {
            GameLogger.Warning($"[IAP] Purchase failed: {product?.definition.id} — {failureDescription.reason} ({failureDescription.message})");
            OnPurchaseComplete?.Invoke(product?.definition.id ?? "", false);
        }

        // ── Fallback prices (shown before store initialises) ──────────────────
        private static string GetFallbackPrice(string productId) => productId switch
        {
            ProductCrystals600   => "$0.99",
            ProductCrystals3200  => "$4.99",
            ProductCrystals7500  => "$9.99",
            ProductCrystals20000 => "$19.99",
            AdFree.ProductId     => AdFree.FallbackPrice,
            _                    => "—",
        };
    }
}
#endif
