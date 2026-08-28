namespace VMS.Core.Security;

public interface IAuthService
{
    string HashPassword(string plaintextPassword);
    bool VerifyPassword(string plaintextPassword, string storedHash);
}
