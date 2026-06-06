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
        private Button _btnSettings;
        private Button _btnCredits;

        private Label _titleLabel;
        private Label _subtitleLabel;
        private Label _taglineLabel;
        private Label _versionLabel;
        private VisualElement _menuBg;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            QueryElements(root);
            ApplyBranding();
            BindButtons();
        }

        private void OnDisable()
        {
            if (_btnPlay        != null) _btnPlay.clicked        -= OnPlayPressed;
            if (_btnLeaderboard != null) _btnLeaderboard.clicked -= OnLeaderboardPressed;
            if (_btnSettings    != null) _btnSettings.clicked    -= OnSettingsPressed;
            if (_btnCredits     != null) _btnCredits.clicked     -= OnCreditsPressed;
        }

        private void QueryElements(VisualElement root)
        {
            _menuBg        = root.Q("menu-bg");
            _titleLabel    = root.Q<Label>("menu-title");
            _subtitleLabel = root.Q<Label>("menu-subtitle");
            _taglineLabel  = root.Q<Label>("menu-tagline");
            _versionLabel  = root.Q<Label>("menu-version");
            _btnPlay        = root.Q<Button>("btn-play");
            _btnLeaderboard = root.Q<Button>("btn-leaderboard");
            _btnSettings    = root.Q<Button>("btn-settings");
            _btnCredits     = root.Q<Button>("btn-credits");
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
            _btnSettings.clicked    += OnSettingsPressed;
            _btnCredits.clicked     += OnCreditsPressed;
        }

        private void OnPlayPressed()        => SceneLoader.GoTo("GameScene");
        private void OnLeaderboardPressed() => GameLogger.Debug("[MainMenu] Leaderboard — coming soon");
        private void OnSettingsPressed()    => GameLogger.Debug("[MainMenu] Settings — coming soon");
        private void OnCreditsPressed()     => GameLogger.Debug("[MainMenu] Credits — coming soon");
    }
}
