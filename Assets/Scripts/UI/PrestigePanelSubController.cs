using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The Prestige panel (prestige-panel in GameHUD.uxml) and the prestige requests (#286, moved out of
    /// HUDController): the summary and payout preview, Confirm, the Megastructure's Ascension, the prestige that
    /// joins a tournament, and the "end your tournament run first" prompt (#505). Requests set
    /// PlayerProgressData.PrestigeRequested for PrestigeSystem. HUDController keeps its public prestige methods as
    /// one-line forwarders (other controllers call them) and closes other panels before <see cref="Open"/>.
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class PrestigePanelSubController : MonoBehaviour
    {
        HUDController _hud;
        EntityManager _em;
        EntityQuery   _progressQuery, _prestigeQuery;
        bool          _ecsReady;

        VisualElement _panel;
        Label         _summary, _currency;

        public void Initialize(VisualElement root, HUDController hud)
        {
            _hud      = hud;
            _panel    = root.Q("prestige-panel");
            _summary  = root.Q<Label>("prestige-summary");
            _currency = root.Q<Label>("prestige-currency");
            var confirm = root.Q<Button>("btn-confirm-prestige");
            if (confirm != null) confirm.clicked += Confirm;
        }

        public void SetECSContext(EntityManager em)
        {
            _em            = em;
            _progressQuery = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
            _prestigeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PrestigeData>());
            _ecsReady      = true;
        }

        /// <summary>True once ECS is ready for a prestige request.</summary>
        public bool CanRequest => _ecsReady && !_progressQuery.IsEmpty;

        double NetWorth => CanRequest ? _em.GetComponentData<PlayerProgressData>(_progressQuery.GetSingletonEntity()).NetWorth : 0d;

        /// <summary>Prestige currency a prestige would pay now (the tournament join card shows it).</summary>
        public long Preview() => CanRequest ? HudText.CalculatePrestigePreview(NetWorth) : 0;

        /// <summary>Fills and shows the panel. The caller closes other panels first. False when ECS is not ready.</summary>
        public bool Open()
        {
            if (!CanRequest || _prestigeQuery.IsEmpty) return false;

            var progress = _em.GetComponentData<PlayerProgressData>(_progressQuery.GetSingletonEntity());
            var prestige = _em.GetComponentData<PrestigeData>(_prestigeQuery.GetSingletonEntity());

            // Mirror the rate used in PrestigeSystem
            long preview = HudText.CalculatePrestigePreview(progress.NetWorth);
            long held    = prestige.PrestigeCurrency - prestige.PrestigeCurrencySpent;

            if (_summary != null) _summary.text = $"Net worth: {progress.NetWorth:N0}" +
                (TournamentService.Instance != null && TournamentService.Instance.PrestigeLeavesRun
                    ? "\nThis ends your tournament run early." : "");
            if (_currency != null) _currency.text = $"You will receive: {preview} ✦   (current: {held} ✦)";

            HUDController.SetElementVisible(_panel, true);
            return true;
        }

        void Request()
        {
            var entity   = _progressQuery.GetSingletonEntity();
            var progress = _em.GetComponentData<PlayerProgressData>(entity);
            progress.PrestigeRequested = true;
            _em.SetComponentData(entity, progress);
        }

        /// <summary>The panel's Confirm: prestige now (after ending a tournament run, when one is on).</summary>
        public void Confirm()
        {
            if (!CanRequest) return;
            if (LeavesTournamentFirst(Confirm)) return;
            Request();
            HUDController.SetElementVisible(_panel, false);
            _hud?.ShowNotification("✦", "Prestige complete! Starting a new run.");
        }

        /// <summary>The Megastructure panel's Ascend button: a prestige that also applies <see cref="AscensionRules"/>.</summary>
        public void RequestAscension()
        {
            if (!CanRequest) return;
            if (LeavesTournamentFirst(RequestAscension)) return;
            AscensionRules.Pending = true;
            Confirm();
            _hud?.ShowNotification("◉", "Ascension! The worlds begin again; the megastructure grows.");
        }

        /// <summary>The prestige that starts a tournament run: the normal prestige, exempt from the tournament lock.</summary>
        public bool RequestTournamentPrestige()
        {
            if (!CanRequest) return false;
            Request();
            return true;
        }

        /// <summary>
        /// During a tournament run a prestige or Ascension ends the run early (#505): asks, retires on the server at
        /// the current net worth, then runs <paramref name="then"/>. Returns false (and does nothing) when no run is
        /// on, so the caller goes ahead.
        /// </summary>
        bool LeavesTournamentFirst(System.Action then)
        {
            var svc = TournamentService.Instance;
            if (svc == null || !svc.PrestigeLeavesRun) return false;
            ConfirmPrompt.Show("End your tournament run?", TournamentRules.PrestigeLeaveMessage(NetWorth, svc.RunRemaining),
                "End run & prestige", onConfirm: async () =>
                {
                    string error = await svc.RetireAsync();
                    if (this == null) return;
                    if (error != null)
                    {
                        _hud?.ShowNotification("⚠", TournamentRules.ErrorText(error) + " Prestige not done.", "warning");
                        return;
                    }
                    then?.Invoke();
                });
            return true;
        }
    }
}
