using Microsoft.AspNetCore.DataProtection;

namespace VMS.Backend.Server.Security;

public interface ICameraCredentialProtector
{
    string Protect(string plainPassword);

    /// <summary>
    /// Returns the camera's real ONVIF password. Tolerates rows written before this protector
    /// existed — Unprotect throws CryptographicException on a value that was never Protect()-ed
    /// (plain legacy text), in which case that text *is* the password, so it's returned as-is
    /// instead of propagating the exception. DbInitializer re-encrypts every such row it finds at
    /// startup, so this fallback only ever fires once per pre-existing camera, not indefinitely.
    /// </summary>
    string Unprotect(string storedValue);

    /// <summary>True if storedValue was never run through Protect() — used by DbInitializer's one-time startup backfill to find and re-encrypt pre-existing plaintext rows.</summary>
    bool IsLegacyPlaintext(string storedValue);
}

public sealed class CameraCredentialProtector(IDataProtectionProvider provider) : ICameraCredentialProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("VMS.Camera.OnvifPassword");

    public string Protect(string plainPassword) => _protector.Protect(plainPassword);

    public string Unprotect(string storedValue)
    {
        try
        {
            return _protector.Unprotect(storedValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return storedValue;
        }
    }

    public bool IsLegacyPlaintext(string storedValue)
    {
        try
        {
            _protector.Unprotect(storedValue);
            return false;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return true;
        }
    }
}
