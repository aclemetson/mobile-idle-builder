// App Tracking Transparency bridge for AdConsent (Assets/Scripts/Services/Ads/AdConsent.cs).
// The framework is weak-linked by Assets/Scripts/Editor/IOSTrackingPostProcess.cs, which also writes
// NSUserTrackingUsageDescription into Info.plist (iOS terminates the app on the request without it).

#import <Foundation/Foundation.h>
#import <AppTrackingTransparency/AppTrackingTransparency.h>

extern "C" {

// 0 not determined, 1 restricted, 2 denied, 3 authorized (ATTrackingManagerAuthorizationStatus).
// Before iOS 14 there is no prompt and tracking is allowed, so report authorized.
int MIB_TrackingAuthorizationStatus()
{
    if (@available(iOS 14, *))
        return (int)[ATTrackingManager trackingAuthorizationStatus];
    return 3;
}

// Shows the system prompt if the status is still undetermined. The result is read back by polling
// MIB_TrackingAuthorizationStatus, which keeps the bridge free of callbacks into managed code.
void MIB_RequestTrackingAuthorization()
{
    if (@available(iOS 14, *))
    {
        dispatch_async(dispatch_get_main_queue(), ^{
            [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {}];
        });
    }
}

}
