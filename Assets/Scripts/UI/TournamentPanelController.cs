using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The Tournament panel (<c>pve-panel</c> in GameHUD.uxml; PvE phases 4-5). Shows the tournament state from
    /// <see cref="TournamentService"/> with the league badge and the last result banner, starts a run (confirm
    /// card → server join → the normal prestige through <see cref="HUDController.RequestTournamentPrestige"/>),
    /// lists the bracket leaderboard at your own elapsed time, and claims rewards (crystals + item stars, with
    /// a card revealing where the stars landed).
    ///
    /// Also finishes a join whose prestige never ran (the app closed between the join and the prestige).
    /// Added to the HUD GameObject by <see cref="HUDController"/> (GetComponent ?? AddComponent): no scene wiring.
    /// </summary>
    public class TournamentPanelController : MonoBehaviour
    {
        HUDController _hud;
        VisualElement _panel;
        Label _state, _timer, _info, _completed, _badge, _banner, _yourRank;
        Button _start, _refresh, _claim;
        ScrollView _list;
        IVisualElementScheduledItem _ticker;

        bool _busy;
        bool _claiming;
        bool _subscribed;
        bool _pendingPrestigeHandled;
        string _message;              // last error / hint shown in the info line
        PveBoardResponse _board;

        public bool IsOpen => _panel != null && !_panel.ClassListContains("hidden");

        public void Init(VisualElement root, HUDController hud)
        {
            _hud       = hud;
            _panel     = root?.Q("pve-panel");
            if (_panel == null) { GameLogger.Warning("[PvE] pve-panel missing from GameHUD.uxml; tournament panel disabled."); return; }
            _state     = _panel.Q<Label>("pve-state-label");
            _timer     = _panel.Q<Label>("pve-timer-label");
            _info      = _panel.Q<Label>("pve-info-label");
            _completed = _panel.Q<Label>("pve-result-label");
            _badge     = _panel.Q<Label>("pve-league-badge");
            _banner    = _panel.Q<Label>("pve-banner");
            _yourRank  = _panel.Q<Label>("pve-your-rank");
            _start     = _panel.Q<Button>("btn-pve-start");
            _claim     = _panel.Q<Button>("btn-pve-claim");
            _refresh   = _panel.Q<Button>("btn-pve-refresh");
            _list      = _panel.Q<ScrollView>("pve-leaderboard-list");
            if (_start != null)   _start.clicked   += OnStartPressed;
            if (_claim != null)   _claim.clicked   += OnClaimPressed;
            if (_refresh != null) _refresh.clicked += () => _ = RefreshAllAsync();
            foreach (var l in new[] { _info, _completed }) if (l != null) l.style.whiteSpace = WhiteSpace.Normal;
        }

        public void Open()
        {
            if (_panel == null) return;
            _message = null;
            HUDController.SetElementVisible(_panel, true);
            Rebuild();
            _ = RefreshAllAsync();
            if (_ticker == null) _ticker = _panel.schedule.Execute(Tick).Every(1000);
            else _ticker.Resume();
        }

        void Tick()
        {
            if (!IsOpen) { _ticker?.Pause(); return; }
            Rebuild(rowsToo: false);
        }

        void Update()
        {
            var svc = TournamentService.Instance;
            if (svc == null) return;
            if (!_subscribed) { svc.OnChanged += OnServiceChanged; _subscribed = true; }

            // A join whose prestige never ran (app closed in between): run it now, once the game is loaded.
            if (!_pendingPrestigeHandled && svc.JoinPrestigePending && _hud != null && _hud.CanRequestPrestige
                && ECSLoadBridge.Instance != null && ECSLoadBridge.Instance.IsLoaded && !PrestigeRunReset.ReloadPending)
            {
                _pendingPrestigeHandled = true;
                GameLogger.Info("[PvE] Finishing the tournament join's prestige.");
                _hud.RequestTournamentPrestige();
            }
        }

        void OnDestroy()
        {
            if (_subscribed && TournamentService.Instance != null) TournamentService.Instance.OnChanged -= OnServiceChanged;
        }

        void OnServiceChanged() { if (IsOpen) Rebuild(); }

        async System.Threading.Tasks.Task RefreshAllAsync()
        {
            var svc = TournamentService.Instance;
            if (svc == null || !TournamentService.Enabled) return;
            await svc.RefreshAsync();
            var state = svc.PanelState;
            if (state == TournamentPanelState.Running || state == TournamentPanelState.Finished || state == TournamentPanelState.Results)
            {
                var board = await PveApi.Board();
                _board = string.IsNullOrEmpty(board.error) ? board : _board;
            }
            if (this != null) Rebuild();
        }

        void Rebuild(bool rowsToo = true)
        {
            var svc = TournamentService.Instance;
            if (_panel == null || svc == null) return;
            var state = svc.PanelState;
            var cfg = GameBootstrap.Instance != null ? GameBootstrap.Instance.gameConfig?.pve : null;
            var d = TournamentPanelText.Describe(state, svc.LastStatus, svc.Entry, TournamentService.Now, cfg,
                                                 svc.JoinPrestigePending);

            if (_state != null) _state.text = d.headline;
            if (_timer != null) _timer.text = d.timer;
            if (_info != null)
            {
                _info.text = string.IsNullOrEmpty(_message) ? d.info : _message;
                HUDController.SetElementVisible(_info, !string.IsNullOrEmpty(_info.text));
            }
            if (_completed != null)
            {
                _completed.text = d.result;
                HUDController.SetElementVisible(_completed, !string.IsNullOrEmpty(d.result));
            }
            if (_start != null)
            {
                HUDController.SetElementVisible(_start, d.showStart);
                _start.SetEnabled(!_busy);
                _start.text = _busy && !_claiming ? "Starting..." : "Start tournament";
            }

            // League badge (header), result banner, claim button, your rank.
            bool live = state != TournamentPanelState.Disabled && state != TournamentPanelState.Locked;
            string leagueId = svc.LastStatus?.profile?.league ?? svc.Entry?.league;
            if (_badge != null)
            {
                _badge.text = TournamentRules.LeagueName(cfg, leagueId);
                foreach (var l in TournamentPanelText.LeagueIds) _badge.EnableInClassList("pve-league-badge--" + l, l == leagueId);
                HUDController.SetElementVisible(_badge, live && !string.IsNullOrEmpty(leagueId));
            }
            var banner = TournamentPanelText.Banner(svc.LastStatus, cfg, state);
            if (_banner != null)
            {
                _banner.text = banner.text;
                _banner.EnableInClassList("pve-banner--promoted", banner.kind == "promoted");
                _banner.EnableInClassList("pve-banner--demoted", banner.kind == "demoted");
                HUDController.SetElementVisible(_banner, live && !string.IsNullOrEmpty(banner.text));
            }
            var reward = live ? TournamentRules.NextUnappliedReward(svc.LastStatus, SaveManager.Instance?.Current) : null;
            if (_claim != null)
            {
                HUDController.SetElementVisible(_claim, reward != null);
                _claim.SetEnabled(!_busy);
                if (reward != null) _claim.text = _claiming ? "Claiming..." : TournamentPanelText.ClaimButtonText(reward);
            }
            if (_yourRank != null) _yourRank.text = TournamentPanelText.YourRank(_board, state);
            if (rowsToo) BuildRows(state);
        }

        void BuildRows(TournamentPanelState state)
        {
            if (_list == null) return;
            _list.Clear();
            bool showBoard = state == TournamentPanelState.Running || state == TournamentPanelState.Finished
                             || state == TournamentPanelState.Results;
            if (!showBoard) return;
            if (_board == null || _board.rows == null || _board.rows.Count == 0)
            {
                _list.Add(new Label(_board == null ? "Loading the leaderboard..." : "No one else has started yet."));
                return;
            }
            int size = _board.rows.Count;
            foreach (var r in _board.rows)
            {
                var row = new VisualElement();
                row.AddToClassList("tournament-row");
                if (r.you) row.AddToClassList("tournament-row--you");
                if (r.projected) row.AddToClassList("tournament-row--projected");
                var zone = TournamentPanelText.Zone(r.rank, size, _board.promote, _board.demote);
                if (zone > 0) row.AddToClassList("tournament-row--promote");
                if (zone < 0) row.AddToClassList("tournament-row--demote");

                var rank = new Label($"#{r.rank}"); rank.AddToClassList("tournament-row__rank");
                var name = new Label(r.you ? $"{r.name} (you)" : r.name); name.AddToClassList("tournament-row__name");
                var value = new Label(TournamentPanelText.ValueText(r)); value.AddToClassList("tournament-row__value");
                var played = new Label(TournamentPanelText.PlayedText(r.elapsed)); played.AddToClassList("tournament-row__played");
                row.Add(rank); row.Add(name); row.Add(value); row.Add(played);
                _list.Add(row);
            }
            _list.Add(new Label(TournamentPanelText.BoardFootnote(_board)) { style = { whiteSpace = WhiteSpace.Normal, fontSize = 11, opacity = 0.7f } });
        }

        // ── Claim ─────────────────────────────────────────────────────────

        async void OnClaimPressed()
        {
            var svc = TournamentService.Instance;
            var reward = TournamentRules.NextUnappliedReward(svc?.LastStatus, SaveManager.Instance?.Current);
            if (svc == null || reward == null || _busy) return;
            _busy = _claiming = true;
            Rebuild(rowsToo: false);
            TournamentService.ClaimOutcome outcome;
            try { outcome = await svc.ClaimAsync(reward); }
            finally { _busy = _claiming = false; }
            if (this == null) return;
            Rebuild();
            if (!outcome.applied) return;
            string text = TournamentPanelText.RevealText(outcome.reward, outcome.starsLanded,
                id => ItemDatabase.GetStatic(id)?.displayName ?? id,
                id => ItemStars.Count(SaveManager.Instance?.Current, id),
                GameBootstrap.Instance != null ? GameBootstrap.Instance.gameConfig?.pve : null);
            ConfirmPrompt.Show("Tournament reward", text, "Nice!", onConfirm: null, cancelLabel: "Close", destructive: false);
        }

        // ── Start ─────────────────────────────────────────────────────────

        void OnStartPressed()
        {
            if (_busy) return;
            if (!PlayerNameService.HasName)
            {
                _message = "Choose a player name first: it's shown on the leaderboard.";
                Rebuild(rowsToo: false);
                GetComponent<PlayerNamePromptController>()?.OpenFromSettings();
                return;
            }
            var svc = TournamentService.Instance;
            var cfg = GameBootstrap.Instance != null ? GameBootstrap.Instance.gameConfig?.pve : null;
            string league = TournamentRules.LeagueName(cfg, svc?.LastStatus?.profile?.league);
            long gain = _hud != null ? _hud.CurrentPrestigePreview() : 0;
            ConfirmPrompt.Show("Start the tournament?", TournamentRules.JoinConfirmMessage(gain, league), "Start",
                               onConfirm: () => _ = JoinAsync(), destructive: true);
        }

        async System.Threading.Tasks.Task JoinAsync()
        {
            var svc = TournamentService.Instance;
            if (svc == null || _busy) return;
            _busy = true;
            _message = null;
            Rebuild(rowsToo: false);
            var r = await svc.JoinAsync();
            _busy = false;
            if (this == null) return;
            if (!string.IsNullOrEmpty(r.error))
            {
                _message = TournamentRules.ErrorText(r.error);
                Rebuild(rowsToo: false);
                return;
            }
            _pendingPrestigeHandled = true;
            _hud?.RequestTournamentPrestige();
            _hud?.ShowNotification("🏆", "Tournament started! You have 24 hours.");
        }
    }

    /// <summary>The Tournament panel's text. Pure, so it is unit-tested.</summary>
    public static class TournamentPanelText
    {
        public struct Lines
        {
            public string headline, timer, info, result;
            public bool showStart;
        }

        public static Lines Describe(TournamentPanelState state, PveStatusResponse status, TournamentSaveData entry,
                                     DateTime now, PveConfig cfg, bool prestigePending)
        {
            var d = new Lines();
            string league = TournamentRules.LeagueName(cfg, status?.profile?.league ?? entry?.league);
            DateTime? Parse(string iso) { try { return string.IsNullOrEmpty(iso) ? (DateTime?)null : PveRules.ParseIso(iso); } catch (FormatException) { return null; } }
            switch (state)
            {
                case TournamentPanelState.Disabled:
                    d.headline = "Tournaments are unavailable right now.";
                    break;
                case TournamentPanelState.Locked:
                    d.headline = "Locked";
                    d.info = "Finish the Chemistry tutorial to unlock weekly tournaments.";
                    break;
                case TournamentPanelState.Offline:
                    d.headline = "Can't reach the tournament server";
                    d.info = TournamentRules.ErrorText(status?.error ?? "network:");
                    break;
                case TournamentPanelState.Waiting:
                    d.headline = $"{league} league";
                    d.timer = $"Next tournament opens in {TournamentRules.FormatSpan(PveRules.NextEntry(now) - now)} ({TournamentRules.NextOpensLocal(now)}).";
                    d.result = LastResult(status, cfg);
                    break;
                case TournamentPanelState.Open:
                    d.headline = $"{league} league: this week's tournament is open!";
                    var closes = Parse(status?.phaseEndsUtc) ?? PveRules.PhaseEnds(now);
                    d.timer = $"Entry closes in {TournamentRules.FormatSpan(closes - now)}.";
                    d.info = "Start any time before entry closes. Starting prestiges your run; you then have 24 hours " +
                             "to reach the highest net worth in your bracket. The top players move up a league.";
                    d.result = LastResult(status, cfg);
                    d.showStart = true;
                    break;
                case TournamentPanelState.Running:
                    d.headline = prestigePending ? "Starting your run..." : $"{league} league: run in progress";
                    d.timer = $"{TournamentRules.FormatSpan(TournamentRules.Remaining(entry, now))} left";
                    d.info = "Values are compared at your own elapsed time. ~ marks an estimate for players behind you.";
                    break;
                case TournamentPanelState.Finished:
                    d.headline = "Your run is over";
                    var at = status?.week != null ? PveRules.SettleAfter(status.week) : now;
                    d.timer = at > now ? $"Final results in {TournamentRules.FormatSpan(at - now)}." : "Final results are being tallied.";
                    break;
                case TournamentPanelState.Results:
                    d.headline = "Results";
                    d.result = LastResult(status, cfg);
                    d.timer = $"Next tournament opens in {TournamentRules.FormatSpan(PveRules.NextEntry(now) - now)}.";
                    break;
            }
            d.timer ??= ""; d.info ??= ""; d.result ??= ""; d.headline ??= "";
            return d;
        }

        /// <summary>"Last tournament: rank 2 of 5, promoted to Silver. Reward waiting: 150 ◆ + 5 ★", or "".</summary>
        public static string LastResult(PveStatusResponse status, PveConfig cfg)
        {
            var h = status?.profile?.history;
            if (h == null || h.Count == 0) return "";
            var last = h[h.Count - 1];
            string move = last.result switch
            {
                "promoted" => $", promoted to {TournamentRules.LeagueName(cfg, last.newLeague)}",
                "demoted"  => $", moved down to {TournamentRules.LeagueName(cfg, last.newLeague)}",
                _          => $", staying in {TournamentRules.LeagueName(cfg, last.newLeague)}",
            };
            string text = $"Last tournament: rank {last.rank} of {last.size}{move}.";
            var reward = status.profile.pendingRewards?.FirstOrDefault(r => r.week == last.week);
            if (reward != null) text += $" Reward waiting: {reward.crystals:N0} ◆ + {reward.stars} ★";
            return text;
        }

        /// <summary>+1 = in the promotion zone, -1 = in the demotion zone, 0 = neither.</summary>
        public static int Zone(int rank, int size, int promote, int demote)
        {
            if (rank <= promote) return 1;
            if (demote > 0 && rank > size - demote) return -1;
            return 0;
        }

        public static string ValueText(PveBoardRow r) =>
            (r.projected ? "~" : "") + NumberFormat.Entropy(r.value);

        public static string PlayedText(int elapsedSeconds) => $"{elapsedSeconds / 3600f:0.0}h";

        /// <summary>League ids with a badge colour (pve-league-badge--{id} in GameHUD.uss).</summary>
        public static readonly string[] LeagueIds = { "bronze", "silver", "gold", "platinum", "diamond", "champion" };

        /// <summary>
        /// The result banner for the most recent settled tournament: shown on the Results screen and, until the
        /// next tournament starts, on the Waiting and Open screens. kind = promoted / demoted / stayed.
        /// </summary>
        public static (string text, string kind) Banner(PveStatusResponse status, PveConfig cfg, TournamentPanelState state)
        {
            if (state != TournamentPanelState.Results && state != TournamentPanelState.Waiting && state != TournamentPanelState.Open)
                return ("", "");
            var h = status?.profile?.history;
            if (h == null || h.Count == 0) return ("", "");
            var last = h[h.Count - 1];
            string league = TournamentRules.LeagueName(cfg, last.newLeague);
            return last.result switch
            {
                "promoted" => ($"Promoted to {league}!", "promoted"),
                "demoted"  => ($"Moved down to {league}", "demoted"),
                _          => ($"Staying in {league}", "stayed"),
            };
        }

        /// <summary>"Claim 150 ◆ + 5 ★".</summary>
        public static string ClaimButtonText(PveReward r)
        {
            if (r == null) return "";
            if (r.crystals > 0 && r.stars > 0) return $"Claim {r.crystals:N0} ◆ + {r.stars} ★";
            if (r.stars > 0) return $"Claim {r.stars} ★";
            return $"Claim {r.crystals:N0} ◆";
        }

        /// <summary>"You: #3 of 12" while a board is shown, else "".</summary>
        public static string YourRank(PveBoardResponse b, TournamentPanelState state)
        {
            bool shown = state == TournamentPanelState.Running || state == TournamentPanelState.Finished
                         || state == TournamentPanelState.Results;
            if (!shown || b?.rows == null) return "";
            var me = b.rows.Find(r => r.you);
            return me == null ? "" : $"You: #{me.rank} of {b.rows.Count}";
        }

        /// <summary>
        /// The reveal card: rank, crystals, then each item that got stars (grouped) with its new total and bonus,
        /// e.g. "★×2 Carbon: now ★5 (+100% value)".
        /// </summary>
        public static string RevealText(PveReward r, System.Collections.Generic.IReadOnlyList<string> starsLanded,
                                        Func<string, string> nameOf, Func<string, int> starsNow, PveConfig cfg = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"Rank {r.rank} of {r.size} in {TournamentRules.LeagueName(cfg, r.league)}.");
            if (r.crystals > 0) sb.Append($"\n+{r.crystals:N0} ◆");
            if (starsLanded != null && starsLanded.Count > 0)
            {
                var order = new System.Collections.Generic.List<string>();
                var counts = new System.Collections.Generic.Dictionary<string, int>();
                foreach (var id in starsLanded)
                {
                    if (!counts.ContainsKey(id)) { counts[id] = 0; order.Add(id); }
                    counts[id]++;
                }
                foreach (var id in order)
                {
                    int total = starsNow(id);
                    string got = counts[id] > 1 ? $"★×{counts[id]}" : "★";
                    sb.Append($"\n{got} {nameOf(id)}: now ★{total} (+{Mathf.RoundToInt(total * ItemStars.BonusPerStar * 100f)}% value)");
                }
            }
            return sb.ToString();
        }

        public static string BoardFootnote(PveBoardResponse b)
        {
            string at = b.settled ? "Final standings at 24h." : $"Compared at your time: {TournamentRules.FormatSpan(TimeSpan.FromSeconds(b.viewerT))} in.";
            string moves = b.promote > 0 || b.demote > 0 ? $" Top {b.promote} move up" + (b.demote > 0 ? $", bottom {b.demote} move down." : ".") : "";
            return at + moves;
        }
    }
}
