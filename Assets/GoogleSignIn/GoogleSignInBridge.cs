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
            UseGameSignIn  = false
        };

        UGSCloudSaveService.GoogleAuthProvider = GetIdTokenAsync;
        Debug.Log("[GoogleSignInBridge] Registered.");
    }

    static async Task<string> GetIdTokenAsync()
    {
        GoogleSignInUser user;
        bool usedInteractiveSignIn;

        if (AuthSessionPolicy.RequiresInteractiveSignIn())
        {
            // Policy requires full account picker (inactivity ≥30d or full-auth ≥90d)
            user = await Wrap(GoogleSignIn.DefaultInstance.SignIn());
            usedInteractiveSignIn = true;
        }
        else
        {
            usedInteractiveSignIn = false;
            try
            {
                user = await Wrap(GoogleSignIn.DefaultInstance.SignInSilently());
            }
            catch
            {
                // Cached session dropped — fall back to account picker
                user = await Wrap(GoogleSignIn.DefaultInstance.SignIn());
                usedInteractiveSignIn = true;
            }
        }

        if (usedInteractiveSignIn)
            AuthSessionPolicy.RecordFullAuth();

        if (string.IsNullOrEmpty(user.IdToken))
            throw new Exception("[GoogleSignIn] IdToken is null — verify RequestIdToken = true in configuration.");

        return user.IdToken;
    }

    // GoogleSignIn Tasks use ContinueWith internally; wrap to standard awaitable Task
    static Task<GoogleSignInUser> Wrap(Task<GoogleSignInUser> task)
    {
        var tcs = new TaskCompletionSource<GoogleSignInUser>();
        task.ContinueWith(t =>
        {
            if (t.IsFaulted)        tcs.SetException(t.Exception.InnerException ?? t.Exception);
            else if (t.IsCanceled)  tcs.SetCanceled();
            else                    tcs.SetResult(t.Result);
        });
        return tcs.Task;
    }
}
#endif
