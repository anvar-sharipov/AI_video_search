using VMS.Backend.Server.Api;

namespace VMS.Backend.Server.Tests.Api;

public class RefreshTokenGeneratorTests
{
    [Fact]
    public void GenerateRaw_produces_different_values_each_call()
    {
        var a = RefreshTokenGenerator.GenerateRaw();
        var b = RefreshTokenGenerator.GenerateRaw();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Hash_is_deterministic_for_the_same_input()
    {
        var raw = RefreshTokenGenerator.GenerateRaw();

        Assert.Equal(RefreshTokenGenerator.Hash(raw), RefreshTokenGenerator.Hash(raw));
    }

    [Fact]
    public void Hash_differs_for_different_input()
    {
        var a = RefreshTokenGenerator.GenerateRaw();
        var b = RefreshTokenGenerator.GenerateRaw();

        Assert.NotEqual(RefreshTokenGenerator.Hash(a), RefreshTokenGenerator.Hash(b));
    }

    [Fact]
    public void Hash_never_contains_the_raw_token_value()
    {
        // The whole point of hashing before storage (RefreshToken.TokenHash) is that the raw,
        // usable token value never lands in the database — just check the obvious failure mode.
        var raw = RefreshTokenGenerator.GenerateRaw();

        Assert.DoesNotContain(raw, RefreshTokenGenerator.Hash(raw));
    }
}
