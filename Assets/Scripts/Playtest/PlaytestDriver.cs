#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MobileIdleBuilder.Playtest
{
    /// <summary>
    /// Runtime side of the AI playtest harness (see Playtests/README.md). Created on demand by
    /// the playtest MCP tools, lives until play mode ends. It:
    ///  - injects taps and drags through a virtual Input System mouse, so the HUD (via the
    ///    EventSystem) and world input (via InputUtils) see exactly what a player's finger does;
    ///  - keeps the game running and accepting that input while the Game View is unfocused;
    ///  - owns Time.timeScale while it exists: the playtest speed, dialogue pauses, and the
    ///    turn-based freeze that stops the game while the agent thinks;
    ///  - keeps GameClock (research, cooldowns, boosts) in step with the game time it lets run,
    ///    and feeds that player time to the active PlaytestBenchmark.
    /// All screen coordinates in this harness are pixels with a TOP-LEFT origin, matching the
    /// Game View screenshots; conversion to the Input System's bottom-left origin happens here.
    /// </summary>
    public sealed class PlaytestDriver : MonoBehaviour
    {
        public const float MinTimeScale = 0.1f;
        public const float MaxTimeScale = 50f;
        private const int TapHoldFrames = 2;

        private static PlaytestDriver s_Instance;

        private Mouse         _mouse;
        private InputSettings _originalInputSettings;
        private readonly Queue<Step> _steps = new();
        private Action _onGestureDone;
        private float  _timeScale = 1f;

        // Turn-based clock: the game only runs while _runRemaining (player seconds) is positive.
        private bool   _turnBased;
        private double _runRemaining;
        private Action _onRunDone;
        private DialogueController _dialogue;

        /// <summary>True while a queued tap/drag has not finished.</summary>
        public bool IsBusy => _steps.Count > 0;
        public float TimeScale => _timeScale;
        public bool TurnBased => _turnBased;
        /// <summary>Turn-based: player seconds each tap or drag costs (reaction + reading time).</summary>
        public double ActionSeconds { get; set; } = 2.0;
        public bool IsRunning => _runRemaining > 0;
        /// <summary>Player seconds the game has run since the driver started (absences excluded).</summary>
        public double PlayerSeconds { get; private set; }

        public static PlaytestDriver Instance => s_Instance;

        public static PlaytestDriver Ensure()
        {
            if (s_Instance != null) return s_Instance;
            // No DontSave hide flags: those survive exiting Play Mode, which would skip OnDestroy
            // and leave the focus-ignoring input settings in place in the Editor.
            var go = new GameObject("[PlaytestDriver]");
            DontDestroyOnLoad(go);
            return go.AddComponent<PlaytestDriver>();
        }

        void Awake()
        {
            s_Instance = this;
            Application.runInBackground = true;

            // By default the editor holds pointer/keyboard events back while the Game View is
            // unfocused, and the agent's terminal always has focus. Route everything to the game
            // for this session only; the original settings come back in OnDestroy.
            _originalInputSettings = InputSystem.settings;
            var settings = Instantiate(_originalInputSettings);
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            InputSystem.settings = settings;

            _mouse = InputSystem.AddDevice<Mouse>("PlaytestMouse");
            GameLogger.Info("[Playtest] Driver started (virtual mouse added, input ignores focus).");
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
            PlaytestBenchmark.Stop();
            GameClock.DevReset();
            if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
            if (_originalInputSettings != null) InputSystem.settings = _originalInputSettings;
        }

        // ── Gestures ──────────────────────────────────────────────────────────

        /// <summary>Queues a tap at a top-left-origin screen point.</summary>
        public void Tap(Vector2 screenTopLeft, int holdFrames, Action onDone)
        {
            var p = ToInputSpace(screenTopLeft);
            _steps.Enqueue(new Step(p, false));
            _steps.Enqueue(new Step(p, true));
            for (int i = 0; i < Mathf.Max(1, holdFrames); i++) _steps.Enqueue(new Step(p, true));
            _steps.Enqueue(new Step(p, false));
            Chain(onDone);
        }

        public void Tap(Vector2 screenTopLeft, Action onDone) => Tap(screenTopLeft, TapHoldFrames, onDone);

        /// <summary>Queues a press-move-release drag between two top-left-origin screen points.</summary>
        public void Drag(Vector2 fromTopLeft, Vector2 toTopLeft, int frames, Action onDone)
        {
            var a = ToInputSpace(fromTopLeft);
            var b = ToInputSpace(toTopLeft);
            frames = Mathf.Max(2, frames);
            _steps.Enqueue(new Step(a, false));
            _steps.Enqueue(new Step(a, true));
            for (int i = 1; i <= frames; i++) _steps.Enqueue(new Step(Vector2.Lerp(a, b, i / (float)frames), true));
            _steps.Enqueue(new Step(b, true));
            _steps.Enqueue(new Step(b, false));
            Chain(onDone);
        }

        private void Chain(Action onDone)
        {
            var previous = _onGestureDone;
            _onGestureDone = previous == null ? onDone : previous + onDone;
        }

        public static Vector2 ToInputSpace(Vector2 screenTopLeft) =>
            new(screenTopLeft.x, Screen.height - screenTopLeft.y);

        public static Vector2 ToTopLeft(Vector2 inputSpace) =>
            new(inputSpace.x, Screen.height - inputSpace.y);

        // ── Screenshots ───────────────────────────────────────────────────────

        /// <summary>
        /// Captures the composited frame (3D + UI Toolkit HUD) at the end of the next rendered
        /// frame. The callback receives null if the capture failed; the caller owns the texture.
        /// </summary>
        public void CaptureScreen(Action<Texture2D> onDone) => StartCoroutine(CaptureAtEndOfFrame(onDone));

        private System.Collections.IEnumerator CaptureAtEndOfFrame(Action<Texture2D> onDone)
        {
            yield return new WaitForEndOfFrame();
            Texture2D tex = null;
            try { tex = ScreenCapture.CaptureScreenshotAsTexture(); }
            catch (Exception e) { GameLogger.Warning($"[Playtest] Screen capture failed: {e.Message}"); }
            onDone?.Invoke(tex);
        }

        // ── Time ──────────────────────────────────────────────────────────────

        // Pausing is the turn-based clock's job (SetTurnBased), so the speed never goes to zero.
        public void SetTimeScale(float scale) => _timeScale = Mathf.Clamp(scale, MinTimeScale, MaxTimeScale);

        /// <summary>
        /// Turn-based: the game stands still (time scale 0, GameClock held) except while RunFor
        /// lets it run, so the agent's thinking time costs the player nothing. Off: the game runs
        /// continuously, as in a normal session.
        /// </summary>
        public void SetTurnBased(bool on)
        {
            _turnBased = on;
            if (!on) FinishRun();
        }

        /// <summary>Lets <paramref name="playerSeconds"/> of game time pass, then calls back.</summary>
        public void RunFor(double playerSeconds, Action onDone)
        {
            _runRemaining = Math.Max(_runRemaining, playerSeconds);
            _onRunDone   += onDone;
            if (_runRemaining <= 0) FinishRun();
        }

        /// <summary>Stops a RunFor early; the game freezes again on the next frame.</summary>
        public void CancelRun() => FinishRun();

        private void FinishRun()
        {
            _runRemaining = 0;
            var done = _onRunDone;
            _onRunDone = null;
            done?.Invoke();
        }

        private bool  _frozen;
        private float _nextDialogueLookup;

        private bool DialoguePausing
        {
            get
            {
                if (_dialogue == null && Time.realtimeSinceStartup >= _nextDialogueLookup)
                {
                    _dialogue = FindAnyObjectByType<DialogueController>();
                    _nextDialogueLookup = Time.realtimeSinceStartup + 1f;
                }
                return _dialogue != null && _dialogue.IsPausingGame;
            }
        }

        private void AdvanceClocks()
        {
            // Player time this frame: none while frozen for the agent; real reading time while a
            // dialogue pauses the game (the player is reading); otherwise scaled game time.
            float  unscaled = Time.unscaledDeltaTime;
            double played   = _frozen ? 0 : (Time.timeScale == 0f && DialoguePausing ? unscaled : Time.deltaTime);

            // Wall-clock timers follow the game time actually let through, so they hold still
            // while frozen and run N times faster at time scale N.
            GameClock.DevShift(TimeSpan.FromSeconds(played - unscaled));
            PlayerSeconds += played;
            PlaytestBenchmark.Active?.Tick(played);

            if (_runRemaining > 0)
            {
                _runRemaining -= played;
                if (_runRemaining <= 0) FinishRun();
            }
        }

        void Update()
        {
            AdvanceClocks();

            // One state event per frame: the Input System applies it at the start of the next
            // frame, so press, hold and release land on separate frames like a real touch.
            if (_steps.Count > 0)
            {
                var step = _steps.Dequeue();
                _mouse.MakeCurrent();
                var state = new MouseState { position = step.Position };
                if (step.Pressed) state = state.WithButton(MouseButton.Left);
                InputSystem.QueueStateEvent(_mouse, state);

                if (_steps.Count == 0)
                {
                    var done = _onGestureDone;
                    _onGestureDone = null;
                    done?.Invoke();
                }
            }
        }

        void LateUpdate()
        {
            // Dialogue and SceneLoader also write timeScale; the driver decides last each frame.
            // Never freeze mid-load: loading waits on scaled time and would stall.
            var bridge = ECSLoadBridge.Instance;
            bool settled = bridge != null && bridge.IsLoaded && !SceneLoader.IsTransitioning;
            _frozen = _turnBased && _runRemaining <= 0 && settled;

            float target = _frozen || DialoguePausing ? 0f : _timeScale;
            if (!Mathf.Approximately(Time.timeScale, target)) Time.timeScale = target;
        }

        private readonly struct Step
        {
            public readonly Vector2 Position;
            public readonly bool    Pressed;
            public Step(Vector2 position, bool pressed) { Position = position; Pressed = pressed; }
        }
    }
}
#endif
