using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Populates and manages the Quantum Domains panel (sites-panel): one row per build site
    /// with its lock/unlock state, entropy unlock cost, and a Travel/Unlock action.
    /// Sits as a component alongside HUDController on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class SitesSubController : MonoBehaviour
    {
        private VisualElement _panel;
        private Label         _entropyLabel;
        private ScrollView    _list;
        private Button        _navButton;
        private HUDController _hud;

        private EntityManager _em;
        private EntityQuery   _progressQuery;
        private bool          _ecsReady;

        private DialogueDatabaseSO _dialogueDb;

        /// <summary>Research gate that triggers the one-shot Quantum Domains introduction.</summary>
        internal const string DomainsGateResearchId  = "materials_science";
        private  const string DomainsIntroDialogueId  = "intro_quantum_domains";

        // Two-tap unlock confirm: the row whose Unlock button is armed, and the timer that disarms it.
        private int _pendingUnlockIndex = -1;
        private IVisualElementScheduledItem _pendingReset;

        private const long ConfirmWindowMs = 3000;

        /// <summary>What action a site row should present.</summary>
        public enum SiteRowAction { Active, Travel, Unlock, Locked }

        /// <summary>
        /// Pure classification of a site row's action from its state. Testable without ECS/UI.
        /// </summary>
        public static SiteRowAction ClassifyRow(bool isActive, bool isUnlocked, bool canAfford)
        {
            if (isActive)   return SiteRowAction.Active;
            if (isUnlocked) return SiteRowAction.Travel;
            if (canAfford)  return SiteRowAction.Unlock;
            return SiteRowAction.Locked;
        }

        /// <summary>
        /// Pure rule for the one-shot domains intro: fire once, when the gate research is
        /// unlocked. Testable without ECS/UI.
        /// </summary>
        public static bool ShouldShowDomainsIntro(string researchId, bool alreadySeen) =>
            !alreadySeen && researchId == DomainsGateResearchId;

        // ── One-shot domains introduction ────────────────────────────────────

        /// <summary>
        /// Routed from <see cref="HUDController"/>.OnResearchUnlocked. When the player unlocks
        /// the domains gate research for the first time, plays the Quantum Domains intro and
        /// highlights the Domains nav button.
        /// </summary>
        public void NotifyResearchUnlocked(ResearchSO research)
        {
            if (research == null) return;
            bool seen = SaveManager.Instance?.Current?.domainsIntroSeen ?? true;
            if (!ShouldShowDomainsIntro(research.id, seen)) return;
            PlayDomainsIntro();
        }

        private void PlayDomainsIntro()
        {
            var save = SaveManager.Instance?.Current;
            if (save != null)
            {
                save.domainsIntroSeen = true;
                SaveManager.Instance.SaveLocal();
            }

            _dialogueDb ??= Resources.Load<DialogueDatabaseSO>("DialogueDatabase");
            var dialogue   = _dialogueDb != null ? _dialogueDb.Get(DomainsIntroDialogueId) : null;
            var controller = FindAnyObjectByType<DialogueController>();

            if (dialogue != null && controller != null)
                controller.PlayDialogue(dialogue);
            else
                _hud?.ShowNotification("◈", "Quantum Domains unlocked — open the menu and tap Domains.");

            // Persisting highlight on the drawer nav button until the player opens the panel.
            _navButton?.AddToClassList("panel-btn--highlight");
        }

        /// <summary>Clears the Domains nav-button highlight. Called when the panel is opened.</summary>
        public void ClearNavHighlight() => _navButton?.RemoveFromClassList("panel-btn--highlight");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Dev-only: re-arm and replay the intro regardless of the seen flag.</summary>
        public void ReplayDomainsIntroForTesting()
        {
            if (SaveManager.Instance?.Current != null)
                SaveManager.Instance.Current.domainsIntroSeen = false;
            PlayDomainsIntro();
        }
#endif

        public void Init(VisualElement root, HUDController hud)
        {
            _hud          = hud;
            _panel        = root.Q("sites-panel");
            _entropyLabel = root.Q<Label>("sites-entropy");
            _list         = root.Q<ScrollView>("sites-list");
            _navButton    = root.Q<Button>("btn-sites");
            if (_list == null)
                GameLogger.Error("[SitesSubController] 'sites-list' not found in GameHUD.uxml.");
        }

        public void SetECSContext(EntityManager em)
        {
            _em            = em;
            _progressQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerProgressData>());
            _ecsReady      = true;
        }

        /// <summary>Rebuilds the panel contents with current site/entropy state.</summary>
        public void Refresh()
        {
            if (_list == null) return;
            _list.Clear();

            long entropy = GetCurrentEntropy();
            if (_entropyLabel != null)
                _entropyLabel.text = $"Entropy: {entropy:N0}";

            var svc = SiteService.Instance;
            if (svc?.AllSites == null || svc.AllSites.Count == 0)
            {
                _list.Add(new Label("Sites not available."));
                return;
            }

            int activeIndex = svc.ActiveIndex;
            for (int i = 0; i < svc.AllSites.Count; i++)
            {
                var site = svc.AllSites[i];
                if (site == null) continue;

                bool isActive   = i == activeIndex;
                bool isUnlocked = svc.IsUnlocked(i);
                bool canAfford  = entropy >= site.unlockCost;
                var  action     = ClassifyRow(isActive, isUnlocked, canAfford);

                _list.Add(BuildRow(i, site, action));
            }
        }

        private VisualElement BuildRow(int index, SiteSO site, SiteRowAction action)
        {
            var row = new VisualElement();
            row.AddToClassList("upgrade-row");
            if (action == SiteRowAction.Locked) row.AddToClassList("upgrade-row--locked");
            else if (action == SiteRowAction.Active) row.AddToClassList("upgrade-row--maxed");

            // Header: name + status badge
            var header = new VisualElement();
            header.AddToClassList("upgrade-row-header");

            var nameLabel = new Label(string.IsNullOrEmpty(site.displayName) ? site.id : site.displayName);
            nameLabel.AddToClassList("upgrade-row-name");

            string badge = action switch
            {
                SiteRowAction.Active => "Active",
                SiteRowAction.Travel => "Unlocked",
                _                    => "Locked",
            };
            var badgeLabel = new Label(badge);
            badgeLabel.AddToClassList("upgrade-row-level");

            header.Add(nameLabel);
            header.Add(badgeLabel);
            row.Add(header);

            // Footer: cost (locked) + action button
            var footer = new VisualElement();
            footer.AddToClassList("upgrade-row-footer");

            if (action == SiteRowAction.Unlock || action == SiteRowAction.Locked)
            {
                var costLabel = new Label($"◈ {site.unlockCost:N0}");
                costLabel.AddToClassList("upgrade-row-cost");
                if (action == SiteRowAction.Locked) costLabel.AddToClassList("upgrade-row-cost--unaffordable");
                footer.Add(costLabel);
            }

            switch (action)
            {
                case SiteRowAction.Active:
                {
                    var badgeBtn = new Button { text = "Current" };
                    badgeBtn.AddToClassList("craft-btn");
                    badgeBtn.SetEnabled(false);
                    footer.Add(badgeBtn);
                    break;
                }
                case SiteRowAction.Travel:
                {
                    var travelBtn = new Button { text = "Travel" };
                    travelBtn.AddToClassList("craft-btn");
                    int captured = index;
                    travelBtn.clicked += () => OnTravelPressed(captured);
                    footer.Add(travelBtn);
                    break;
                }
                case SiteRowAction.Unlock:
                {
                    bool armed = index == _pendingUnlockIndex;
                    var unlockBtn = new Button { text = armed ? "Confirm?" : "Unlock" };
                    unlockBtn.AddToClassList("craft-btn");
                    int captured = index;
                    unlockBtn.clicked += () => OnUnlockPressed(captured);
                    footer.Add(unlockBtn);
                    break;
                }
                case SiteRowAction.Locked:
                {
                    var lockedBtn = new Button { text = "Unlock" };
                    lockedBtn.AddToClassList("craft-btn");
                    lockedBtn.SetEnabled(false);
                    footer.Add(lockedBtn);
                    break;
                }
            }

            row.Add(footer);
            return row;
        }

        // ── Actions ──────────────────────────────────────────────────────────

        private void OnTravelPressed(int index)
        {
            var svc = SiteService.Instance;
            if (svc == null) return;

            // Cancel any active placement/conveyor/deconstruct overlay before swapping grids.
            _hud?.CancelActiveModes();

            if (svc.SwitchTo(index))
            {
                ClearPending();
                _hud?.ShowNotification("◈", $"Traveled to {SiteName(svc, index)}.");
                Refresh();
            }
        }

        private void OnUnlockPressed(int index)
        {
            var svc = SiteService.Instance;
            if (svc == null) return;

            // First tap arms the confirm; second tap within the window commits the spend.
            if (_pendingUnlockIndex != index)
            {
                ArmConfirm(index);
                Refresh();
                return;
            }

            ClearPending();
            var site = svc.GetSite(index);
            if (site != null && svc.UnlockSite(site.id))
                _hud?.ShowNotification("◈", $"Unlocked {SiteName(svc, index)}.");
            Refresh();
        }

        private void ArmConfirm(int index)
        {
            _pendingUnlockIndex = index;
            _pendingReset?.Pause();
            // Auto-disarm after the confirm window so a stale "Confirm?" can't commit later.
            if (_panel != null)
                _pendingReset = _panel.schedule.Execute(() =>
                {
                    if (_pendingUnlockIndex < 0) return;
                    ClearPending();
                    Refresh();
                }).StartingIn(ConfirmWindowMs);
        }

        private void ClearPending()
        {
            _pendingUnlockIndex = -1;
            _pendingReset?.Pause();
            _pendingReset = null;
        }

        private static string SiteName(SiteService svc, int index)
        {
            var s = svc.GetSite(index);
            return s == null ? $"site {index}" : (string.IsNullOrEmpty(s.displayName) ? s.id : s.displayName);
        }

        private long GetCurrentEntropy()
        {
            if (!_ecsReady || _progressQuery.IsEmpty) return 0L;
            return _progressQuery.GetSingleton<PlayerProgressData>().BaseCurrency;
        }
    }
}
