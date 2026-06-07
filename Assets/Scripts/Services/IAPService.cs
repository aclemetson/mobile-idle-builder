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
    /// Persists across scenes so the store is initialised once per session.
    ///
    /// Wire-up: add to the same GameObject as PremiumShopService in the MainMenu scene.
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
        public bool IsInitialized { get; private set; }

        private IStoreController   _controller;
        private IExtensionProvider _extensions;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
        }

        private void Start() => InitializePurchasing();

        private void InitializePurchasing()
        {
            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            builder.AddProduct(ProductCrystals600,   ProductType.Consumable);
            builder.AddProduct(ProductCrystals3200,  ProductType.Consumable);
            builder.AddProduct(ProductCrystals7500,  ProductType.Consumable);
            builder.AddProduct(ProductCrystals20000, ProductType.Consumable);
            UnityPurchasing.Initialize(this, builder);
        }

        public void BuyProduct(string productId)
        {
            if (!IsInitialized)
            {
                GameLogger.Warning("[IAP] Store not initialized — cannot buy.");
                return;
            }
            _controller.InitiatePurchase(productId);
        }

        /// <summary>Returns localised price string or the display price fallback.</summary>
        public string GetLocalizedPrice(string productId)
        {
            if (!IsInitialized) return GetFallbackPrice(productId);
            var product = _controller.products.WithID(productId);
            return product?.metadata.localizedPriceString ?? GetFallbackPrice(productId);
        }

        // ── IDetailedStoreListener ────────────────────────────────────────────

        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _controller  = controller;
            _extensions  = extensions;
            IsInitialized = true;
            GameLogger.Info("[IAP] Store initialized.");
        }

        public void OnInitializeFailed(InitializationFailureReason error)
            => GameLogger.Warning($"[IAP] Init failed: {error}");

        public void OnInitializeFailed(InitializationFailureReason error, string message)
            => GameLogger.Warning($"[IAP] Init failed: {error} — {message}");

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            string id = args.purchasedProduct.definition.id;
            if (CrystalAmounts.TryGetValue(id, out long amount))
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
            _                    => "—",
        };
    }
}
#endif
