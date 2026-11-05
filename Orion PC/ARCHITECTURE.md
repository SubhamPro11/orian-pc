# Orion Store for PC — Architecture & Porting Specification

## 1. Overview
Orion Store for PC is a native Windows desktop port of Orion Store (originally an Android application at `github.com/RookieEnough/Orion-Store`). It preserves Orion's philosophy: a completely transparent, public JSON catalog, multi-source release resolution (GitHub, GitLab, Codeberg), no tracking, no hidden backend, and zero analytics, engineered natively for Windows 10 and 11 using .NET, WinUI 3, and the Windows App SDK.

---

## 2. Project Structure & Architecture

The Windows solution (`Orion PC/Orion.slnx`) is organized into clear, decoupled layers:

```
Orion PC/
├── src/
│   ├── Orion.Core/                   # .NET 8 Class Library (Platform-Agnostic)
│   │   ├── Models/                   # AppItem, ReleaseInfo, StoreConfig, Enums, PlatformConstants
│   │   └── Services/                 # CatalogProcessor, VersionService, AssetResolver,
│   │                                 # HashVerifier, StorefrontEngine, XpEngine, ReleaseNotesCleaner
│   ├── Orion.Platform.Windows/       # .NET 8 (Windows SDK / WinRT)
│   │   ├── Services/                 # WindowsAppTracker (Registry + WinRT + Portable DB)
│   │   │                             # WindowsInstallerService (.exe, .msi, .msix, .zip safe extract)
│   │   │                             # DownloadManager (Range resume, Persistent queue, SHA-256, Authenticode)
│   │   │                             # WindowsSecureStorage (DPAPI & PasswordVault)
│   │   │                             # VirusTotalService (v3 API)
│   ├── Orion.App/                    # WinUI 3 Desktop App (Windows App SDK 1.6, .NET 10/8)
│   │   ├── Views/                    # HomePage, SearchPage, AppDetailPage, MyAppsPage,
│   │   │                             # DownloadsPage, SettingsPage, AboutPage
│   │   ├── ViewModels/               # MainViewModel, HomeViewModel, SearchViewModel, AppDetailViewModel,
│   │   │                             # MyAppsViewModel, DownloadsViewModel, SettingsViewModel, AboutViewModel
│   │   ├── Services/                 # CatalogDataService, NavigationService
│   │   └── Converters/               # XAML UI Converters
└── tests/
    └── Orion.Tests/                  # xUnit Test Suite (85 unit & integration tests)
```

---

## 3. Porting Map & Platform Adaptations

| Component | Android Orion Store 2.x | Windows PC Orion Store | Rationale / Mapping Details |
|---|---|---|---|
| **UI Framework** | Jetpack Compose + Material 3 Expressive | WinUI 3 + Windows App SDK Fluent Design | Native Windows desktop design (Mica backdrop, custom TitleBar, NavigationView, responsive layout). |
| **Catalog Schema** | `Orion-Data` JSON (`apps.json`, `config.json`, `mirror.json`) | Reused directly without modifications | Reuses the official schema (`"platform": "PC"`, `"platform": "Android"`, tags). Embedded fallback in `Assets/local_apps.json`. |
| **Installed App Detection** | Android `PackageManager` (`getInstalledPackages`) | `WindowsAppTracker`: 4-way Windows Registry scan + WinRT `PackageManager` + Local Portable DB | Scans HKLM & HKCU (both 64-bit and WOW6432Node), modern MSIX packages via WinRT, and custom portable installations tracked in `%LocalAppData%\OrionStore\installed_portable.json`. |
| **Installation Service** | PackageInstaller, Shizuku, Wireless ADB, Root | `WindowsInstallerService`: `.exe`, `.msi`, `.msix`, `.zip` | Native execution: silent/interactive `.exe`, `msiexec.exe /i` for `.msi`, WinRT `PackageManager.AddPackageAsync` for `.msix`, safe extraction with Start Menu shortcut creation for `.zip`. |
| **Download Management** | Android Foreground Service + DownloadManager | Background `DownloadManager` with persistent JSON queue | Supports HTTP Range resumption, SHA-256 checksum verification, Authenticode signature extraction, and queue persistence in `%LocalAppData%\OrionStore\downloads_queue.json`. |
| **Device Selection** | Implicit single-device (Android) | Top-Level Device Switcher (Windows, Android, Linux, macOS, All) | Default is Windows. Non-Windows apps are visible for cross-platform browsing, but download/install actions are disabled. |
| **Non-Windows Enforcement** | N/A | Strict Dual-Layer Enforcement (UI + Service Layer) | UI replaces Install/Download buttons with "Not available on Windows" state and safe actions (View on GitHub, Copy link). `WindowsInstallerService` and `DownloadManager` reject non-Windows requests programmatically. |
| **Architecture Detection** | Android ABI (`arm64-v8a`, `armeabi-v7a`, `x86_64`) | Windows Architecture (`X64`, `Arm64`, `X86`) | Automatically matches machine OS architecture against release assets and classifies package types. |
| **Security Storage** | Android EncryptedSharedPreferences | `WindowsSecureStorage` (DPAPI & WinRT `PasswordVault`) | Securely stores sensitive user secrets (e.g. VirusTotal API key) without plain-text exposure. |
| **Android Guardian / Debloater** | Built-in system app debloater for Android | **Removed** | Android package disablement/uninstallation has no direct or safe Windows equivalent. Removed to avoid fake or dangerous registry tweaking. |
| **Shizuku / Wireless ADB** | Android privilege escalation helper | **Removed** | Specific to Android OS architecture; Windows uses standard User Account Control (UAC) elevation when required. |
| **Analytics / Ads** | Unity Ads / Play Services hooks (if any legacy) | **Removed (Zero Analytics)** | Strict privacy and telemetry-free design. |

---

## 4. Security & Safety Model

1. **Service-Layer Guardrails:**
   - The installer service refuses to execute non-Windows assets even if callers attempt direct invocation.
2. **HTTPS-Only Enforcement:**
   - All download URLs and mirror endpoints are strictly required to use HTTPS schemes.
3. **Integrity & Checksum Verification:**
   - Downloaded assets are hashed with SHA-256 and matched against catalog/release metadata when provided.
4. **Authenticode Signature Verification:**
   - Downloaded PE binaries (`.exe`, `.msi`) are inspected using `X509Certificate2.CreateFromSignedFile` to extract and display the publisher and signing status to the user before execution.
5. **Zip-Slip & Path Traversal Prevention:**
   - Archive extraction explicitly normalizes target paths and verifies `destinationPath.StartsWith(fullExtractPath)` to prevent directory traversal attacks.
6. **No Silent Admin Execution:**
   - The app runs un-elevated (standard user privilege) and only requests UAC elevation when an installer specifically requires it.

---

## 5. License & Credits

- **License:** Orion Store for PC is licensed under the **GNU General Public License v3.0 (GPL-3.0)**, preserving the license of the original project.
- **Original Project:** Orion Store by [RookieEnough](https://github.com/RookieEnough/Orion-Store).
- **Public Catalog:** Maintained by the community at [RookieEnough/Orion-Data](https://github.com/RookieEnough/Orion-Data).
- **Components & Libraries:**
  - [.NET](https://dotnet.microsoft.com/) & [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) (WinUI 3)
  - [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
  - [Microsoft.Extensions.DependencyInjection](https://github.com/dotnet/runtime)
  - [xUnit](https://xunit.net/)

