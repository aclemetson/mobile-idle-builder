using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The left drawer (left-drawer + drawer-backdrop in GameHUD.uxml) and the docking of the tutorial dialogue and
    /// "Show hint" pill beside an open slide panel (#286, moved out of HUDController). The drawer slides in on the ≡
    /// handle (a pan that starts on the handle is not a tap), closes on the backdrop, and is hidden (not just slid
    /// away) once closed so its scroller can never peek in at the left edge (#196). DrawerTapGuard drops the tap that
    /// opened it. HUDController keeps the panel registry and CloseAllPanels, and listens to <see cref="Opened"/> /
    /// <see cref="Closed"/> (tutorial event, achievements hint pulse). Sibling MonoBehaviour on the HUD GameObject.
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class DrawerSubController : MonoBehaviour
    {
        // Matches .left-drawer transition-duration (0.2s) in GameHUD.uss, plus a frame of slack.
        const long DrawerSlideMs = 250;

        // Smallest free strip the tutorial dialogue / "Show hint" pill will move into (panel units).
        const float DialogueDockMinHeight = 200f, DialogueDockMinWidth = 240f;
        const float PillDockMinHeight     = 52f,  PillDockMinWidth     = 120f;
        const float TopBarHeight          = 60f;  // --top-bar-height in tokens.uss

        readonly DrawerTapGuard _tapGuard = new DrawerTapGuard(DrawerSlideMs / 1000.0);
        VisualElement _drawer, _backdrop;
        VisualElement _hudFrame, _dialogueBox, _hintRestore;
        PanelDock _pillDock = PanelDock.None, _dialogueDock = PanelDock.None;

        public bool IsOpen { get; private set; }

        public event Action Opened;
        public event Action Closed;

        public void Initialize(VisualElement root)
        {
            _drawer   = root.Q("left-drawer");
            _backdrop = root.Q("drawer-backdrop");
            if (_drawer != null) _drawer.style.visibility = Visibility.Hidden;
            _tapGuard.Attach(_drawer);

            _hudFrame    = root.Q("root") ?? root;
            _dialogueBox = root.Q("dialogue-box");
            _hintRestore = root.Q("tutorial-hint-restore");

            // A pan that starts on the handle also ends as a click; ignore clicks where the pointer moved.
            var handle = root.Q<Button>("btn-drawer-handle");
            if (handle != null)
            {
                Vector2 downPos = Vector2.zero;
                handle.RegisterCallback<PointerDownEvent>(e => downPos = e.position, TrickleDown.TrickleDown);
                handle.RegisterCallback<ClickEvent>(e => { if (HudText.IsTap(downPos, e.position, 12f)) Toggle(); });
            }
            _backdrop?.RegisterCallback<ClickEvent>(_ => Close());
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else        Open();
        }

        public void Open()
        {
            if (_drawer == null) return;
            IsOpen = true;
            _tapGuard.MarkOpened();
            _drawer.style.visibility = Visibility.Visible;
            _drawer.AddToClassList("left-drawer--open");
            HUDController.SetElementVisible(_backdrop, true);
            Opened?.Invoke();
        }

        public void Close()
        {
            if (_drawer == null) return;
            IsOpen = false;
            _drawer.RemoveFromClassList("left-drawer--open");
            // Hide once the 0.2 s slide-out transition has finished (unless reopened meanwhile).
            _drawer.schedule.Execute(() =>
            {
                if (!IsOpen) _drawer.style.visibility = Visibility.Hidden;
            }).StartingIn(DrawerSlideMs);
            HUDController.SetElementVisible(_backdrop, false);
            Closed?.Invoke();
        }

        /// <summary>
        /// Moves the "Show hint" pill and the tutorial dialogue onto the map beside an open slide panel (above it in
        /// portrait, left of it in landscape), so neither covers the panel's buttons. See <see cref="PanelDock"/>.
        /// Null = no panel open (or the drawer is over it).
        /// </summary>
        public void RefreshPanelDocking(VisualElement openPanel)
        {
            if (_hudFrame == null) return;
            var frame = new Vector2(_hudFrame.layout.width, _hudFrame.layout.height);
            if (float.IsNaN(frame.x) || float.IsNaN(frame.y)) return;
            var rect  = openPanel != null ? openPanel.layout : Rect.zero;
            bool sized = openPanel != null && !float.IsNaN(rect.width) && rect.height > 0f;

            var pill = sized ? PanelDock.For(frame, rect, TopBarHeight, PillDockMinHeight, PillDockMinWidth) : PanelDock.None;
            var dlg  = sized ? PanelDock.For(frame, rect, TopBarHeight, DialogueDockMinHeight, DialogueDockMinWidth) : PanelDock.None;

            if (pill.Where != _pillDock.Where || pill.Free != _pillDock.Free)
            {
                _pillDock = pill;
                pill.PlaceBottomRight(_hintRestore, frame);
            }
            if (dlg.Where != _dialogueDock.Where || dlg.Free != _dialogueDock.Free)
            {
                _dialogueDock = dlg;
                dlg.Fill(_dialogueBox, frame);
                _dialogueBox?.EnableInClassList("dialogue-box--docked", dlg.Where != PanelDock.Side.None);
            }
        }
    }
}
