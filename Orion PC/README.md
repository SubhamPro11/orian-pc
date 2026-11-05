# Orion Store for PC

A native Windows desktop client for Orion Store, built with **C#**, **.NET**, **WinUI 3**, and the **Windows App SDK**.

Orion Store for PC brings the transparent, multi-source software catalog to Windows with zero tracking, no hidden backends, and strict security verification.

---

## Prerequisites

- **Operating System:** Windows 10 (version 1809 / build 17763 or higher) or Windows 11 (x64 or ARM64).
- **Development Tooling:**
  - [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) (LTS)
  - [Visual Studio 2022](https://visualstudio.microsoft.com/) (version 17.8+) with the **.NET Desktop Development** and **Windows App SDK C# Templates** workloads, OR the **.NET CLI**.
  - Windows App SDK 1.6 (packaged standalone with `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`, no external runtime installation required).

---

## Building the Solution

Open a PowerShell or terminal prompt in the project root:

```powershell
# Build entire solution for x64
dotnet build "Orion PC/Orion.slnx" -c Debug -p:Platform=x64

# Build entire solution for ARM64
dotnet build "Orion PC/Orion.slnx" -c Release -p:Platform=arm64
```

---

## Running the Unit Tests

The test suite covers version comparison, catalog filtering, asset matching, SHA-256 integrity, Authenticode verification, process security, and system readiness checks (119 automated tests):

```powershell
dotnet test "Orion PC/tests/Orion.Tests/Orion.Tests.csproj"
```

---

## Running the Application (Unpackaged)

To run the application directly from source:

```powershell
dotnet run --project "Orion PC/src/Orion.App/Orion.App.csproj"
```

---

## Packaging & Publishing

### 1. Standalone / Unpackaged Binary
Generate a self-contained single-folder release without requiring external .NET runtime installations:

```powershell
dotnet publish "Orion PC/src/Orion.App/Orion.App.csproj" -c Release -r win-x64 --self-contained -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishReadyToRun=true -p:PublishTrimmed=false
```
The output executable will be generated at:
`Orion PC/src/Orion.App/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/Orion.App.exe`

### 2. Packaged MSIX
To create an MSIX package:
1. In `Orion.App.csproj`, set `<WindowsPackageType>MSIX</WindowsPackageType>`.
2. Run:
   ```powershell
   dotnet publish "Orion PC/src/Orion.App/Orion.App.csproj" -c Release -r win-x64 /p:GenerateAppInstallerFile=false /p:AppxPackageSigningEnabled=false
   ```

---

## Key Features & User Experience

- **Device Switcher:**
  - Located in the custom title bar: **Windows** (default), **Android**, **Linux**, **macOS**, and **All**.
  - Easily browse software across platforms while maintaining strict installation boundaries.
- **Service-Enforced Safe States:**
  - When viewing non-Windows applications, the Download and Install actions are completely replaced by an informative **"Not available on Windows"** banner.
  - Non-installing actions (View on Source / GitHub / GitLab / Official Site, Copy Link) allow safe interaction.
  - The underlying `WindowsInstallerService` and `DownloadManager` programmatically reject non-Windows assets.
- **Architecture & Package Recommendation:**
  - Automatically matches machine CPU architecture (`x64`, `ARM64`, `x86`) to release assets.
  - Supports `.exe`, `.msi`, `.msix`, and `.zip` portable applications.
- **Security & Integrity:**
  - **HTTPS-Only:** Rejects unencrypted download endpoints.
  - **SHA-256 Verification:** Verifies file hashes before opening or executing.
  - **Authenticode Check:** Inspects embedded code signatures on executables and displays publisher details.
  - **Zip-Slip Protection:** Prevents directory traversal vulnerabilities when unpacking portable zips.
  - **DPAPI / Credential Vault:** Securely stores optional user secrets (e.g. VirusTotal API key).
- **Offline Reliability:**
  - Multi-mirror remote fetching with automatic fallback to local disk cache (`%LocalAppData%\OrionStore\Cache\`) and embedded catalog (`Assets/local_apps.json`).

---

## Project Layout

- `Orion.Core`: Pure C# domain logic, models, catalog processors, and version engines with no Windows UI dependencies.
- `Orion.Platform.Windows`: Windows OS integrations (Registry scanning, WinRT package management, Win32 Authenticode checking, background download manager, safe installer service).
- `Orion.App`: WinUI 3 modern desktop user interface using Windows App SDK, Mica material, and MVVM.
- `Orion.Tests`: Unit and integration test suite.

---

## License & Credits

Orion Store for PC is licensed under the **GNU General Public License v3.0 (GPL-3.0)**.
- Original project by [RookieEnough](https://github.com/RookieEnough/Orion-Store).
- Catalog maintained at [RookieEnough/Orion-Data](https://github.com/RookieEnough/Orion-Data).
- See [ARCHITECTURE.md](ARCHITECTURE.md) for full architectural documentation.

