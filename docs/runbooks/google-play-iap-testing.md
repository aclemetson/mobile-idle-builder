# Google Play In-App Purchase Testing Runbook

How to test the crystal-pack in-app purchases (IAP) end-to-end through Google Play Console.

## What the game ships

Unity IAP (`com.unity.purchasing`) is integrated. `IAPService`
(`Assets/Scripts/Services/IAPService.cs`) registers four **consumable** crystal packs. The product
IDs below must match the Play Console product IDs **exactly**:

| Product ID       | Crystals | Target price |
| ---------------- | -------- | ------------ |
| `crystals_tier1` | 600      | $0.99        |
| `crystals_tier2` | 3,200    | $4.99        |
| `crystals_tier3` | 7,500    | $9.99        |
| `crystals_tier4` | 20,000   | $19.99       |

On a successful purchase, `IAPService.ProcessPurchase` calls
`PremiumShopService.AwardCrystals(amount)`, which adds to `SaveData.paidCurrency` (the ◆ balance)
and `SaveData.crystalsPurchased` (lifetime audit), then saves.

Both `IAPService` and `PremiumShopService` are created automatically at startup by
`PremiumServicesBootstrap` (`[RuntimeInitializeOnLoadMethod]`) — no scene wiring needed.

App package id: `com.clemtek.mobileidlebuilder` (Player Settings, Android + iPhone).

## Prerequisites (already done)

- App exists in Google Play Console under `com.clemtek.mobileidlebuilder`.
- Keystore / Play App Signing is configured for the Android build.

## Procedure

### 1. Create the four managed products

Play Console > **Monetize > Products > In-app products** > Create product.

- Create one product per row in the table above. **Product ID must match exactly**
  (`crystals_tier1` ... `crystals_tier4`) — it cannot be changed after creation.
- Set a name/description and a price (~$0.99 / $4.99 / $9.99 / $19.99 or local equivalents).
- Set each product's status to **Active**.
- Note: products can take a few hours to propagate before they are purchasable.

### 2. Add license testers

Play Console > **Setup > License testing**.

- Add the tester Gmail accounts (the Google accounts that will be signed in on the test devices).
- Set the **License response** to `RESPOND_NORMALLY`.
- Licensed testers get **test purchases**: they are not charged, payments use a "Test card, always
  approves" instrument, and consumables can be re-bought.

### 3. Internal testing track

Play Console > **Testing > Internal testing**.

- Create a release (uploaded in step 4).
- Under **Testers**, add the same accounts (email list or a linked Google Group).
- Copy the **opt-in / join URL** to share with testers.

### 4. Build and upload a signed AAB

In Unity:

1. Bump **Player Settings > Other Settings > Bundle Version Code** so it is higher than any version
   already on a track (every upload needs a new code).
2. Confirm the Internet permission and the keystore / Play App Signing upload key are configured
   (already set up).
3. Build an **Android App Bundle (.aab)** (Build Settings / Build Profiles > Build App Bundle).

In Play Console:

4. Upload the `.aab` to the **Internal testing** release and roll out.

A build must exist on a track for products to be queryable on-device, the test account must be both
a **licensed tester** (step 2) and a **track tester** (step 3), and the on-device build version must
be ≤ the version on the track.

### 5. Test on a device

1. On the test device, sign in with a tester Google account.
2. Open the internal-testing opt-in URL, accept, and install the game from Google Play.
3. Launch, open the shop > **Crystals** tab, and buy a pack.

### 6. Verify

With the device connected, watch logs:

```
adb logcat -s Unity | grep IAP
```

Expect:

- `[IAP] Store initialized.` shortly after launch.
- `[IAP] Purchase complete: crystals_tierN -> +N` after a purchase.
- The crystal (◆) balance in the HUD increases by the pack amount.
- `SaveData.crystalsPurchased` grows (audit trail).

No real charge occurs for licensed testers.

## Gotchas

- **Propagation delay:** newly activated products may take a few hours to be purchasable.
- **Version / signing match:** the on-device build must be signed with the matching upload key and
  its version code must be ≤ the track build, or billing will not connect.
- **minSdk:** 25 is fine for Google Play Billing.
- **Billing Library version:** Unity IAP 5.3.1 bundles a current Google Play Billing Library. If
  Play rejects the upload for an out-of-date billing library, update `com.unity.purchasing`.
- **Receipt validation:** local receipt validation (GooglePlayTangle) is optional and out of scope
  for this test pass; add it before production hardening.
- **Store init failure in Editor / non-Play builds:** Unity IAP uses a fake store off-device; real
  product prices and purchases only work through a Play-delivered (or matching signed) build with a
  tester account.
