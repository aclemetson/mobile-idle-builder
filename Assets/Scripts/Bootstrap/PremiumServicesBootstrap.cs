using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Ensures the persistent premium-economy singletons exist at startup without requiring
    /// manual scene placement. Runs after the first scene loads; creates PremiumShopService and
    /// IAPService on DontDestroyOnLoad GameObjects if they are not already present.
    ///
    /// Both services are SingletonMonoBehaviour with PersistAcrossScenes => true, so AddComponent
    /// runs their Awake synchronously (assigning Instance and calling DontDestroyOnLoad). Created
    /// once at the first scene, they survive every subsequent scene load. PremiumShopService is
    /// created first so IAPService.ProcessPurchase always has a target for AwardCrystals.
    /// </summary>
    public static class PremiumServicesBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Init()
        {
            if (PremiumShopService.Instance == null)
                new GameObject("[PremiumShopService]").AddComponent<PremiumShopService>();
#if UNITY_PURCHASING
            if (IAPService.Instance == null)
                new GameObject("[IAPService]").AddComponent<IAPService>();
#endif
        }
    }
}
