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

// Local bridge header for the Google Sign-In Unity plugin (iOS).
//
// The original 2017 plugin declared a GIDSignInDelegate/GIDSignInUIDelegate
// handler here. The modern GoogleSignIn SDK (>= 6.x, integrated via CocoaPods) is
// completion-handler based and needs no such delegate, so this header now only
// pulls in the Unity app controller, which provides UnityGetGLViewController() --
// used to present the sign-in sheet.
#import <UnityAppController.h>
