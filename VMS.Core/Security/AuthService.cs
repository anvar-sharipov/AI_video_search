namespace VMS.Core.Security;

/// <summary>BCrypt work factor 12 — deliberate cost, this runs at login time only.</summary>
public class AuthService : IAuthService
{
    private const int WorkFactor = 12;

    public string HashPassword(string plaintextPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextPassword);
        return BCrypt.Net.BCrypt.HashPassword(plaintextPassword, workFactor: WorkFactor);
    }

    public bool VerifyPassword(string plaintextPassword, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(plaintextPassword) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        return BCrypt.Net.BCrypt.Verify(plaintextPassword, storedHash);
    }
}
