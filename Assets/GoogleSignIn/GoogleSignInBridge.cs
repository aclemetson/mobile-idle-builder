#if UNITY_ANDROID && !UNITY_EDITOR || UNITY_IOS && !UNITY_EDITOR
using System;
using System.Threading.Tasks;
using Google;
using MobileIdleBuilder;
using UnityEngine;

public static class GoogleSignInBridge
{
    // Replace with your Web application client ID from Google Cloud Console
    const string k_WebClientId = "835677862122-lo96buj4knugl0fh4tpeub9arvtcdh1n.apps.googleusercontent.com";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        GoogleSignIn.Configuration = new GoogleSignInConfiguration
        {
            WebClientId    = k_WebClientId,
            RequestIdToken = true,
            RequestEmail   = true,   // shown in Settings so both devices can be checked against each other
            UseGameSignIn  = false
        };

        UGSCloudSaveService.GoogleAuthProvider = GetCredentialAsync;
        Debug.Log("[GoogleSignInBridge] Registered.");
    }

    static async Task<GoogleCredential> GetCredentialAsync(GoogleAuthMode mode)
    {
        bool interactive = mode == GoogleAuthMode.Interactive ||
                           (mode == GoogleAuthMode.Policy && AuthSessionPolicy.RequiresInteractiveSignIn());

        GoogleSignInUser user;
        if (interactive)
        {
            // Account picker: first install, the 30/90-day policy, or Settings "Sign in with Google".
            user = await Wrap(GoogleSignIn.DefaultInstance.SignIn());
            AuthSessionPolicy.RecordFullAuth();
        }
        else
        {
            // Silent only. If the credential is gone, throw so the caller stays a guest without
            // showing any UI; Settings offers the interactive sign-in.
            user = await Wrap(GoogleSignIn.DefaultInstance.SignInSilently());
        }

        if (string.IsNullOrEmpty(user?.IdToken))
            throw new Exception("[GoogleSignIn] IdToken is null — verify RequestIdToken = true in configuration.");

        return new GoogleCredential { IdToken = user.IdToken, Email = user.Email };
    }

    // GoogleSignIn Tasks use ContinueWith internally; wrap to standard awaitable Task
    static Task<GoogleSignInUser> Wrap(Task<GoogleSignInUser> task)
    {
        var tcs = new TaskCompletionSource<GoogleSignInUser>();
        task.ContinueWith(t =>
        {
            if (t.IsFaulted)        tcs.SetException(Readable(t.Exception.InnerException ?? t.Exception));
            else if (t.IsCanceled)  tcs.SetCanceled();
            else                    tcs.SetResult(t.Result);
        });
        return tcs.Task;
    }

    // SignInException carries only a status code; give it a message Settings can show, and log it
    // so logcat has it too.
    static Exception Readable(Exception ex)
    {
        if (!(ex is GoogleSignIn.SignInException sie)) return ex;
        string text = GoogleSignInErrors.Describe((int)sie.Status, sie.Status.ToString(),
                                                  Application.platform == RuntimePlatform.Android);
        Debug.LogWarning($"[GoogleSignInBridge] Sign-in failed: {text}");
        return new Exception(text, ex);
    }
}
#endif
