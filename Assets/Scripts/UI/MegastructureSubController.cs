using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Populates and manages the Megastructure panel (Dyson Sphere endgame project).
    /// Sits as a component alongside HUDController on the same GameObject.
    ///
    /// Shows the current stage's bill of materials with per-item progress + contribute buttons, the
    /// already-completed stages and their active rewards, and a "complete" state once all stages are done.
    /// All state and mutation goes through <see cref="MegastructureService"/>.
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class MegastructureSubController : MonoBehaviour
    {
        private VisualElement _panel;
        private Label         _progressLabel;
        private ScrollView    _list;
        private HUDController _hud;
        private bool          _subscribed;

        public void Init(VisualElement root, HUDController hud)
        {
            _hud           = hud;
            _panel         = root.Q("megastructure-panel");
            _progressLabel = root.Q<Label>("megastructure-progress");
            _list          = root.Q<ScrollView>("megastructure-list");
        }

        void OnDisable()
        {
            if (_subscribed && MegastructureService.Instance != null)
                MegastructureService.Instance.OnChanged -= Refresh;
            _subscribed = false;
        }

        /// <summary>Rebuilds the panel contents with current state.</summary>
        public void Refresh()
        {
            if (_list == null) return;
            _list.Clear();

            var svc = MegastructureService.Instance;
            if (svc == null || svc.Data == null)
            {
                if (_progressLabel != null) _progressLabel.text = "Unavailable";
                _list.Add(new Label("Megastructure data not available."));
                return;
            }

            // Subscribe lazily so the panel live-refreshes while open (e.g. items finish crafting onto belts).
            if (!_subscribed) { svc.OnChanged += Refresh; _subscribed = true; }

            int total = svc.Data.stages?.Length ?? 0;
            if (_progressLabel != null)
                _progressLabel.text = $"Stage {Mathf.Min(svc.CompletedStages, total)} / {total}";

            // Completed stages first (read-only summary of granted rewards).
            for (int i = 0; i < svc.CompletedStages && i < total; i++)
            {
                var done = svc.Data.stages[i];
                var row = new VisualElement();
                row.AddToClassList("upgrade-row");
                row.AddToClassList("upgrade-row--maxed");

                var header = new Label($"✓ {done.displayName}");
                header.AddToClassList("upgrade-row-name");
                row.Add(header);

                var reward = new Label(RewardText(done));
                reward.AddToClassList("upgrade-row-desc");
                row.Add(reward);

                _list.Add(row);
            }

            var stage = svc.CurrentStage;
            if (stage == null)
            {
                var complete = new Label("The Dyson Sphere is complete. All stage rewards are active.");
                complete.AddToClassList("recipe-name");
                _list.Add(complete);
                return;
            }

            BuildCurrentStage(svc, stage);
        }

        private void BuildCurrentStage(MegastructureService svc, MegastructureStage stage)
        {
            var container = new VisualElement();
            container.AddToClassList("upgrade-row");

            var title = new Label(stage.displayName);
            title.AddToClassList("upgrade-row-name");
            container.Add(title);

            var reward = new Label($"Reward: {RewardText(stage)}");
            reward.AddToClassList("upgrade-row-desc");
            container.Add(reward);

            int items = stage.costItems?.Length ?? 0;
            for (int i = 0; i < items; i++)
            {
                var item = stage.costItems[i];
                if (item == null) continue;
                int need        = (i < stage.costQuantities.Length) ? stage.costQuantities[i] : 0;
                int contributed = svc.GetContributed(item.itemId);
                int have        = svc.InventoryCount(item.itemId);
                int remaining   = Mathf.Max(0, need - contributed);

                var itemRow = new VisualElement();
                itemRow.AddToClassList("megastructure-item-row");

                var nameLabel = new Label($"{item.displayName}  {contributed} / {need}  (have {have})");
                nameLabel.AddToClassList("upgrade-row-desc");
                itemRow.Add(nameLabel);

                // Progress bar (C#-built, same classes as the achievement progress bars).
                var bar = new VisualElement();
                bar.AddToClassList("ach-progress-bar");
                var fill = new VisualElement();
                fill.AddToClassList("ach-progress-fill");
                float ratio = need > 0 ? Mathf.Clamp01((float)contributed / need) : 1f;
                fill.style.width = new StyleLength(new Length(ratio * 100f, LengthUnit.Percent));
                bar.Add(fill);
                itemRow.Add(bar);

                if (remaining > 0)
                {
                    var btnRow = new VisualElement();
                    btnRow.AddToClassList("upgrade-row-footer");

                    int capturedId = item.itemId;

                    var oneBtn = new Button { text = "Contribute 1" };
                    oneBtn.AddToClassList("craft-btn");
                    oneBtn.SetEnabled(have >= 1);
                    oneBtn.clicked += () => Contribute(capturedId, 1);
                    btnRow.Add(oneBtn);

                    var allBtn = new Button { text = "Contribute all" };
                    allBtn.AddToClassList("craft-btn");
                    allBtn.SetEnabled(have >= 1);
                    allBtn.clicked += () => ContributeAll(capturedId);
                    btnRow.Add(allBtn);

                    itemRow.Add(btnRow);
                }

                container.Add(itemRow);
            }

            _list.Add(container);
        }

        private void Contribute(int itemId, int qty)
        {
            MegastructureService.Instance?.Contribute(itemId, qty);
            Refresh(); // OnChanged also fires, but refresh immediately for responsiveness
        }

        private void ContributeAll(int itemId)
        {
            MegastructureService.Instance?.ContributeAll(itemId);
            Refresh();
        }

        private static string RewardText(MegastructureStage stage)
        {
            int pct = Mathf.RoundToInt(stage.rewardValue * 100f);
            return stage.rewardType switch
            {
                MegastructureRewardType.OutputMultiplier       => $"+{pct}% global output",
                MegastructureRewardType.SpeedMultiplier        => $"+{pct}% global craft speed",
                MegastructureRewardType.PrestigeGainMultiplier => $"+{pct}% prestige currency gain",
                _ => ""
            };
        }
    }
}
