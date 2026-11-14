using System.IdentityModel.Tokens.Jwt;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;
using Tulaziva.Infrastructure.Security;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Security;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_DoesNotContainThePassword_AndIsDifferentEachTime()
    {
        var first = _hasher.Hash("Password1");
        var second = _hasher.Hash("Password1");

        first.Should().StartWith("v1.").And.NotContain("Password1");
        first.Should().NotBe(second);
    }

    [Fact]
    public void Verify_AcceptsTheRightPasswordOnly()
    {
        var hash = _hasher.Hash("Password1");

        _hasher.Verify("Password1", hash).Should().BeTrue();
        _hasher.Verify("password1", hash).Should().BeFalse();
        _hasher.Verify("", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("v2.1.AAAA.AAAA")]
    [InlineData("v1.notanumber.AAAA.AAAA")]
    [InlineData("v1.100.%%%.%%%")]
    public void Verify_ReturnsFalseForAMalformedHash(string hash)
    {
        _hasher.Verify("Password1", hash).Should().BeFalse();
    }
}

public class JwtTokenServiceTests
{
    private const string Key = "test-key-with-at-least-32-characters-long";
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static JwtTokenService CreateService(string key = Key) =>
        new(Options.Create(new JwtOptions { Issuer = "tulaziva", Audience = "tulaziva", Key = key, ExpiresMinutes = 30 }), new FixedTimeProvider(Now));

    private static readonly User Admin = new() { Email = "boss@mail.com", Role = UserRole.Admin, PreferredLanguage = "en" };

    [Fact]
    public void Create_PutsTheUserTheRoleAndTheExpiryInTheToken()
    {
        var result = CreateService().Create(Admin);

        result.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddMinutes(30));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        token.Claims.Single(c => c.Type == "sub").Value.Should().Be(Admin.Id.ToString());
        token.Claims.Single(c => c.Type == "email").Value.Should().Be("boss@mail.com");
        token.Claims.Single(c => c.Type == "role").Value.Should().Be("Admin");
        token.Claims.Single(c => c.Type == "lang").Value.Should().Be("en");
        token.Issuer.Should().Be("tulaziva");
        token.Audiences.Should().Contain("tulaziva");
    }

    [Fact]
    public void Create_SignsTheToken_SoOnlyTheSameKeyValidatesIt()
    {
        var token = CreateService().Create(Admin).Token;

        TokenValidationParameters Parameters(string key) => new()
        {
            ValidIssuer = "tulaziva",
            ValidAudience = "tulaziva",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateLifetime = false
        };

        var handler = new JwtSecurityTokenHandler();

        var valid = handler.ValidateToken(token, Parameters(Key), out _);
        valid.Should().NotBeNull();

        var tampered = () => handler.ValidateToken(token, Parameters("another-key-with-at-least-32-characters!!"), out _);
        tampered.Should().Throw<SecurityTokenException>();
    }
}
