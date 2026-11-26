# Store Privacy Declarations — Play Console App content + App Store privacy

Answers for the store forms, worked out from what the code and SDKs actually collect. Keep this in step with
`docs/privacy-policy.html` (the stores check the two against each other) and update both when a new SDK or
data type ships. Issues: #163 (Play Console), #176 (App Store).

## What the game collects (source of truth for both stores)

| Data | Collected by | Linked to the player | Shared with third parties | Purpose |
|---|---|---|---|---|
| Player ID (UGS) | UGS Authentication | yes | no (service provider) | Account, cloud save |
| Google account id (optional link) | Google Sign-In -> UGS | yes | no | Account restore |
| Player name (PvE leagues) | `PlayerNameService` -> Cloud Code | yes | no, but shown to other players | Tournament standings |
| Game progress | UGS Cloud Save | yes | no | Cloud save |
| Gameplay analytics (incl. tutorial steps, rewarded-ad use, purchases by product and price, league entries, feedback-link use; #567) | UGS Analytics (only while `analytics.enabled`) | yes (player id) | no | Balancing |
| Crash and diagnostic reports | Cloud Diagnostics | device/app info | no | Bug fixing |
| Advertising ID (GAID / IDFA) + device and network info | LevelPlay and mediated networks | yes | **yes** (ad networks) | Ads, ad measurement, fraud prevention |
| Purchase history | Google Play Billing / StoreKit via Unity IAP; product and price also in UGS Analytics (`iap_purchase`, #567) | yes | no | Deliver purchases, refunds, Analytics |
| Push notification token | Unity Push Notifications | yes | no | Notifications |

Policy URL: https://aclemetson.github.io/mobile-idle-builder/privacy-policy.html (GitHub Pages, served from
`develop:/docs`, so a policy edit is live only once it reaches `develop`). The policy covers every row above
as of 2026-11-26 (purchase history used for analytics since #567).

Everything goes over HTTPS (all UGS, LevelPlay and store endpoints), so **encrypted in transit: yes**.
Deletion: by email request. For the Play "delete account" URL use
https://aclemetson.github.io/mobile-idle-builder/privacy-policy.html#delete.

The Google account email is stored only on the device (PlayerPrefs, shown in Settings), so it is not
"collected" in the store sense.

## Google Play: Policy and programs -> App content

### Data safety

Data collected (and processed ephemerally: no):

| Play category -> type | Collected | Shared | Required / optional | Purposes |
|---|---|---|---|---|
| Location -> Approximate location | yes | **yes** | required | Analytics, Fraud prevention, Advertising or marketing |
| Personal info -> User IDs | yes | no | required | App functionality, Account management |
| Personal info -> Name (player name) | yes | no | optional (only PvE) | App functionality |
| Financial info -> Purchase history | yes | no | optional | App functionality, Analytics |
| App activity -> App interactions | yes | no | required | Analytics |
| App activity -> Other actions (gameplay, ad interactions) | yes | **yes** | required | App functionality, Analytics, Fraud prevention, Advertising or marketing |
| App info and performance -> Crash logs | yes | no | required | Analytics |
| App info and performance -> Diagnostics | yes | no | required | Analytics, Fraud prevention, Advertising or marketing |
| Device or other IDs | yes | **yes** | required | App functionality (push), Analytics, Fraud prevention, Advertising or marketing |

- The LevelPlay rows (approximate location, ad interactions under Other actions, diagnostics, device IDs) follow
  Unity's [LevelPlay Google Data Safety Questionnaire](https://docs.unity.com/en-us/grow/levelplay/platform/legal-resources/google-data-safety-questionnaire),
  which marks them required. Re-check it when the LevelPlay SDK is updated.
- Feedback emails are sent from the player's own mail app, so they are not collected by the app.
- Account creation method: **OAuth** (Google Sign-In; Sign in with Apple later, #564). Partial deletion without
  deleting the account: **No**.

### Ads

Contains ads: **Yes** (rewarded only, opt-in).

### Advertising ID

Uses advertising ID: **Yes**, for Advertising or marketing and Analytics. The `AD_ID` permission is merged in
from `play-services-ads-identifier`; confirm in the bundle (`docs/runbooks/levelplay-ads-setup.md` section 4).

### Content rating (IARC questionnaire)

Category: Game. Answers: no violence, no sexual content, no profanity, no drugs/alcohol/tobacco, no
gambling or simulated gambling (crystals are bought currency but there are no random paid rewards), no
unrestricted internet access. Users interact: **yes**, player names are visible to other players in PvE
leagues (no chat). Digital purchases: **yes**. Shares location: no.

### Target audience and content

Pick **13 and over** only (13-15, 16-17, 18+). Choosing an under-13 band puts the app under the Families
policy, which restricts the ad networks we can mediate. Appeals to children: no.

### App access

**All functionality is available without special access.** Sign-in is anonymous; Google linking is optional.

### Other declarations

Government app: no. Financial features: no. Health: no. News: no. Data deletion: link to the policy section.

## App Store Connect: App Privacy

Data used to track you (needs the ATT prompt, which `AdConsent` shows):

- **Identifiers -> Device ID** (IDFA, only when the player allows tracking): Third-party advertising.

Data linked to you:

| Apple category -> type | Purposes |
|---|---|
| Identifiers -> User ID | App functionality |
| Contact info -> Name (player name) | App functionality |
| Purchases -> Purchase history | App functionality, Analytics |
| Usage data -> Product interaction | Analytics |
| Usage data -> Advertising data | Third-party advertising |
| Other data -> Gameplay content | App functionality |
| Identifiers -> Device ID (push token) | App functionality |

Data not linked to you:

| Apple category -> type | Purposes |
|---|---|
| Diagnostics -> Crash data | App functionality |
| Diagnostics -> Performance data, Other diagnostic data | App functionality |

Also on iOS:

- `NSUserTrackingUsageDescription` and the ATT request ship in the build (`IOSTrackingPostProcess`, `AdConsent`).
- Privacy manifests: Unity writes one for UnityFramework, and the IronSource pod brings its own. Check that
  both are in the archive (Xcode **Generate Privacy Report** on the `.xcarchive`) and that the report matches
  the table above.
- Age rating questionnaire: no objectionable content in any category; declare in-app purchases and that
  player names are visible to other players (user-generated content, no chat).
