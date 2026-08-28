using System.Security.Cryptography;
using System.Text;

namespace VMS.MediaEngine.Onvif;

/// <summary>
/// Builds a WS-Security UsernameToken header with PasswordDigest, per the ONVIF
/// authentication profile: digest = Base64(SHA1(nonce_bytes + created_bytes + password_bytes)).
/// </summary>
public static class WsSecurityHeaderBuilder
{
    public static string Build(string username, string password)
    {
        var nonceBytes = RandomNumberGenerator.GetBytes(16);
        var nonceBase64 = Convert.ToBase64String(nonceBytes);
        var created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        var digestInput = nonceBytes
            .Concat(Encoding.UTF8.GetBytes(created))
            .Concat(Encoding.UTF8.GetBytes(password))
            .ToArray();
        var digest = Convert.ToBase64String(SHA1.HashData(digestInput));

        return $"""
            <Security xmlns="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd" xmlns:wsu="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd">
              <UsernameToken>
                <Username>{System.Security.SecurityElement.Escape(username)}</Username>
                <Password Type="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest">{digest}</Password>
                <Nonce EncodingType="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary">{nonceBase64}</Nonce>
                <wsu:Created>{created}</wsu:Created>
              </UsernameToken>
            </Security>
            """;
    }
}
