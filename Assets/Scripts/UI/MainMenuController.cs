using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuController : MonoBehaviour
    {
        [Header("Art / Branding")]
        [Tooltip("Assign a Sprite to use as the menu background. Leave null for the CSS-only fallback.")]
        [SerializeField] private Sprite backgroundSprite;

        [Tooltip("Overrides the top title label. Leave blank to use the UXML default (QUANTUM).")]
        [SerializeField] private string titleOverride;

        [Tooltip("Overrides the subtitle label. Leave blank to use the UXML default (FOUNDRY).")]
        [SerializeField] private string subtitleOverride;

        [Tooltip("Overrides the tagline label. Leave blank to use the UXML default.")]
        [SerializeField] private string taglineOverride;


        private Button _btnPlay;
        private Button _btnLeaderboard;
        private Button _btnAchievements;
        private Button _btnSettings;
        private Button _btnCredits;

        private Label _titleLabel;
        private Label _subtitleLabel;
        private Label _taglineLabel;
        private Label _versionLabel;
        private Label _achievementsLockedHint;
        private VisualElement _menuBg;

        private MainMenuAchievementsController _achievementsController;

        private void OnEnable()
        {
            _achievementsController = GetComponent<MainMenuAchievementsController>();
            var root = GetComponent<UIDocument>().rootVisualElement;
            QueryElements(root);
            ApplyBranding();
            BindButtons();
            ApplyAchievementsGate();
            _achievementsController?.Initialize(root);
        }

        // Re-apply gate in Start — guaranteed to run after all Awake() calls (including
        // SaveManager.Awake() which loads the save). OnEnable() may fire before SaveManager
        // is ready if script execution order puts it earlier in the scene.
        private void Start() => ApplyAchievementsGate();

        private void OnDisable()
        {
            if (_btnPlay         != null) _btnPlay.clicked         -= OnPlayPressed;
            if (_btnLeaderboard  != null) _btnLeaderboard.clicked  -= OnLeaderboardPressed;
            if (_btnAchievements != null) _btnAchievements.clicked -= OnAchievementsPressed;
            if (_btnSettings     != null) _btnSettings.clicked     -= OnSettingsPressed;
            if (_btnCredits      != null) _btnCredits.clicked      -= OnCreditsPressed;
            _achievementsController?.Cleanup();
        }

        private void QueryElements(VisualElement root)
        {
            _menuBg                  = root.Q("menu-bg");
            _titleLabel              = root.Q<Label>("menu-title");
            _subtitleLabel           = root.Q<Label>("menu-subtitle");
            _taglineLabel            = root.Q<Label>("menu-tagline");
            _versionLabel            = root.Q<Label>("menu-version");
            _achievementsLockedHint  = root.Q<Label>("achievements-locked-hint");
            _btnPlay         = root.Q<Button>("btn-play");
            _btnLeaderboard  = root.Q<Button>("btn-leaderboard");
            _btnAchievements = root.Q<Button>("btn-achievements");
            _btnSettings     = root.Q<Button>("btn-settings");
            _btnCredits      = root.Q<Button>("btn-credits");
        }

        private void ApplyAchievementsGate()
        {
            bool unlocked = SaveManager.Instance?.Current?.tutorial.hasCompletedFirstRun ?? false;
            if (_btnAchievements != null)
                _btnAchievements.SetEnabled(unlocked);
            HUDController.SetElementVisible(_achievementsLockedHint, !unlocked);
        }

        private void ApplyBranding()
        {
            if (backgroundSprite != null)
                _menuBg.style.backgroundImage = new StyleBackground(backgroundSprite);

            if (!string.IsNullOrEmpty(titleOverride))
                _titleLabel.text = titleOverride;

            if (!string.IsNullOrEmpty(subtitleOverride))
                _subtitleLabel.text = subtitleOverride;

            if (!string.IsNullOrEmpty(taglineOverride))
                _taglineLabel.text = taglineOverride;

            _versionLabel.text = $"v{Application.version}";
        }

        private void BindButtons()
        {
            _btnPlay.clicked        += OnPlayPressed;
            _btnLeaderboard.clicked += OnLeaderboardPressed;
            if (_btnAchievements != null) _btnAchievements.clicked += OnAchievementsPressed;
            _btnSettings.clicked    += OnSettingsPressed;
            _btnCredits.clicked     += OnCreditsPressed;
        }

        private void OnPlayPressed()         => SceneLoader.GoTo("GameScene");
        private void OnLeaderboardPressed()  => GameLogger.Debug("[MainMenu] Leaderboard — coming soon");
        private void OnAchievementsPressed() => _achievementsController?.Open();
        private void OnSettingsPressed()     => GameLogger.Debug("[MainMenu] Settings — coming soon");
        private void OnCreditsPressed()      => GameLogger.Debug("[MainMenu] Credits — coming soon");
    }
}
