using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The Cyclotron Kick minigame (docs/agents/tasks/feature-cyclotron-kick.md): a drawer button, unlocked at
    /// the second prestige, opens a card with three steps:
    ///  - <b>intro</b>: the description, time until the next run (one an hour), Insight banked, Start;
    ///  - <b>play</b>: a 30 s <see cref="CyclotronRun"/> drawn by <see cref="CyclotronView"/>; tap the view
    ///    as the particle crosses the gap;
    ///  - <b>result</b>: the energy reached and the Insight it pays; Collect banks it
    ///    (<see cref="CyclotronProgress.Collect"/>).
    /// Closing mid-run ends the run and still pays for the energy reached; closing on the result collects.
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
        Button        _start;
        CyclotronView _view;
        CyclotronRun  _run;
        Step          _step = Step.Closed;
        float         _feedbackLeft, _nextStatus;
        readonly System.Random _rng = new System.Random();
        readonly object _modalOwner = new();

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
            "widens its orbit. The energy you reach in 30 s becomes Insight: a discount on your next research.";

        void Update()
        {
            if (_modal == null) return;
            var save = SaveManager.Instance?.Current;
            bool loaded = ECSLoadBridge.Instance != null && ECSLoadBridge.Instance.IsLoaded;
            bool unlocked = loaded && CyclotronProgress.IsUnlocked(save);
            HUDController.SetElementVisible(_drawerButton, unlocked);

            if (unlocked && Time.unscaledTime >= _nextStatus)
            {
                _nextStatus = Time.unscaledTime + 1f;
                _drawerButton.text = DrawerLabel(CyclotronProgress.CooldownLeft(save, GameClock.UtcNow));
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

        /// <summary>"Cyclotron (ready)", or the time left rounded up to the minute: "Cyclotron (42m)".</summary>
        internal static string DrawerLabel(TimeSpan left) =>
            left <= TimeSpan.Zero ? "Cyclotron (ready)" : $"Cyclotron ({(int)Math.Ceiling(left.TotalMinutes)}m)";

        /// <summary>Countdown for the card: "42m 05s".</summary>
        internal static string FormatLeft(TimeSpan left) => $"{(int)left.TotalMinutes}m {left.Seconds:00}s";

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
            var left = CyclotronProgress.CooldownLeft(save, GameClock.UtcNow);
            if (_status != null) _status.text = StatusText(left, save.researchInsight);
            if (_start != null)
            {
                _start.SetEnabled(left <= TimeSpan.Zero);
                _start.text = left <= TimeSpan.Zero ? "Start run" : $"Ready in {FormatLeft(left)}";
            }
        }

        internal static string StatusText(TimeSpan left, float insight)
        {
            string s = left <= TimeSpan.Zero ? "Ready to run" : $"Next run in {FormatLeft(left)} (one run an hour)";
            s += $"\nInsight banked: {Mathf.RoundToInt(insight * 100f)}% off your next research " +
                 $"(max {Mathf.RoundToInt(CyclotronProgress.MaxInsight * 100f)}%)";
            return s;
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
            if (_resultText != null) _resultText.text = ResultText(_run.Energy, _run.Kicks, _run.Perfects);
        }

        internal static string ResultText(float energy, int kicks, int perfects) =>
            $"Collision at {Mathf.RoundToInt(energy * 100f)}% energy ({kicks} kick{(kicks == 1 ? "" : "s")}, {perfects} perfect).\n" +
            $"+{Mathf.RoundToInt(CyclotronProgress.InsightFor(energy) * 100f)}% Insight off your next research.";

        void CollectAndClose()
        {
            var save = SaveManager.Instance?.Current;
            if (_run != null && save != null)
            {
                float added = CyclotronProgress.Collect(save, _run.Energy, _rng.NextDouble(), out int crystals);
                SaveManager.Instance.SaveLocal();
                string msg = $"Cyclotron: +{Mathf.RoundToInt(added * 100f)}% Insight (now {Mathf.RoundToInt(save.researchInsight * 100f)}% off your next research)";
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
