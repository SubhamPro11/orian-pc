using System.Security.Cryptography;
using System.Text;

namespace Orion.Platform.Windows.Services;

public interface ISecureStorageService
{
    void SetSecret(string key, string secret);
    string? GetSecret(string key);
    void RemoveSecret(string key);
}

public class WindowsSecureStorage : ISecureStorageService
{
    private const string ResourceName = "OrionStore";
    private readonly string _fallbackDir;

    public WindowsSecureStorage()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _fallbackDir = Path.Combine(localAppData, "OrionStore", "Secrets");
        Directory.CreateDirectory(_fallbackDir);
    }

    public void SetSecret(string key, string secret)
    {
        // Try WinRT PasswordVault (Credential Locker)
        try
        {
            var vault = new global::Windows.Security.Credentials.PasswordVault();
            // Remove existing if any
            try
            {
                var existing = vault.Retrieve(ResourceName, key);
                vault.Remove(existing);
            }
            catch
            {
                // Key does not exist yet
            }

            vault.Add(new global::Windows.Security.Credentials.PasswordCredential(ResourceName, key, secret));
            return;
        }
        catch
        {
            // Fall back to DPAPI
        }

        // DPAPI fallback (Windows Data Protection API encrypted with current user key)
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var encrypted = ProtectedData.Protect(secretBytes, null, DataProtectionScope.CurrentUser);
        var path = Path.Combine(_fallbackDir, $"{key}.dat");
        File.WriteAllBytes(path, encrypted);
    }

    public string? GetSecret(string key)
    {
        // Try WinRT PasswordVault
        try
        {
            var vault = new global::Windows.Security.Credentials.PasswordVault();
            var credential = vault.Retrieve(ResourceName, key);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch
        {
            // Try DPAPI fallback
        }

        var path = Path.Combine(_fallbackDir, $"{key}.dat");
        if (!File.Exists(path)) return null;

        try
        {
            var encrypted = File.ReadAllBytes(path);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return null;
        }
    }

    public void RemoveSecret(string key)
    {
        try
        {
            var vault = new global::Windows.Security.Credentials.PasswordVault();
            var credential = vault.Retrieve(ResourceName, key);
            vault.Remove(credential);
        }
        catch
        {
            // Ignore
        }

        var path = Path.Combine(_fallbackDir, $"{key}.dat");
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* Ignore */ }
        }
    }
}

