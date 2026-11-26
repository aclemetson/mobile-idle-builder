using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Small HUD chrome (#286, moved out of HUDController): the tooltip popup, the ⚡ power-connection toggle with
    /// its map overlay, and the "What's new" / game-data update notice decision. HUDController keeps one-line
    /// public forwarders (ShowTooltip, HideTooltip, MaybeShowGameUpdateNotice) because other classes call them.
    /// Sibling MonoBehaviour on the HUD GameObject (added at runtime when missing).
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class HudChromeSubController : MonoBehaviour
    {
        VisualElement _tooltip;
        Label         _tooltipTitle, _tooltipBody;

        Button _btnPowerToggle;
        PowerConnectionOverlayController _powerOverlay;
        bool   _powerConnectionsShown;

        public void Initialize(VisualElement root)
        {
            // Tooltip: inner element inside the "tooltip-instance" TemplateContainer
            _tooltip      = root.Q("tooltip-popup");
            _tooltipTitle = root.Q<Label>("tooltip-popup__title");
            _tooltipBody  = root.Q<Label>("tooltip-popup__body");
            HUDController.SetElementVisible(_tooltip, false);

            // Power-connection overlay toggle (⚡ button). Restores its persisted on/off state.
            _btnPowerToggle = root.Q<Button>("btn-power-toggle");
            if (_btnPowerToggle != null)
            {
                _powerConnectionsShown = SettingsService.Instance?.Current?.showPowerConnections ?? false;
                ApplyPowerConnectionState();
                _btnPowerToggle.clicked += TogglePowerConnections;
            }
        }

        // ── Tooltip ───────────────────────────────────────────────────────────

        /// <summary>Shows the tooltip near <paramref name="anchor"/>: above it, or below when there is no room.</summary>
        public void ShowTooltip(string title, string body, VisualElement anchor)
        {
            if (_tooltip == null) return;
            if (_tooltipTitle != null) _tooltipTitle.text = title;
            if (_tooltipBody  != null) _tooltipBody.text  = body;
            // Layout isn't known until after first render: position once geometry resolves.
            _tooltip.RegisterCallbackOnce<GeometryChangedEvent>(_ => PositionTooltip(anchor));
            HUDController.SetElementVisible(_tooltip, true);
        }

        public void HideTooltip() => HUDController.SetElementVisible(_tooltip, false);

        void PositionTooltip(VisualElement anchor)
        {
            if (anchor == null || _tooltip == null) return;
            var anchorBounds = anchor.worldBound;
            float top = anchorBounds.yMin - _tooltip.layout.height - 8f;
            if (top < 0f) top = anchorBounds.yMax + 8f;   // not enough room above: go below
            _tooltip.style.left = anchorBounds.xMin;
            _tooltip.style.top  = top;
        }

        // ── Power-connection overlay toggle (⚡ top-bar button) ─────────────────

        void TogglePowerConnections()
        {
            _powerConnectionsShown = !_powerConnectionsShown;
            ApplyPowerConnectionState();
            SettingsService.Instance?.SetShowPowerConnections(_powerConnectionsShown);
        }

        /// <summary>Syncs the button's on/off look and the map overlay to the current toggle state.</summary>
        void ApplyPowerConnectionState()
        {
            EnsurePowerOverlay()?.SetVisible(_powerConnectionsShown);
            _btnPowerToggle?.EnableInClassList("power-toggle-btn--active", _powerConnectionsShown);
        }

        /// <summary>
        /// Finds the map power-connection overlay, creating a runtime one if the scene has none (it self-wires to
        /// the GridRenderer), so no manual scene wiring is required.
        /// </summary>
        PowerConnectionOverlayController EnsurePowerOverlay()
        {
            if (_powerOverlay != null) return _powerOverlay;
            _powerOverlay = FindAnyObjectByType<PowerConnectionOverlayController>();
            if (_powerOverlay == null)
                _powerOverlay = new GameObject("PowerConnectionOverlay").AddComponent<PowerConnectionOverlayController>();
            return _powerOverlay;
        }

        // ── Update notice ─────────────────────────────────────────────────────

        /// <summary>
        /// Shows the game-data update notice once if the remote <c>gamedata.updatedUtc</c> stamp is newer than the
        /// saved marker, then records the marker so it fires once per published stamp; and the build's "What's
        /// new" notes once per text (#502). Called by ECSLoadBridge after the save is applied.
        /// </summary>
        public void MaybeShowGameUpdateNotice(GameUpdateNoticeSubController notice)
        {
            var save = SaveManager.Instance?.Current;

            string notes = FeatureFlags.WhatsNewNotes, notesVersion = FeatureFlags.WhatsNewVersion;
            var whatsNew = WhatsNew.Decide(Application.version, notesVersion, notes, save?.lastSeenWhatsNewHash,
                                           newPlayer: !(save?.tutorial.hasCompletedFirstRun ?? false));
            bool dataChanged = GameUpdateNotice.ShouldShow(FeatureFlags.GameDataUpdatedUtc, save?.lastSeenGameDataUtc);

            if (whatsNew == WhatsNew.Decision.Show)
            {
                // One card: the notes, then the balance notice when values changed too.
                string message = notes.Trim();
                if (dataChanged) message += "\n\n" + FeatureFlags.GameDataNoticeMessage;
                notice?.Show(WhatsNew.Title(notesVersion), message, leftAligned: true);   // bullet lists read left
            }
            else if (dataChanged)
            {
                notice?.Show(FeatureFlags.GameDataNoticeTitle, FeatureFlags.GameDataNoticeMessage);
            }
            if (dataChanged) TelemetryService.Instance?.RecordGameUpdateNotice(FeatureFlags.GameDataVersion);

            if (save == null || (whatsNew == WhatsNew.Decision.Hide && !dataChanged)) return;
            if (whatsNew != WhatsNew.Decision.Hide) save.lastSeenWhatsNewHash = WhatsNew.Hash(notes);
            if (dataChanged) save.lastSeenGameDataUtc = FeatureFlags.GameDataUpdatedUtc;
            SaveManager.Instance.SaveLocal();
        }
    }
}
