using System;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The Cyclotron Kick minigame (docs/agents/tasks/feature-cyclotron-kick.md): a drawer button, unlocked at
    /// the second prestige, opens a card with three steps:
    ///  - <b>intro</b>: the description, time until the next run, the boost running (if any), Start, and
    ///    the two Insight upgrades (Longer Boost, Faster Recharge);
    ///  - <b>play</b>: a 30 s <see cref="CyclotronRun"/> drawn by <see cref="CyclotronView"/>; tap the view
    ///    as the particle crosses the gap;
    ///  - <b>result</b>: the energy reached, the entropy boost and the Insight it pays; Collect starts the
    ///    boost and banks the Insight (<see cref="CyclotronProgress.Collect"/>).
    /// Closing mid-run ends the run and still pays for the energy reached; closing on the result collects.
    ///
    /// It also keeps the <see cref="EntropyIncomeBoost"/> ECS singleton in step with the save's boost, so the
    /// income systems apply it and drop it the moment it runs out.
    ///
    /// Added to the HUD GameObject by <see cref="HUDController"/> (GetComponent ?? AddComponent), so no
    /// scene wiring is needed.
    /// </summary>
    public class CyclotronController : MonoBehaviour
    {
        const float MaxStep = 1f / 120f;   // physics sub-step, so a gap window is never jumped over

        enum Step { Closed, Intro, Play, Result }

        HUDController _hud;
        Button        _drawerButton;
        VisualElement _modal, _intro, _play, _result, _energyFill;
        Label         _introText, _status, _energyLabel, _timerLabel, _feedback, _resultText;
        Label         _insightLabel, _boostUpText, _cooldownUpText;
        Button        _start, _boostUp, _cooldownUp;
        CyclotronView _view;
        CyclotronRun  _run;
        Step          _step = Step.Closed;
        float         _feedbackLeft, _nextStatus;
        readonly System.Random _rng = new System.Random();
        readonly object _modalOwner = new();
        EntityManager _em;
        EntityQuery   _boostQuery;
        bool          _ecsReady;
        float         _syncedBoost = -1f;

        public bool IsOpen => _step != Step.Closed;

        public void Init(VisualElement root, HUDController hud)
        {
            _hud          = hud;
            _drawerButton = root?.Q<Button>("btn-cyclotron");
            _modal        = root?.Q("cyclotron-modal");
            if (_drawerButton == null || _modal == null)
            {
                GameLogger.Warning("[Cyclotron] btn-cyclotron / cyclotron-modal missing from GameHUD.uxml; minigame disabled.");
                return;
            }

            _intro       = _modal.Q("cyclotron__intro");
            _play        = _modal.Q("cyclotron__play");
            _result      = _modal.Q("cyclotron__result");
            _introText   = _modal.Q<Label>("cyclotron__intro-text");
            _status      = _modal.Q<Label>("cyclotron__status");
            _start       = _modal.Q<Button>("cyclotron__start");
            _energyLabel = _modal.Q<Label>("cyclotron__energy");
            _timerLabel  = _modal.Q<Label>("cyclotron__timer");
            _energyFill  = _modal.Q("cyclotron__energy-fill");
            _feedback    = _modal.Q<Label>("cyclotron__feedback");
            _resultText  = _modal.Q<Label>("cyclotron__result-text");
            _view        = _modal.Q<CyclotronView>("cyclotron__view");
            _insightLabel   = _modal.Q<Label>("cyclotron__insight");
            _boostUpText    = _modal.Q<Label>("cyclotron__boost-upgrade-text");
            _cooldownUpText = _modal.Q<Label>("cyclotron__cooldown-upgrade-text");
            _boostUp        = _modal.Q<Button>("cyclotron__boost-upgrade");
            _cooldownUp     = _modal.Q<Button>("cyclotron__cooldown-upgrade");
            if (_boostUp != null)    _boostUp.clicked    += () => BuyUpgrade(CyclotronProgress.Upgrade.Boost);
            if (_cooldownUp != null) _cooldownUp.clicked += () => BuyUpgrade(CyclotronProgress.Upgrade.Cooldown);

            _drawerButton.clicked += Open;
            if (_start != null) _start.clicked += StartRun;
            var collect = _modal.Q<Button>("cyclotron__collect");
            if (collect != null) collect.clicked += CollectAndClose;
            var close = _modal.Q<Button>("cyclotron__close");
            if (close != null) close.clicked += Close;
            // Taps anywhere on the machine kick the particle; PointerDown so a fast tap isn't lost to a click.
            _view?.RegisterCallback<PointerDownEvent>(_ => OnTap());

            if (_introText != null) _introText.text = IntroText;
            HUDController.SetElementVisible(_modal, false);
            HUDController.SetElementVisible(_drawerButton, false);
        }

        internal const string IntroText =
            "Kick the particle each time it crosses the vertical gap. Every good kick adds energy and " +
            "widens its orbit. The energy you reach in 30 s sets an entropy boost (x1.5 to x3 on everything " +
            "your factory earns) and pays Insight, which upgrades the cyclotron below.";

        void Update()
        {
            if (_modal == null) return;
            var save = SaveManager.Instance?.Current;
            bool loaded = ECSLoadBridge.Instance != null && ECSLoadBridge.Instance.IsLoaded;
            bool unlocked = loaded && CyclotronProgress.IsUnlocked(save);
            HUDController.SetElementVisible(_drawerButton, unlocked);
            if (loaded) SyncBoost(CyclotronProgress.BoostMultiplier(save, GameClock.UtcNow));

            if (unlocked && Time.unscaledTime >= _nextStatus)
            {
                _nextStatus = Time.unscaledTime + 1f;
                var now = GameClock.UtcNow;
                _drawerButton.text = DrawerLabel(CyclotronProgress.CooldownLeft(save, now),
                                                 CyclotronProgress.BoostMultiplier(save, now),
                                                 CyclotronProgress.BoostLeft(save, now));
                if (_step == Step.Intro) RefreshIntro(save);
            }

            if (_step != Step.Play || _run == null) return;

            float dt = Time.unscaledDeltaTime;
            while (dt > 0f && !_run.Done)
            {
                float step = Mathf.Min(dt, MaxStep);
                _run.Tick(step);
                dt -= step;
            }
            _view?.Animate(Time.unscaledDeltaTime);
            if (_energyFill != null) _energyFill.style.width = Length.Percent(_run.Energy * 100f);
            if (_energyLabel != null) _energyLabel.text = $"Energy {Mathf.RoundToInt(_run.Energy * 100f)}%";
            if (_timerLabel != null) _timerLabel.text = $"{Mathf.CeilToInt(_run.TimeLeft)}s";
            if (_feedbackLeft > 0f)
            {
                _feedbackLeft -= Time.unscaledDeltaTime;
                if (_feedbackLeft <= 0f && _feedback != null) _feedback.text = "";
            }
            if (_run.Done) ShowResult();
        }

        /// <summary>
        /// "Cyclotron (x2.4 4m)" while a boost runs, else "Cyclotron (ready)" or the cooldown rounded up to
        /// the minute, "Cyclotron (42m)".
        /// </summary>
        internal static string DrawerLabel(TimeSpan cooldownLeft, float boost = 1f, TimeSpan boostLeft = default)
        {
            if (boost > 1f && boostLeft > TimeSpan.Zero)
                return $"Cyclotron ({FormatMult(boost)} {CeilMinutes(boostLeft)}m)";
            return cooldownLeft <= TimeSpan.Zero ? "Cyclotron (ready)" : $"Cyclotron ({CeilMinutes(cooldownLeft)}m)";
        }

        static int CeilMinutes(TimeSpan t) => (int)Math.Ceiling(t.TotalMinutes);

        /// <summary>Countdown for the card: "42m 05s".</summary>
        internal static string FormatLeft(TimeSpan left) => $"{(int)left.TotalMinutes}m {left.Seconds:00}s";

        /// <summary>"x2.4"; whole multipliers drop the decimal: "x3".</summary>
        internal static string FormatMult(float m) =>
            "x" + m.ToString(Math.Abs(m - Mathf.Round(m)) < 0.05f ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture);

        static string FormatMinutes(double minutes) => $"{minutes:0} min";

        void Open()
        {
            var save = SaveManager.Instance?.Current;
            if (!CyclotronProgress.IsUnlocked(save)) return;
            _hud?.CloseMenuDrawer();
            ShowStep(Step.Intro);
            RefreshIntro(save);
            HUDController.SetElementVisible(_modal, true);
            UIInputBlocker.SetModal(_modalOwner, true);
        }

        void RefreshIntro(SaveData save)
        {
            if (save == null) return;
            var now  = GameClock.UtcNow;
            var left = CyclotronProgress.CooldownLeft(save, now);
            if (_status != null)
                _status.text = StatusText(left, CyclotronProgress.CooldownMinutes(save), CyclotronProgress.BoostMinutes(save),
                                          CyclotronProgress.BoostMultiplier(save, now), CyclotronProgress.BoostLeft(save, now));
            if (_start != null)
            {
                _start.SetEnabled(left <= TimeSpan.Zero);
                _start.text = left <= TimeSpan.Zero ? "Start run" : $"Ready in {FormatLeft(left)}";
            }
            if (_insightLabel != null) _insightLabel.text = $"Insight: {save.cyclotronInsight}";
            RefreshUpgrade(save, CyclotronProgress.Upgrade.Boost,    _boostUpText,    _boostUp);
            RefreshUpgrade(save, CyclotronProgress.Upgrade.Cooldown, _cooldownUpText, _cooldownUp);
        }

        void RefreshUpgrade(SaveData save, CyclotronProgress.Upgrade u, Label text, Button button)
        {
            int level = CyclotronProgress.Level(save, u);
            bool maxed = CyclotronProgress.IsMaxed(save, u);
            if (text != null) text.text = UpgradeText(u, level);
            if (button == null) return;
            button.text = maxed ? "Maxed" : $"{CyclotronProgress.UpgradeCost(u, level)} Insight";
            button.SetEnabled(CyclotronProgress.CanUpgrade(save, u));
        }

        /// <summary>"Longer Boost (Lv 2): boost lasts 7 min -> 8 min"; at the cap just the current value.</summary>
        internal static string UpgradeText(CyclotronProgress.Upgrade u, int level)
        {
            bool boost = u == CyclotronProgress.Upgrade.Boost;
            string name = boost ? "Longer Boost" : "Faster Recharge";
            string what = boost ? "boost lasts" : "one run every";
            double cur = boost ? CyclotronProgress.BoostMinutes(level) : CyclotronProgress.CooldownMinutes(level);
            if (level >= CyclotronProgress.MaxLevel(u))
                return $"{name} (Lv {level}, max): {what} {FormatMinutes(cur)}";
            double next = boost ? CyclotronProgress.BoostMinutes(level + 1) : CyclotronProgress.CooldownMinutes(level + 1);
            return $"{name} (Lv {level}): {what} {FormatMinutes(cur)} -> {FormatMinutes(next)}";
        }

        internal static string StatusText(TimeSpan left, double cooldownMinutes, double boostMinutes,
                                          float boost = 1f, TimeSpan boostLeft = default)
        {
            string s = left <= TimeSpan.Zero ? "Ready to run"
                                             : $"Next run in {FormatLeft(left)} (one run every {FormatMinutes(cooldownMinutes)})";
            s += boost > 1f && boostLeft > TimeSpan.Zero
                ? $"\nBoost active: {FormatMult(boost)} entropy for {FormatLeft(boostLeft)}"
                : $"\nA run's boost lasts {FormatMinutes(boostMinutes)}";
            return s;
        }

        void BuyUpgrade(CyclotronProgress.Upgrade u)
        {
            var save = SaveManager.Instance?.Current;
            if (!CyclotronProgress.TryUpgrade(save, u)) return;
            SaveManager.Instance.SaveLocal();
            RefreshIntro(save);
        }

        /// <summary>
        /// Writes the live boost into the <see cref="EntropyIncomeBoost"/> singleton (creating it the first
        /// time) whenever it changes, so the income systems start and stop applying it on the same frame.
        /// </summary>
        void SyncBoost(float mult)
        {
            if (!_ecsReady)
            {
                var world = World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated) return;
                _em = world.EntityManager;
                _boostQuery = _em.CreateEntityQuery(ComponentType.ReadWrite<EntropyIncomeBoost>());
                _ecsReady = true;
            }
            if (_boostQuery.IsEmptyIgnoreFilter)
            {
                var e = _em.CreateEntity(typeof(EntropyIncomeBoost));
                _em.SetName(e, "EntropyIncomeBoost");
                _syncedBoost = -1f;
            }
            if (Mathf.Approximately(mult, _syncedBoost)) return;
            _boostQuery.SetSingleton(new EntropyIncomeBoost { Mult = mult });
            _syncedBoost = mult;
        }

        void StartRun()
        {
            var save = SaveManager.Instance?.Current;
            if (!CyclotronProgress.TryStartRun(save, GameClock.UtcNow)) { RefreshIntro(save); return; }
            SaveManager.Instance?.SaveLocal();
            _run = new CyclotronRun();
            if (_view != null) _view.Run = _run;
            if (_feedback != null) _feedback.text = "Tap as it crosses the gap!";
            _feedbackLeft = 2f;
            ShowStep(Step.Play);
        }

        void OnTap()
        {
            if (_step != Step.Play || _run == null || _run.Done) return;
            var kick = _run.Tap();
            _view?.Flash(kick == CyclotronRun.Kick.Miss);
            if (_feedback != null)
            {
                _feedback.text = kick switch
                {
                    CyclotronRun.Kick.Perfect => _run.Combo > 1 ? $"Perfect! x{_run.Combo}" : "Perfect!",
                    CyclotronRun.Kick.Good    => "Kick",
                    _                         => "Mistimed",
                };
                _feedbackLeft = 0.8f;
            }
        }

        void ShowResult()
        {
            if (_run == null) return;
            ShowStep(Step.Result);
            if (_resultText != null)
                _resultText.text = ResultText(_run.Energy, _run.Kicks, _run.Perfects,
                                              CyclotronProgress.BoostMinutes(SaveManager.Instance?.Current));
        }

        internal static string ResultText(float energy, int kicks, int perfects, double boostMinutes) =>
            $"Collision at {Mathf.RoundToInt(energy * 100f)}% energy ({kicks} kick{(kicks == 1 ? "" : "s")}, {perfects} perfect).\n" +
            $"{FormatMult(CyclotronProgress.MultiplierFor(energy))} entropy for {FormatMinutes(boostMinutes)} " +
            $"and +{CyclotronProgress.InsightFor(energy)} Insight.";

        void CollectAndClose()
        {
            var save = SaveManager.Instance?.Current;
            if (_run != null && save != null)
            {
                CyclotronProgress.Collect(save, _run.Energy, _rng.NextDouble(), GameClock.UtcNow,
                                          out float mult, out int insight, out int crystals);
                SaveManager.Instance.SaveLocal();
                SyncBoost(mult);
                string msg = $"Cyclotron: {FormatMult(mult)} entropy for {FormatMinutes(CyclotronProgress.BoostMinutes(save))}, " +
                             $"+{insight} Insight";
                if (crystals > 0) msg += $", +{crystals} crystals";
                _hud?.ShowNotification("⚛", msg);
            }
            _run = null;
            Hide();
        }

        /// <summary>X / Android back. Mid-run it ends the run (still paid); on the result it collects.</summary>
        public void Close()
        {
            switch (_step)
            {
                case Step.Play:   ShowResult(); break;          // ends early; the result step pays
                case Step.Result: CollectAndClose(); break;
                default:          Hide(); break;
            }
        }

        void Hide()
        {
            _step = Step.Closed;
            if (_view != null) _view.Run = null;
            HUDController.SetElementVisible(_modal, false);
            UIInputBlocker.SetModal(_modalOwner, false);
        }

        void ShowStep(Step step)
        {
            _step = step;
            HUDController.SetElementVisible(_intro,  step == Step.Intro);
            HUDController.SetElementVisible(_play,   step == Step.Play);
            HUDController.SetElementVisible(_result, step == Step.Result);
        }

        void OnDisable() => UIInputBlocker.SetModal(_modalOwner, false);
    }
}
