# App Store In-App Purchase Testing Runbook

How to set up and test the in-app purchases (IAP) on iOS through App Store Connect. The Android
version of this runbook is `docs/runbooks/google-play-iap-testing.md`; read its "What the game ships"
section for how purchases are granted, saved and restored. Everything there applies to iOS too.

## What the game ships

`IAPService` (`Assets/Scripts/Services/IAPService.cs`) registers five products. The product IDs must
match App Store Connect **exactly** and cannot be changed after creation:

| Product ID       | App Store Connect type | Target price | Gives |
| ---------------- | ---------------------- | ------------ | ----- |
| `crystals_tier1` | Consumable             | $0.99        | 600 ◆ |
| `crystals_tier2` | Consumable             | $4.99        | 3,200 ◆ |
| `crystals_tier3` | Consumable             | $9.99        | 7,500 ◆ |
| `crystals_tier4` | Consumable             | $19.99       | 20,000 ◆ |
| `ad_free`        | **Non-Consumable**     | $9.99        | every ad reward without the ad + permanent +20% entropy income (`AdFree.cs`, #503) |

iOS differences from Android:
- Apple does not redeliver owned non-consumables at store init, so the shop shows a **Restore Purchases**
  button under the Ad-Free card on iOS (`IAPService.RestorePurchases`, `HUDPremiumShopSubController`).
  App Review requires it for any app that sells a non-consumable.
- Apple refuses a second purchase of `ad_free` and offers a free restore instead.

Bundle ID: `com.clemtek.mobileidlebuilder`.

## 1. Agreements, tax and banking (one-time)

App Store Connect > **Business** (Agreements, Tax, and Banking).

- The **Paid Applications** agreement must be **Active**, with bank account and tax forms complete.
- Until it is, `IAPService` initializes but no products load, even in TestFlight. This is the most common
  reason for "products not found".

## 2. Create the five products

App Store Connect > **Apps** > Quantum Foundry > **Monetization > In-App Purchases** > **+**.

For each row in the table:
1. **Type**: Consumable for the four crystal packs, Non-Consumable for `ad_free`.
2. **Reference Name**: internal only, e.g. `Crystals 600` / `Ad-Free`.
3. **Product ID**: exactly as in the table.
4. **Availability**: all countries (or match the app).
5. **Price Schedule**: pick the tier closest to the target price; Apple fills in other countries.
6. **App Store Localization** (English (U.S.) at least): Display Name and Description. Keep the display
   names the same as the in-game shop cards (reviewers compare them):
   - `crystals_tier1`: "Starter Pack" / "600 crystals to spend on boosts and the shop."
   - `crystals_tier2`: "Explorer Pack" / "3,200 crystals to spend on boosts and the shop."
   - `crystals_tier3`: "Founder Pack" / "7,500 crystals to spend on boosts and the shop."
   - `crystals_tier4`: "Quantum Pack" / "20,000 crystals to spend on boosts and the shop."
   - `ad_free`: "Ad-Free Forever" / "Every ad reward without the ad, plus +20% entropy income forever."
7. **Review Information**: the screenshot of the shop showing the product, from `docs/store/iap-review/`
   (1290x2796, the 6.7" iPhone size; Editor captures of Shop > Buy):
   - `shop-ad-free.png`: Ad-Free Forever (top card) and the first three packs. Use it for `ad_free`,
     `crystals_tier1`, `crystals_tier2` and `crystals_tier3`.
   - `shop-crystal-packs.png`: the list scrolled down to all four packs. Use it for `crystals_tier4`.
   Review notes (optional): "Shop > Buy tab, opened from the menu button at the top left."
   Retake them if the shop's look or the product names change.
8. `ad_free` only: **Family Sharing** is optional. Turning it on cannot be undone.

Each product should reach status **Ready to Submit**. That is enough for TestFlight and sandbox testing.
The first products must be submitted **with an app version**: on the version page, under
**In-App Purchases and Subscriptions**, add all five before submitting the build for review.

New products can take up to an hour to become loadable.

## 3. Testers

- **TestFlight builds** (the normal path, `release-ios.yml`): purchases run in the sandbox automatically
  with the tester's own Apple ID. They are **not charged**. No sandbox account needed.
- **Xcode / development builds**: create a Sandbox tester (App Store Connect > **Users and Access >
  Sandbox > Test Accounts**) with an email address that is not an existing Apple ID. On the device, sign in
  under **Settings > App Store > Sandbox Account** (not the main Apple ID).

## 4. Test on a TestFlight build

Install the latest build from TestFlight, then:

1. Open the shop > **Crystals** tab. All four packs and Ad-Free should show **store prices** (local
   currency). Placeholder or missing prices mean the products did not load: re-check steps 1-2.
2. Buy each crystal pack. The ◆ balance goes up by the pack amount.
3. Buy **Ad-Free**. The card shows **Owned** and rewarded ads grant without playing an ad.
4. Delete the app, reinstall from TestFlight, sign in to the same game account, tap **Restore Purchases**.
   Ad-Free comes back as Owned.
5. Try to buy Ad-Free again: the game refuses (or Apple offers a free restore).

## 5. Logs

Connect the iPhone to a Mac and open **Console.app** (select the device, filter on `IAP`), or run the
build from Xcode. Expect:

- `[IAP] Store initialized.` shortly after launch (`[IAP] Init failed: ...` means no products loaded).
- `[IAP] Purchase complete: crystals_tierN → +N◆` after a pack purchase, `[IAP] Ad-free purchased.` after
  Ad-Free.
- `[IAP] Ad-free restored from the store receipt.` after a successful Restore Purchases;
  `[IAP] Restore failed: ...` points to a sandbox / Apple ID problem.

## Gotchas

- **Products not found**: Paid Applications agreement not active, a product ID typo, a product missing
  its localization or price (status stuck at **Missing Metadata**), or created less than an hour ago.
- **Feature flag**: `iap.enabled` off in Remote Config skips store init entirely
  (`[IAP] Disabled by feature flag`).
- **Environment**: TestFlight builds are dev builds (`DEV_ENVIRONMENT`, see #416), so purchases update the
  save in the UGS **development** environment.
- **Receipt validation**: local receipt validation (AppleTangle) is out of scope for this test pass; add
  it before production hardening, as on Android.
- **API**: `RestorePurchases` uses the legacy `IAppleExtensions.RestoreTransactions`, which IAP 5 marks
  obsolete but still supports. Move it to the IAP 5 API when the rest of `IAPService` moves.
