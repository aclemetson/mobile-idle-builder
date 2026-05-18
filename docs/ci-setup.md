# CI/CD Setup Guide

One-time setup steps to activate the GitHub Actions pipeline.

## 1. Self-Hosted Runner (Windows build machine)

The Android build jobs run on your local machine to avoid consuming GitHub Actions cloud minutes.

1. Go to: **GitHub repo → Settings → Actions → Runners → New self-hosted runner**
2. Select **Windows** platform
3. Follow the download + configure instructions shown
4. When prompted for labels, use: `self-hosted,windows,unity`
5. Install as a Windows service so it starts automatically:
   ```powershell
   .\svc.cmd install
   .\svc.cmd start
   ```
6. Set the `UNITY_PATH` environment variable on the runner machine if Unity is not installed at the default path:
   ```
   C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe
   ```
   To set permanently: System → Advanced system settings → Environment Variables → add `UNITY_PATH`.

## 2. Unity License Secret (for GameCI PR test runner)

GameCI runs Unity tests in Docker on GitHub-hosted Ubuntu runners. It needs a Unity license.

### Get the license file

1. **Request an activation file** — run this workflow once manually:
   - In your repo: **Actions → Get Activation File** (you'll need to create it once — see GameCI docs)
   - Or locally: run Unity with `-batchmode -createManualActivationFile` to generate a `.alf`

2. **Activate the license**:
   - Go to [https://license.unity3d.com/manual](https://license.unity3d.com/manual)
   - Upload the `.alf` file
   - Download the `.ulf` license file

3. **Add the secret**:
   - GitHub repo → Settings → Secrets and variables → Actions → New repository secret
   - Name: `UNITY_LICENSE`
   - Value: paste the full XML content of the `.ulf` file

4. Also add:
   - `UNITY_EMAIL` — your Unity account email
   - `UNITY_PASSWORD` — your Unity account password

## 3. Android Keystore Secret

The keystore file is gitignored. Encode it to base64 and store it as a secret.

```powershell
# Run from the project root
$bytes = [System.IO.File]::ReadAllBytes("secrets\user.keystore")
[System.Convert]::ToBase64String($bytes) | Set-Clipboard
```

Add to GitHub Secrets:
- `ANDROID_KEYSTORE_BASE64` — paste the base64 string from clipboard
- `ANDROID_KEYSTORE_PASS` — keystore password
- `ANDROID_KEY_ALIAS` — `mobile-idle-builder`
- `ANDROID_KEY_PASS` — key password

## 4. Firebase App Distribution

1. In Firebase Console: go to **App Distribution**
2. Enable App Distribution for the Android app
3. Create a tester group named `internal-testers` and add tester emails

4. Get a CI token:
   ```bash
   firebase login:ci
   ```
   Copy the token output.

5. Get the Firebase App ID:
   - Firebase Console → Project settings → Your apps → Android app → App ID
   - Looks like: `1:835677862122:android:xxxxx`

6. Add to GitHub Secrets:
   - `FIREBASE_TOKEN` — the token from `firebase login:ci`
   - `FIREBASE_APP_ID` — the App ID from Firebase Console

## 5. Google Play Internal Testing

1. In **Google Play Console**: ensure the app has at least one release in any track (required to use the API)

2. Create a service account:
   - Google Play Console → Setup → API access → Create new service account
   - Link to a Google Cloud project → Create key (JSON type)
   - Grant the service account **Release manager** permission in Play Console

3. Add to GitHub Secrets:
   - `GOOGLE_PLAY_JSON_KEY` — paste the full JSON content of the service account key file

## 6. GitHub Actions Permissions

Ensure the repo allows Actions to create tags and releases:
- Settings → Actions → General → Workflow permissions → **Read and write permissions**

## Secrets Summary

| Secret | Source | Used By |
|--------|--------|---------|
| `UNITY_LICENSE` | Unity license portal | PR tests (GameCI) |
| `UNITY_EMAIL` | Unity account | PR tests (GameCI) |
| `UNITY_PASSWORD` | Unity account | PR tests (GameCI) |
| `ANDROID_KEYSTORE_BASE64` | base64 of `secrets/user.keystore` | Dev + Prod builds |
| `ANDROID_KEYSTORE_PASS` | keystore password | Dev + Prod builds |
| `ANDROID_KEY_ALIAS` | `mobile-idle-builder` | Dev + Prod builds |
| `ANDROID_KEY_PASS` | key password | Dev + Prod builds |
| `FIREBASE_TOKEN` | `firebase login:ci` | Dev distribution |
| `FIREBASE_APP_ID` | Firebase Console | Dev distribution |
| `GOOGLE_PLAY_JSON_KEY` | GCP service account JSON | Prod distribution |

## Local Testing

Run Unity edit mode tests locally without triggering CI:

```powershell
.\scripts\test-local.ps1
```

Override Unity path if needed:
```powershell
.\scripts\test-local.ps1 -UnityPath "C:\path\to\Unity.exe"
```

Results are saved to `TestResults\editmode.xml`.
