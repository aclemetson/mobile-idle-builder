#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace MobileIdleBuilder.Editor
{
    /// <summary>
    /// Injects the GIDClientID + reversed-client-ID URL scheme into Info.plist so the
    /// GoogleSignIn SDK can start the OAuth flow and receive its redirect.
    ///
    /// The GoogleSignIn CocoaPod itself is NOT declared here -- it comes from
    /// GoogleSignInDependencies.xml, which the Mobile Dependency Resolver (EDM4U) turns
    /// into the Podfile at PostProcessBuild order 40, alongside every other package's
    /// pods. This class used to overwrite that Podfile with a GoogleSignIn-only one,
    /// which silently deleted LevelPlay's IronSource pods; IOSPodfileVerifier now guards
    /// against that. scripts/archive-upload-ios.sh runs `pod install` on the macOS runner
    /// and archives the resulting .xcworkspace.
    ///
    /// Android is unaffected (it uses the native-googlesignin .aar, not this pod).
    /// </summary>
    public static class IOSGoogleSignInPostProcess
    {
        // iOS OAuth client ID (Google Cloud Console credential type 'iOS', bound to
        // this app's bundle id, same project as the web client below). The reversed
        // form of this value is registered as a URL scheme further down. Note: the
        // ID token minted on iOS carries THIS client id as its audience, so the UGS
        // Google identity provider must list it as an allowed client id.
        const string k_IOSClientId = "835677862122-ca90eq353jva89iaslm83ja7mlnu20c5.apps.googleusercontent.com";

        // The web/server OAuth client ID -- same value as
        // GoogleSignInBridge.k_WebClientId on Android. Supplied to the SDK so it can
        // return a server auth code for the backend; UGS must trust this audience.
        const string k_ServerClientId =
            "835677862122-lo96buj4knugl0fh4tpeub9arvtcdh1n.apps.googleusercontent.com";

        const string k_ClientIdSuffix = ".apps.googleusercontent.com";

        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
                return;

            PatchInfoPlist(pathToBuiltProject);
        }

        static void PatchInfoPlist(string projectPath)
        {
            string plistPath = Path.Combine(projectPath, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            PlistElementDict root = plist.root;

            root.SetString("GIDClientID", k_IOSClientId);
            root.SetString("GIDServerClientID", k_ServerClientId);

            // Register the reversed-client-ID URL scheme so the OAuth redirect can
            // return to the app. Append to any existing CFBundleURLTypes.
            PlistElementArray urlTypes = root.values.ContainsKey("CFBundleURLTypes")
                ? root["CFBundleURLTypes"].AsArray()
                : root.CreateArray("CFBundleURLTypes");

            PlistElementDict urlType = urlTypes.AddDict();
            urlType.SetString("CFBundleTypeRole", "Editor");
            PlistElementArray schemes = urlType.CreateArray("CFBundleURLSchemes");
            schemes.AddString(ReverseClientId(k_IOSClientId));

            plist.WriteToFile(plistPath);
            Debug.Log("[IOSGoogleSignInPostProcess] Patched Info.plist (GIDClientID + URL scheme).");
        }

        // Converts "<id>.apps.googleusercontent.com" to the reversed URL-scheme form
        // "com.googleusercontent.apps.<id>" that GoogleSignIn expects.
        static string ReverseClientId(string clientId)
        {
            string id = clientId.EndsWith(k_ClientIdSuffix)
                ? clientId.Substring(0, clientId.Length - k_ClientIdSuffix.Length)
                : clientId;
            return "com.googleusercontent.apps." + id;
        }
    }
}
#endif
