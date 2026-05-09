using Unity.Services.Authentication;
using UnityEditor;
using UnityEngine;

namespace MobileIdleBuilder.Editor
{
    /// <summary>
    /// Tools > UGS > Editor Sign-In
    /// Stores UGS username/password in EditorPrefs so each Play session signs in
    /// as the same player (instead of a new anonymous identity).
    /// </summary>
    public class UGSEditorAuthWindow : EditorWindow
    {
        string _username = "";
        string _password = "";

        [MenuItem("Tools/UGS/Editor Sign-In")]
        static void Open() => GetWindow<UGSEditorAuthWindow>("UGS Editor Sign-In").Show();

        void OnEnable()
        {
            _username = EditorPrefs.GetString(UGSEditorAuth.k_UsernameKey, "");
            _password = EditorPrefs.GetString(UGSEditorAuth.k_PasswordKey, "");
        }

        void OnGUI()
        {
            GUILayout.Label("UGS Cloud Save — Editor Identity", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawStatus();
            EditorGUILayout.Space(8);

            DrawCredentialFields();
            EditorGUILayout.Space(8);

            DrawDashboardHelp();
        }

        void DrawStatus()
        {
            bool hasCredentials = !string.IsNullOrEmpty(EditorPrefs.GetString(UGSEditorAuth.k_UsernameKey, ""));

            if (!hasCredentials)
            {
                EditorGUILayout.HelpBox(
                    "No credentials saved. Enter a username/password below and click Save.\n" +
                    "First Play will create the account; subsequent sessions sign in automatically.",
                    MessageType.Info);
                return;
            }

            if (Application.isPlaying)
            {
                try
                {
                    bool signedIn = AuthenticationService.Instance.IsSignedIn;
                    if (signedIn)
                    {
                        string playerId = AuthenticationService.Instance.PlayerId;
                        EditorGUILayout.HelpBox($"Signed in as '{EditorPrefs.GetString(UGSEditorAuth.k_UsernameKey)}'\nPlayerId: {playerId}", MessageType.None);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Signing in... (check console for result)", MessageType.Warning);
                    }
                }
                catch
                {
                    EditorGUILayout.HelpBox("UGS not yet initialized — sign-in happens during scene load.", MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Credentials saved for '{EditorPrefs.GetString(UGSEditorAuth.k_UsernameKey)}'.\n" +
                    "Enter Play Mode to sign in — PlayerId will appear here.",
                    MessageType.None);
            }
        }

        void DrawCredentialFields()
        {
            GUILayout.Label("Credentials (stored in EditorPrefs — dev only)", EditorStyles.miniBoldLabel);

            _username = EditorGUILayout.TextField("Username", _username);
            _password = EditorGUILayout.PasswordField("Password", _password);

            EditorGUILayout.HelpBox(
                "UGS password requirements: 8–30 chars, 1 uppercase, 1 lowercase, 1 digit, 1 symbol.\n" +
                "Example: Dev@12345!",
                MessageType.Info);

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Save Credentials"))
            {
                EditorPrefs.SetString(UGSEditorAuth.k_UsernameKey, _username);
                EditorPrefs.SetString(UGSEditorAuth.k_PasswordKey, _password);
                Debug.Log($"[UGS Editor] Credentials saved for '{_username}'. Enter Play Mode to authenticate.");
            }

            GUI.color = new Color(1f, 0.7f, 0.7f);
            if (GUILayout.Button("Clear Credentials", GUILayout.Width(140)))
            {
                EditorPrefs.DeleteKey(UGSEditorAuth.k_UsernameKey);
                EditorPrefs.DeleteKey(UGSEditorAuth.k_PasswordKey);
                _username = "";
                _password = "";
                Debug.Log("[UGS Editor] Credentials cleared — will use anonymous sign-in.");
            }
            GUI.color = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        void DrawDashboardHelp()
        {
            GUILayout.Label("Viewing data in the dashboard", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "1. Enter Play Mode — the PlayerId logs to the console and appears above.\n" +
                "2. Go to cloud.unity.com → your project → Cloud Save.\n" +
                "3. Under 'Player Data' search by your PlayerId to inspect stored keys.",
                MessageType.None);

            if (GUILayout.Button("Open cloud.unity.com"))
                Application.OpenURL("https://cloud.unity.com");
        }

        void OnInspectorUpdate() => Repaint();
    }
}
