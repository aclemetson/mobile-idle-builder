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
    ///  - holds a playtest time scale that survives dialogue pauses and scene loads.
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

        /// <summary>True while a queued tap/drag has not finished.</summary>
        public bool IsBusy => _steps.Count > 0;
        public float TimeScale => _timeScale;

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

        // ── Time scale ────────────────────────────────────────────────────────

        // No zero: a playtest pause would fight the dialogue pause over the same global.
        public void SetTimeScale(float scale) => _timeScale = Mathf.Clamp(scale, MinTimeScale, MaxTimeScale);

        void Update()
        {
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
            // Dialogue pauses with timeScale 0 and SceneLoader resets it to 1; leave a pause
            // alone, otherwise re-apply the playtest speed.
            if (Time.timeScale != 0f && !Mathf.Approximately(Time.timeScale, _timeScale))
                Time.timeScale = _timeScale;
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
