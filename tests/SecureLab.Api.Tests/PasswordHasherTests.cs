using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class PasswordHasherTests
{
    private static PasswordHasher<ApplicationUser> HasherWith(int iterations) =>
        new(Options.Create(new PasswordHasherOptions { IterationCount = iterations }));

    [Fact]
    public void WeakerHash_IsAccepted_ButMarkedForRehash()
    {
        var user = new ApplicationUser { DisplayName = "Hasher Test" };
        var password = $"Pw-{Guid.NewGuid():N}!Aa1";
        var oldHash = HasherWith(10_000).HashPassword(user, password);
        var currentHasher = HasherWith(100_000);

        var correct = currentHasher.VerifyHashedPassword(user, oldHash, password);
        var wrong = currentHasher.VerifyHashedPassword(user, oldHash, password + "x");

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, correct);
        Assert.Equal(PasswordVerificationResult.Failed, wrong);
    }

    [Fact]
    public void CurrentHash_IsAcceptedWithoutRehash()
    {
        var user = new ApplicationUser { DisplayName = "Hasher Test" };
        var password = $"Pw-{Guid.NewGuid():N}!Aa1";
        var hasher = HasherWith(100_000);
        var hash = hasher.HashPassword(user, password);

        var result = hasher.VerifyHashedPassword(user, hash, password);

        Assert.Equal(PasswordVerificationResult.Success, result);
    }
}