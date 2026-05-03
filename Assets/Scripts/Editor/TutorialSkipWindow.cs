using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace MobileIdleBuilder.Editor
{
    public class TutorialSkipWindow : EditorWindow
    {
        private const string KeyEnabled    = "TutorialSkip.Enabled";
        private const string KeyStepIndex  = "TutorialSkip.StepIndex";
        private const string KeyEntropy    = "TutorialSkip.Entropy";
        private const string FlowAssetPath = "Assets/Data/tutorial/tutorial_flow.asset";

        private bool     _enabled;
        private int      _stepIndex;
        private string   _entropyStr;
        private string[] _stepLabels = System.Array.Empty<string>();

        [MenuItem("MobileIdleBuilder/Tutorial Skip (Debug)")]
        private static void Open() => GetWindow<TutorialSkipWindow>("Tutorial Skip");

        private void OnEnable()
        {
            _enabled    = EditorPrefs.GetBool(KeyEnabled, false);
            _stepIndex  = EditorPrefs.GetInt(KeyStepIndex, 0);
            _entropyStr = EditorPrefs.GetString(KeyEntropy, "0");
            RefreshSteps();
        }

        private void RefreshSteps()
        {
            var flow = AssetDatabase.LoadAssetAtPath<TutorialFlowSO>(FlowAssetPath);
            if (flow == null || flow.steps == null || flow.steps.Length == 0)
            {
                _stepLabels = new[] { "(No tutorial flow — run importer)" };
                _stepIndex  = 0;
                return;
            }

            _stepLabels = new string[flow.steps.Length];
            for (int i = 0; i < flow.steps.Length; i++)
                _stepLabels[i] = $"[{i}]  {flow.steps[i].id}";

            _stepIndex = Mathf.Clamp(_stepIndex, 0, flow.steps.Length - 1);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Tutorial Skip — Debug Only", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();

            _enabled = EditorGUILayout.Toggle("Enable on Play", _enabled);

            EditorGUI.BeginDisabledGroup(!_enabled);
            _stepIndex  = EditorGUILayout.Popup("Starting Step", _stepIndex, _stepLabels);
            _entropyStr = EditorGUILayout.TextField("Starting Entropy", _entropyStr);
            EditorGUI.EndDisabledGroup();

            if (EditorGUI.EndChangeCheck())
                SavePrefs();

            EditorGUILayout.Space(6);

            if (GUILayout.Button("Refresh Steps"))
                RefreshSteps();

            EditorGUILayout.Space(4);

            var label = _stepLabels.Length > _stepIndex ? _stepLabels[_stepIndex] : "?";
            if (_enabled)
                EditorGUILayout.HelpBox(
                    $"ACTIVE — Play mode will start at {label} with entropy = {_entropyStr}.",
                    MessageType.Warning);
            else
                EditorGUILayout.HelpBox("Disabled — normal Play mode behaviour applies.", MessageType.Info);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(!Application.isPlaying);
            if (GUILayout.Button("Print Skip State to Console"))
                PrintDiagnostics();
            EditorGUI.EndDisabledGroup();

            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Enter Play mode to run diagnostics.", MessageType.None);
        }

        private static void PrintDiagnostics()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Tutorial Skip Diagnostics ===");

            // EditorPrefs
            sb.AppendLine($"[Prefs] Enabled:    {EditorPrefs.GetBool("TutorialSkip.Enabled", false)}");
            sb.AppendLine($"[Prefs] StepIndex:  {EditorPrefs.GetInt("TutorialSkip.StepIndex", 0)}");
            sb.AppendLine($"[Prefs] Entropy:    {EditorPrefs.GetString("TutorialSkip.Entropy", "0")}");

            // TutorialFlowSO
            var flow = TutorialFlowSO.Current;
            sb.AppendLine($"[Flow]  TutorialFlowSO.Current: {(flow == null ? "NULL" : $"{flow.steps?.Length ?? 0} steps")}");

            // ECS tutorial state
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                var tq = world.EntityManager.CreateEntityQuery(typeof(TutorialStateData));
                var pq = world.EntityManager.CreateEntityQuery(typeof(PlayerProgressData));

                if (!tq.IsEmpty)
                {
                    var ts = tq.GetSingleton<TutorialStateData>();
                    sb.AppendLine($"[ECS]   CurrentStepIndex: {ts.CurrentStepIndex}  IsActive: {ts.IsActive}");
                    if (flow != null && ts.CurrentStepIndex < flow.steps.Length)
                        sb.AppendLine($"[ECS]   Current step id:  {flow.steps[ts.CurrentStepIndex].id}");
                }
                else sb.AppendLine("[ECS]   TutorialStateData singleton: NOT FOUND");

                if (!pq.IsEmpty)
                    sb.AppendLine($"[ECS]   BaseCurrency: {pq.GetSingleton<PlayerProgressData>().BaseCurrency}");
                else
                    sb.AppendLine("[ECS]   PlayerProgressData singleton: NOT FOUND");
            }
            else sb.AppendLine("[ECS]   DefaultGameObjectInjectionWorld is null");

            // ResearchService
            var rs = ResearchService.Instance;
            if (rs == null)
            {
                sb.AppendLine("[Research] ResearchService.Instance: NULL");
            }
            else
            {
                var ids = new[] { "recombination_i", "hydrogen_synthesis" };
                foreach (var id in ids)
                    sb.AppendLine($"[Research] IsUnlocked({id}): {rs.IsUnlocked(id)}");
            }

            // SaveManager research list
            var save = SaveManager.Instance?.Current;
            if (save != null)
            {
                sb.AppendLine($"[Save]  unlockedResearch ({save.unlockedResearch?.Count ?? 0}): " +
                              (save.unlockedResearch?.Count > 0
                                  ? string.Join(", ", save.unlockedResearch)
                                  : "(empty)"));
            }
            else sb.AppendLine("[Save]  SaveManager.Instance or Current is null");

            // RecipeKnowledgeService
            var ks = RecipeKnowledgeService.Instance;
            if (ks == null)
            {
                sb.AppendLine("[Recipes] RecipeKnowledgeService.Instance: NULL");
            }
            else
            {
                var recipeIds = new[] { "proton", "neutron", "hydrogen" };
                foreach (var id in recipeIds)
                    sb.AppendLine($"[Recipes] IsKnown({id}): {ks.IsKnown(id)}");
            }

            sb.AppendLine("=================================");
            Debug.Log(sb.ToString());
        }

        private void SavePrefs()
        {
            EditorPrefs.SetBool(KeyEnabled, _enabled);
            EditorPrefs.SetInt(KeyStepIndex, _stepIndex);
            EditorPrefs.SetString(KeyEntropy, _entropyStr);
        }
    }
}
