using Microsoft.AspNetCore.DataProtection;
using VMS.Backend.Server.Security;

namespace VMS.Backend.Server.Tests.Security;

public class CameraCredentialProtectorTests
{
    private static ICameraCredentialProtector CreateProtector() =>
        new CameraCredentialProtector(new EphemeralDataProtectionProvider());

    [Fact]
    public void Protect_then_Unprotect_round_trips_the_original_password()
    {
        var protector = CreateProtector();

        var protected_ = protector.Protect("Saylaw2023!");

        Assert.Equal("Saylaw2023!", protector.Unprotect(protected_));
    }

    [Fact]
    public void Protect_output_is_not_the_plaintext_password()
    {
        var protector = CreateProtector();

        Assert.NotEqual("Saylaw2023!", protector.Protect("Saylaw2023!"));
    }

    [Fact]
    public void Unprotect_on_a_never_protected_value_returns_it_unchanged()
    {
        // Simulates a Camera row written before passwords were encrypted at rest — the stored
        // value is just the plain password, not a Data Protection payload.
        var protector = CreateProtector();

        Assert.Equal("legacy-plaintext-password", protector.Unprotect("legacy-plaintext-password"));
    }

    [Fact]
    public void IsLegacyPlaintext_is_true_for_a_never_protected_value()
    {
        var protector = CreateProtector();

        Assert.True(protector.IsLegacyPlaintext("legacy-plaintext-password"));
    }

    [Fact]
    public void IsLegacyPlaintext_is_false_for_a_properly_protected_value()
    {
        var protector = CreateProtector();

        Assert.False(protector.IsLegacyPlaintext(protector.Protect("Saylaw2023!")));
    }
}
