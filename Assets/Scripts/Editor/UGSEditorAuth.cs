using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEditor;
using UnityEngine;

namespace MobileIdleBuilder.Editor
{
    /// <summary>
    /// Wires UGSCloudSaveService.EditorAuthProvider before scene load so the save system
    /// signs in with the credentials stored in EditorPrefs instead of anonymously.
    /// </summary>
    [InitializeOnLoad]
    static class UGSEditorAuth
    {
        internal const string k_UsernameKey = "MIB_UGS_EditorUsername";
        internal const string k_PasswordKey = "MIB_UGS_EditorPassword";

        static UGSEditorAuth() { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterAuthProvider()
        {
            string username = EditorPrefs.GetString(k_UsernameKey, "");
            string password = EditorPrefs.GetString(k_PasswordKey, "");

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                UGSCloudSaveService.EditorAuthProvider = null;
                return;
            }

            UGSCloudSaveService.EditorAuthProvider = async () =>
            {
                try
                {
                    await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
                    Debug.Log($"[UGS Editor] Signed in as '{username}' — PlayerId: {AuthenticationService.Instance.PlayerId}");
                }
                catch (Exception signInEx)
                {
                    // Account may not exist in this environment yet — attempt creation.
                    Debug.Log($"[UGS Editor] Sign-in failed ({signInEx.Message}), attempting account creation...");
                    try
                    {
                        await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);
                        Debug.Log($"[UGS Editor] Account created for '{username}' — PlayerId: {AuthenticationService.Instance.PlayerId}");
                    }
                    catch (Exception signUpEx)
                    {
                        // Username taken with a different password — credentials need updating.
                        Debug.LogWarning(
                            $"[UGS Editor] Sign-in and sign-up both failed for '{username}'.\n" +
                            $"  Sign-in:  {signInEx.Message}\n" +
                            $"  Sign-up:  {signUpEx.Message}\n" +
                            $"  If the account exists with a different password, update it at Tools > UGS > Editor Sign-In.\n" +
                            $"  Falling back to anonymous.");
                        await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    }
                }
            };
        }
    }
}
