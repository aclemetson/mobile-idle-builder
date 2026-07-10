/**
 * Copyright 2017 Google Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

// Modernized Google Sign-In bridge for Unity (iOS).
//
// Exposes the same extern "C" GoogleSignIn_* surface the C# layer P/Invokes
// (Assets/GoogleSignIn/Impl/GoogleSignInImpl.cs), but implemented against the
// completion-handler GoogleSignIn SDK (>= 7.x, pulled in via CocoaPods by
// Assets/GoogleSignIn/Editor/IOSGoogleSignInPostProcess.cs). The delegate-based
// 2017 API this file used to call (GIDSignInDelegate, [signIn signIn], etc.) was
// removed in SDK 6.x, which is what broke the iOS archive.

#import "GoogleSignIn.h"

#import <GoogleSignIn/GIDGoogleUser.h>
#import <GoogleSignIn/GIDProfileData.h>
#import <GoogleSignIn/GIDSignIn.h>
#import <GoogleSignIn/GIDSignInResult.h>
#import <GoogleSignIn/GIDToken.h>

#import <memory>

// Status codes shared with the Unity C# layer (GoogleSignInStatusCode). The iOS
// SDK error codes are mapped onto these so the managed layer stays platform
// agnostic, matching the Android side.
static const int kStatusCodeSuccess = 0;
static const int kStatusCodeCanceled = 2;
static const int kStatusCodeDeveloperError = 6;
static const int kStatusCodeError = 9;

// Pending sign-in state, guarded by resultLock. Mirrors the original design: the
// C# NativeFuture polls Pending()/Status() until finished, then reads Result().
struct SignInResult {
  int result_code;
  bool finished;
};

static std::unique_ptr<SignInResult> currentResult_;
static NSRecursiveLock *resultLock = [[NSRecursiveLock alloc] init];

// The user + server auth code from the most recent sign-in. The C# accessors read
// these back. Retained so they survive until the next sign-in.
static GIDGoogleUser *currentUser_ = nil;
static NSString *currentServerAuthCode_ = nil;

// Captured from GoogleSignIn_Configure(). clientID / serverClientID come from
// Info.plist (GIDClientID / GIDServerClientID, injected at build time); here we
// only need the optional login hint and any additional scopes.
static NSString *loginHint_ = nil;
static NSMutableArray<NSString *> *additionalScopes_ = nil;

// Maps an iOS sign-in error to the cross-platform status code. Only an explicit
// user cancel is distinguished; everything else is a generic error, which keeps
// the SDK symbol surface this file depends on minimal.
static int MapError(NSError *error) {
  if (error == nil) {
    return kStatusCodeSuccess;
  }
  if ([error.domain isEqualToString:kGIDSignInErrorDomain] &&
      error.code == kGIDSignInErrorCodeCanceled) {
    return kStatusCodeCanceled;
  }
  return kStatusCodeError;
}

// Records the outcome of a sign-in/restore so the polling C# side can pick it up.
static void FinishSignIn(GIDGoogleUser *user, NSString *serverAuthCode,
                         NSError *error) {
  [resultLock lock];
  currentUser_ = user;
  currentServerAuthCode_ = serverAuthCode;
  if (currentResult_) {
    currentResult_->result_code = MapError(error);
    currentResult_->finished = true;
  }
  [resultLock unlock];
  if (error != nil) {
    NSLog(@"[GoogleSignIn] sign-in failed: %@", error.localizedDescription);
  }
}

/**
 * These are the external "C" methods imported by the Unity C# code. The
 * parameters are primitive and easy to marshal. Signatures must stay in lockstep
 * with the [DllImport] declarations in GoogleSignInImpl.cs / NativeFuture.cs.
 */
