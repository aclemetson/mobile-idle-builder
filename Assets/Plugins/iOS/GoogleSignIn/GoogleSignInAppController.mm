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

#import "GoogleSignInAppController.h"
#import <GoogleSignIn/GIDSignIn.h>
#import <objc/runtime.h>

/*
 * Swizzles the app's openURL handlers so the GoogleSignIn SDK gets a chance to
 * finish an OAuth redirect. The 2017 version of this file also swizzled
 * didFinishLaunchingWithOptions to set GIDSignIn.clientID and install a delegate;
 * the modern SDK reads GIDClientID from Info.plist and is completion-handler
 * based, so only the URL handling remains. See:
 * https://developer.apple.com/library/content/documentation/Cocoa/Conceptual/ProgrammingWithObjectiveC/CustomizingExistingClasses/CustomizingExistingClasses.html
 */
@implementation UnityAppController (GoogleSignInAppController)

+ (void)load {
  Method original;
  Method swizzled;

  original = class_getInstanceMethod(
      self, @selector(application:openURL:sourceApplication:annotation:));
  swizzled = class_getInstanceMethod(
      self,
      @selector(GoogleSignInAppController:openURL:sourceApplication:annotation:));
  method_exchangeImplementations(original, swizzled);

  original =
      class_getInstanceMethod(self, @selector(application:openURL:options:));
  swizzled = class_getInstanceMethod(
      self, @selector(GoogleSignInAppController:openURL:options:));
  method_exchangeImplementations(original, swizzled);
}

- (BOOL)GoogleSignInAppController:(UIApplication *)application
                          openURL:(NSURL *)url
                sourceApplication:(NSString *)sourceApplication
                       annotation:(id)annotation {
  // Implementations were exchanged, so this calls the original UnityAppController.
  BOOL handled = [self GoogleSignInAppController:application
                                        openURL:url
                              sourceApplication:sourceApplication
                                     annotation:annotation];
  return [[GIDSignIn sharedInstance] handleURL:url] || handled;
}

- (BOOL)GoogleSignInAppController:(UIApplication *)app
                          openURL:(NSURL *)url
                          options:(NSDictionary *)options {
  BOOL handled =
      [self GoogleSignInAppController:app openURL:url options:options];
  return [[GIDSignIn sharedInstance] handleURL:url] || handled;
}

@end
