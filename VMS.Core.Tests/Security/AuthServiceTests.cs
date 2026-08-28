using VMS.Core.Security;

namespace VMS.Core.Tests.Security;

public class AuthServiceTests
{
    private readonly AuthService _sut = new();

    [Fact]
    public void HashPassword_produces_a_hash_that_verifies_against_the_original_password()
    {
        var hash = _sut.HashPassword("Correct-Horse-Battery-Staple");

        Assert.True(_sut.VerifyPassword("Correct-Horse-Battery-Staple", hash));
    }

    [Fact]
    public void VerifyPassword_fails_for_a_wrong_password()
    {
        var hash = _sut.HashPassword("Correct-Horse-Battery-Staple");

        Assert.False(_sut.VerifyPassword("wrong-password", hash));
    }

    [Fact]
    public void HashPassword_never_stores_the_password_in_plaintext()
    {
        var hash = _sut.HashPassword("Correct-Horse-Battery-Staple");

        Assert.DoesNotContain("Correct-Horse-Battery-Staple", hash);
    }

    [Fact]
    public void Same_password_hashed_twice_yields_different_hashes_due_to_salting()
    {
        var hash1 = _sut.HashPassword("Correct-Horse-Battery-Staple");
        var hash2 = _sut.HashPassword("Correct-Horse-Battery-Staple");

        Assert.NotEqual(hash1, hash2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void VerifyPassword_returns_false_for_empty_input_instead_of_throwing(string? badInput)
    {
        var hash = _sut.HashPassword("Correct-Horse-Battery-Staple");

        Assert.False(_sut.VerifyPassword(badInput!, hash));
    }
}
