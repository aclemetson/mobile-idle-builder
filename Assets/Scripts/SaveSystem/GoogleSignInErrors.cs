namespace MobileIdleBuilder
{
    /// <summary>
    /// Turns a Google Sign-In status code into the text Settings shows. The plugin's SignInException has
    /// no message, so the player used to see only "Exception of type 'Google.GoogleSignIn+SignInException'
    /// was thrown."
    ///
    /// The plugin casts the native int straight to <c>Google.GoogleSignInStatusCode</c>. On Android that
    /// int is a Play services code (CommonStatusCodes / GoogleSignInStatusCodes: 10 = DEVELOPER_ERROR,
    /// 12501 = canceled), which the enum's names don't match, so Android uses its own table. The raw
    /// number is always shown so a report can be looked up either way.
    /// </summary>
    public static class GoogleSignInErrors
    {
        const string SigningKeyHint =
            "this build's signing key isn't registered with Google (check the SHA-1 on the Android OAuth client)";

        public static string Describe(int code, string enumName, bool android)
        {
            string label, hint;
            if (android)
            {
                (label, hint) = code switch
                {
                    4     => ("SIGN_IN_REQUIRED", "sign in again"),
                    5     => ("INVALID_ACCOUNT", "that Google account can't be used"),
                    7     => ("NETWORK_ERROR", "no network connection"),
                    8     => ("INTERNAL_ERROR", "Google had an internal error, try again"),
                    10    => ("DEVELOPER_ERROR", SigningKeyHint),
                    13    => ("ERROR", "Google reported an error"),
                    14    => ("INTERRUPTED", "sign-in was interrupted"),
                    15    => ("TIMEOUT", "Google took too long to answer"),
                    16    => ("CANCELED", "sign-in was canceled"),
                    12500 => ("SIGN_IN_FAILED", "Google rejected the sign-in (often the OAuth consent screen or SHA-1 setup)"),
                    12501 => ("SIGN_IN_CANCELLED", "sign-in was canceled"),
                    12502 => ("SIGN_IN_CURRENTLY_IN_PROGRESS", "a sign-in is already in progress"),
                    _     => ("Status", null),
                };
            }
            else
            {
                (label, hint) = code switch
                {
                    1 => ("ApiNotConnected", "Google sign-in is not connected"),
                    2 => ("Canceled", "sign-in was canceled"),
                    3 => ("Interrupted", "sign-in was interrupted"),
                    4 => ("InvalidAccount", "that Google account can't be used"),
                    5 => ("Timeout", "Google took too long to answer"),
                    6 => ("DeveloperError", "the app's Google sign-in setup is wrong"),
                    7 => ("InternalError", "Google had an internal error, try again"),
                    8 => ("NetworkError", "no network connection"),
                    9 => ("Error", "Google reported an error"),
                    _ => (string.IsNullOrEmpty(enumName) ? "Status" : enumName, null),
                };
            }
            return hint == null ? $"{label} ({code})" : $"{label} ({code}): {hint}";
        }
    }
}