extern "C" {

// Android passes the activity here; iOS has no equivalent, so this is a no-op that
// keeps the cross-platform C# signature uniform.
void *GoogleSignIn_Create(void *data) { return NULL; }

void GoogleSignIn_EnableDebugLogging(void *unused, bool flag) {
  // The iOS SDK has no toggle for verbose logging.
}

// Captures configuration. clientID/serverClientID are supplied via Info.plist, so
// here we only retain the optional hint + extra scopes for the sign-in call.
// Returns !useGameSignIn to match the original contract (Play Games sign-in is
// Android only).
bool GoogleSignIn_Configure(void *unused, bool useGameSignIn,
                            const char *webClientId, bool requestAuthCode,
                            bool forceTokenRefresh, bool requestEmail,
                            bool requestIdToken, bool hidePopups,
                            const char **additionalScopes, int scopeCount,
                            const char *accountName) {
  additionalScopes_ = [NSMutableArray arrayWithCapacity:scopeCount];
  for (int i = 0; i < scopeCount; i++) {
    if (additionalScopes[i]) {
      [additionalScopes_
          addObject:[NSString stringWithUTF8String:additionalScopes[i]]];
    }
  }
  loginHint_ = accountName ? [NSString stringWithUTF8String:accountName] : nil;
  return !useGameSignIn;
}

// Begins a new pending operation, or returns an error result if one is already in
// flight. Returns nullptr when the caller may proceed to start the SDK call.
static SignInResult *BeginOperation() {
  bool busy = false;
  [resultLock lock];
  if (!currentResult_ || currentResult_->finished) {
    currentResult_.reset(new SignInResult());
    currentResult_->result_code = kStatusCodeSuccess;
    currentResult_->finished = false;
  } else {
    busy = true;
  }
  [resultLock unlock];

  if (busy) {
    NSLog(@"[GoogleSignIn] ERROR: a sign-in operation is already pending.");
    // Returned to the caller; freed via GoogleSignIn_DisposeFuture().
    return new SignInResult{kStatusCodeDeveloperError, true};
  }
  return nullptr;
}

void *GoogleSignIn_SignIn(void *unused) {
  SignInResult *busy = BeginOperation();
  if (busy) {
    return busy;
  }
  dispatch_async(dispatch_get_main_queue(), ^{
    [[GIDSignIn sharedInstance]
        signInWithPresentingViewController:UnityGetGLViewController()
                                      hint:loginHint_
                          additionalScopes:additionalScopes_
                                completion:^(GIDSignInResult *result,
                                             NSError *error) {
                                  FinishSignIn(result.user,
                                               result.serverAuthCode, error);
                                }];
  });
  return currentResult_.get();
}

void *GoogleSignIn_SignInSilently(void *unused) {
  SignInResult *busy = BeginOperation();
  if (busy) {
    return busy;
  }
  dispatch_async(dispatch_get_main_queue(), ^{
    [[GIDSignIn sharedInstance]
        restorePreviousSignInWithCompletion:^(GIDGoogleUser *user,
                                              NSError *error) {
          FinishSignIn(user, nil, error);
        }];
  });
  return currentResult_.get();
}

void GoogleSignIn_Signout(void *unused) {
  [[GIDSignIn sharedInstance] signOut];
  currentUser_ = nil;
  currentServerAuthCode_ = nil;
}

void GoogleSignIn_Disconnect(void *unused) {
  [[GIDSignIn sharedInstance] disconnectWithCompletion:^(NSError *error) {
    if (error != nil) {
      NSLog(@"[GoogleSignIn] disconnect failed: %@", error.localizedDescription);
    }
  }];
}

bool GoogleSignIn_Pending(SignInResult *result) {
  if (!result) {
    return false;
  }
  bool pending;
  [resultLock lock];
  pending = !result->finished;
  [resultLock unlock];
  return pending;
}

GIDGoogleUser *GoogleSignIn_Result(SignInResult *result) {
  if (result && result->finished) {
    return currentUser_;
  }
  return nullptr;
}

int GoogleSignIn_Status(SignInResult *result) {
  return result ? result->result_code : kStatusCodeDeveloperError;
}

void GoogleSignIn_DisposeFuture(SignInResult *result) {
  if (result == currentResult_.get()) {
    currentResult_.reset(nullptr);
  } else {
    delete result;
  }
}

// Copies an NSString into the caller's buffer. With a null/zero buffer it returns
// the size needed (length + 1), matching the two-call pattern the C# side uses.
static size_t CopyNSString(NSString *src, char *dest, size_t len) {
  if (dest && src && len) {
    const char *string = [src UTF8String];
    strncpy(dest, string, len);
    return len;
  }
  return src ? src.length + 1 : 0;
}

size_t GoogleSignIn_GetServerAuthCode(GIDGoogleUser *unused, char *buf,
                                      size_t len) {
  // In the modern SDK the server auth code lives on GIDSignInResult, not the
  // user, so it is captured at sign-in time rather than read from the user.
  return CopyNSString(currentServerAuthCode_, buf, len);
}

size_t GoogleSignIn_GetDisplayName(GIDGoogleUser *user, char *buf, size_t len) {
  return CopyNSString(user.profile.name, buf, len);
}

size_t GoogleSignIn_GetEmail(GIDGoogleUser *user, char *buf, size_t len) {
  return CopyNSString(user.profile.email, buf, len);
}

size_t GoogleSignIn_GetFamilyName(GIDGoogleUser *user, char *buf, size_t len) {
  return CopyNSString(user.profile.familyName, buf, len);
}

size_t GoogleSignIn_GetGivenName(GIDGoogleUser *user, char *buf, size_t len) {
  return CopyNSString(user.profile.givenName, buf, len);
}

size_t GoogleSignIn_GetIdToken(GIDGoogleUser *user, char *buf, size_t len) {
  return CopyNSString(user.idToken.tokenString, buf, len);
}

size_t GoogleSignIn_GetImageUrl(GIDGoogleUser *user, char *buf, size_t len) {
  NSURL *url = [user.profile imageURLWithDimension:128];
  return CopyNSString(url ? url.absoluteString : nil, buf, len);
}

size_t GoogleSignIn_GetUserId(GIDGoogleUser *user, char *buf, size_t len) {
  return CopyNSString(user.userID, buf, len);
}

}  // extern "C"
