using FluentAssertions;
using FluentValidation;
using Tulaziva.Application.Auth;
using Tulaziva.Application.Common;
using Tulaziva.Application.Localization;
using Tulaziva.Domain.Entities;
using Tulaziva.Domain.Enums;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Auth;

/// <summary>The links of the emails: verify the address, forgot the password, choose a new one.</summary>
public class AccountEmailTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUserRepository _users = new();
    private readonly FakeUserTokenRepository _tokens = new();
    private readonly CapturingEmailSender _email = new();
    private readonly MutableTimeProvider _time = new(T0);
    private readonly AccountEmailService _service;
    private readonly User _ana = new() { Email = "ana@mail.com", DisplayName = "Ana", PreferredLanguage = "es", PasswordHash = "hashed:OldPass1" };

    public AccountEmailTests()
    {
        _users.Users.Add(_ana);
        _service = new AccountEmailService(_tokens, _email, new StaticAppLinks(), _time);
    }

    private ResetPasswordCommandHandler ResetHandler() => new(_tokens, new FakePasswordHasher(), new Localizer(), _time);

    private VerifyEmailCommandHandler VerifyHandler() => new(_tokens, new Localizer(), _time);

    private async Task<string> RequestReset()
    {
        await _service.SendPasswordResetAsync(_ana, CancellationToken.None);
        var secret = _email.LastTokenFor(_ana.Email);

        // The service created the token without its user (the real repository loads it); the fake needs it set.
        _tokens.Tokens.Last().User = _ana;

        return secret;
    }

    // ---- The service ----

    [Fact]
    public async Task SendVerification_StoresOnlyTheHashAndEmailsTheLinkInTheLanguageOfTheUser()
    {
        await _service.SendVerificationAsync(_ana, CancellationToken.None);

        var secret = _email.LastTokenFor("ana@mail.com");
        var token = _tokens.Tokens.Should().ContainSingle().Subject;

        token.Purpose.Should().Be(TokenPurpose.EmailVerification);
        token.TokenHash.Should().Be(UserTokenRules.Hash(secret)).And.NotContain(secret);
        token.ExpiresAtUtc.Should().Be(T0.UtcDateTime.AddDays(2));

        var message = _email.Sent.Should().ContainSingle().Subject;
        message.Subject.Should().Contain("Verifica");
        message.Body.Should().Contain("Hola Ana").And.Contain($"https://app.test/verify-email?token={secret}");
    }

    [Fact]
    public async Task SendPasswordReset_InEnglishForAnEnglishUser_AndTheLinkLastsAnHour()
    {
        _ana.PreferredLanguage = "en";

        await _service.SendPasswordResetAsync(_ana, CancellationToken.None);

        _email.Sent.Single().Subject.Should().Contain("Reset your");
        _tokens.Tokens.Single().ExpiresAtUtc.Should().Be(T0.UtcDateTime.AddHours(1));
    }

    [Fact]
    public async Task AskingAgain_KillsTheOlderLink()
    {
        await _service.SendPasswordResetAsync(_ana, CancellationToken.None);
        await _service.SendPasswordResetAsync(_ana, CancellationToken.None);

        _tokens.Tokens.Should().HaveCount(2);
        _tokens.Tokens[0].UsedAtUtc.Should().NotBeNull();
        _tokens.Tokens[1].UsedAtUtc.Should().BeNull();
    }

    // ---- Forgot password ----

    [Fact]
    public async Task Forgot_KnownEmail_SendsTheResetLink()
    {
        var accountEmails = new FakeAccountEmailService();

        await new ForgotPasswordCommandHandler(_users, accountEmails).HandleAsync(new ForgotPasswordCommand("  ANA@mail.com "), CancellationToken.None);

        accountEmails.PasswordResets.Should().ContainSingle().Which.Should().BeSameAs(_ana);
    }

    [Fact]
    public async Task Forgot_UnknownOrDisabledAccount_AnswersTheSameAndSendsNothing()
    {
        var disabled = new User { Email = "off@mail.com", IsActive = false };
        _users.Users.Add(disabled);
        var accountEmails = new FakeAccountEmailService();
        var handler = new ForgotPasswordCommandHandler(_users, accountEmails);

        var unknown = () => handler.HandleAsync(new ForgotPasswordCommand("nobody@mail.com"), CancellationToken.None);
        var off = () => handler.HandleAsync(new ForgotPasswordCommand("off@mail.com"), CancellationToken.None);

        await unknown.Should().NotThrowAsync();
        await off.Should().NotThrowAsync();
        accountEmails.PasswordResets.Should().BeEmpty();
    }

    // ---- Reset password ----

    [Fact]
    public async Task Reset_WithTheLink_ChangesThePasswordAndUsesTheToken()
    {
        var secret = await RequestReset();

        await ResetHandler().HandleAsync(new ResetPasswordCommand(secret, "NewPass123"), CancellationToken.None);

        _ana.PasswordHash.Should().Be("hashed:NewPass123");
        _ana.EmailVerifiedAtUtc.Should().Be(T0.UtcDateTime, "following a link sent to the address proves it is theirs");
        _tokens.Tokens.Single().UsedAtUtc.Should().Be(T0.UtcDateTime);
    }

    [Fact]
    public async Task Reset_TheSameLinkTwice_FailsTheSecondTime()
    {
        var secret = await RequestReset();
        var handler = ResetHandler();
        await handler.HandleAsync(new ResetPasswordCommand(secret, "NewPass123"), CancellationToken.None);

        var again = () => handler.HandleAsync(new ResetPasswordCommand(secret, "Another456"), CancellationToken.None);

        (await again.Should().ThrowAsync<ValidationException>()).Which.Errors.Single().ErrorMessage.Should().Be(new Localizer().Get(MessageKeys.TokenInvalid));
        _ana.PasswordHash.Should().Be("hashed:NewPass123");
    }

    [Fact]
    public async Task Reset_AfterItExpired_Fails()
    {
        var secret = await RequestReset();
        _time.Advance(TimeSpan.FromMinutes(61));

        var act = () => ResetHandler().HandleAsync(new ResetPasswordCommand(secret, "NewPass123"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _ana.PasswordHash.Should().Be("hashed:OldPass1");
    }

    [Fact]
    public async Task Reset_ARandomTokenOrOneOfAnotherPurpose_Fails()
    {
        await _service.SendVerificationAsync(_ana, CancellationToken.None);
        var verificationSecret = _email.LastTokenFor(_ana.Email);
        _tokens.Tokens.Last().User = _ana;

        var random = () => ResetHandler().HandleAsync(new ResetPasswordCommand("not-a-token", "NewPass123"), CancellationToken.None);
        var wrongPurpose = () => ResetHandler().HandleAsync(new ResetPasswordCommand(verificationSecret, "NewPass123"), CancellationToken.None);

        await random.Should().ThrowAsync<ValidationException>();
        await wrongPurpose.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Reset_ADisabledAccount_Fails()
    {
        var secret = await RequestReset();
        _ana.IsActive = false;

        var act = () => ResetHandler().HandleAsync(new ResetPasswordCommand(secret, "NewPass123"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Theory]
    [InlineData("short1", false)]
    [InlineData("onlyletters", false)]
    [InlineData("12345678", false)]
    [InlineData("Valid1234", true)]
    public void ResetValidator_AppliesThePasswordRules(string password, bool valid)
    {
        new ResetPasswordCommandValidator(new Localizer()).Validate(new ResetPasswordCommand("token", password)).IsValid.Should().Be(valid);
    }

    [Fact]
    public void ResetValidator_RequiresTheToken()
    {
        new ResetPasswordCommandValidator(new Localizer()).Validate(new ResetPasswordCommand("", "Valid1234")).IsValid.Should().BeFalse();
    }

    // ---- Verify email ----

    [Fact]
    public async Task Verify_WithTheLink_MarksTheAddressAsVerified()
    {
        await _service.SendVerificationAsync(_ana, CancellationToken.None);
        var secret = _email.LastTokenFor(_ana.Email);
        _tokens.Tokens.Single().User = _ana;

        await VerifyHandler().HandleAsync(new VerifyEmailCommand(secret), CancellationToken.None);

        _ana.EmailVerifiedAtUtc.Should().Be(T0.UtcDateTime);
        _tokens.Tokens.Single().UsedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Verify_WithAResetLink_Fails()
    {
        var secret = await RequestReset();

        var act = () => VerifyHandler().HandleAsync(new VerifyEmailCommand(secret), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _ana.EmailVerifiedAtUtc.Should().BeNull();
    }

    // ---- Resend verification ----

    [Fact]
    public async Task Resend_SendsALinkOnlyWhileTheAddressIsNotVerified()
    {
        var accountEmails = new FakeAccountEmailService();
        var handler = new ResendVerificationCommandHandler(_users, accountEmails);

        await handler.HandleAsync(new ResendVerificationCommand(_ana.Id), CancellationToken.None);
        _ana.EmailVerifiedAtUtc = T0.UtcDateTime;
        await handler.HandleAsync(new ResendVerificationCommand(_ana.Id), CancellationToken.None);

        accountEmails.Verifications.Should().ContainSingle();
    }

    [Fact]
    public async Task Resend_UnknownUser_ThrowsNotFound()
    {
        var handler = new ResendVerificationCommandHandler(_users, new FakeAccountEmailService());

        var act = () => handler.HandleAsync(new ResendVerificationCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---- Sign-up ----

    [Fact]
    public async Task Register_SendsTheVerificationLink_AndTheUserStartsUnverified()
    {
        var accountEmails = new FakeAccountEmailService();
        var handler = new RegisterUserCommandHandler(_users, new FakePasswordHasher(), TestSessions.Create(), accountEmails);

        var response = await handler.HandleAsync(new RegisterUserCommand("new@mail.com", "Password1", "New"), CancellationToken.None);

        accountEmails.Verifications.Should().ContainSingle().Which.Email.Should().Be("new@mail.com");
        response.User.EmailVerified.Should().BeFalse();
    }
}
