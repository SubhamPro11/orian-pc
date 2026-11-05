# Security Policy

## Supported Versions

Only the latest active development versions of Orion Store for PC receive security updates.

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x (PC) | :white_check_mark: |
| < 1.0.0 | :x:                |

## Security Architecture & Guarantees

Orion Store for PC is designed with security as a core principle:

1. **Non-Windows Execution Refusal**: Non-Windows targets (Android APKs, Linux DEB/RPM/AppImage, macOS DMG, etc.) can be viewed in the catalog for discovery, but their download and execution are strictly refused at both the UI and service layers (`WindowsInstallerService` and `DownloadManager`).
2. **Cryptographic Integrity & Signature Verification**:
   - SHA-256 hashes are computed for every downloaded asset and verified against catalog or release digests. Mismatched downloads are automatically deleted from disk.
   - Executable binaries (`.exe`, `.msi`) are verified via native Windows Authenticode APIs (`WinVerifyTrust`), detecting tampered digests and invalid certificates.
   - MSIX packages (`.msix`, `.msixbundle`, `.appx`, `.appxbundle`) are inspected for signed `AppxSignature.p7x` manifests.
   - Any unsigned or unverified installer requires explicit user confirmation before running. Unverified files are never executed silently.
3. **Filesystem Traversal Protection**:
   - Portable package extractions validate all zip entry paths against canonical directory targets, rejecting path traversal attempts (`..`, absolute paths, drive letters, and Windows reserved device names: `CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`).
   - Zip-bomb protections enforce strict thresholds on entry count (<= 10,000), uncompressed payload size (<= 2 GB), and compression expansion ratios (<= 100x). Reparse points and symlinks are rejected.
   - Temporary download paths and staging extraction directories are isolated and verified for path containment prior to file operations or uninstallation.
4. **Process Launch Hardening**:
   - Installers and uninstaller commands are parsed directly without shell wrappers (`cmd.exe /c` is strictly prohibited).
   - URI schemes for external links are strictly restricted to `http` and `https` (`file:`, `javascript:`, `ms-msdt:`, and other URI schemes are rejected).
   - VirusTotal queries strictly validate SHA-256 hashes against a 64-character hexadecimal regex pattern.
5. **Secure Storage**:
   - API tokens and secrets are stored in the Windows Credential Locker (`PasswordVault`), with DPAPI (`ProtectedData.Protect` scoped to `CurrentUser`) fallback where the storage directory is ACL-restricted exclusively to the active Windows user.
   - Logging redaction guarantees that secrets, tokens, and personal credential keys are never persisted in plaintext log files.

## Reporting a Vulnerability

If you discover a security vulnerability in Orion Store for PC:

1. Please **do not** open a public GitHub issue.
2. Report the vulnerability privately via GitHub Private Vulnerability Reporting or contact the maintainers.
3. Provide detailed steps to reproduce the vulnerability, including sample payloads or commands where applicable.
4. Maintainers will review the report, acknowledge receipt within 48 hours, and coordinate a fix and advisory release.

