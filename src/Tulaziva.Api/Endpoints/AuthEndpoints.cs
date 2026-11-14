using Tulaziva.Application.Auth;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Users;

namespace Tulaziva.Api.Endpoints;

public sealed record RefreshRequest(string RefreshToken);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth").AllowAnonymous();

        group.MapPost("/register", async (RegisterUserCommand command, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Created("/api/users/me", await dispatcher.SendAsync(command, cancellationToken)))
            .WithSummary("Creates a user account and returns its token. Send latitude and longitude to save where the user registered from.")
            .Produces<AuthResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/login", async (LoginCommand command, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.SendAsync(command, cancellationToken)))
            .WithSummary("Signs in with email and password and returns the token.")
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/forgot-password", async (ForgotPasswordCommand command, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(command, cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Emails a link to choose a new password. Answers the same whether the email has an account or not.")
            .RequireRateLimiting(RateLimitPolicies.AccountEmails)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/reset-password", async (ResetPasswordCommand command, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(command, cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Chooses a new password with the token of the email. The token works once and expires after an hour.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/verify-email", async (VerifyEmailCommand command, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(command, cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Confirms the address of the account with the token of the email.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/refresh", async (RefreshRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.SendAsync(new RefreshSessionCommand(request.RefreshToken ?? string.Empty), cancellationToken)))
            .WithSummary("Swaps the refresh token for a new access token and a new refresh token. The old refresh token stops working.")
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (RefreshRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(new SignOutCommand(request.RefreshToken ?? string.Empty), cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Kills the refresh token, so the session cannot be renewed any more.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/confirm-email-change", async (ConfirmEmailChangeCommand command, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(command, cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Switches the account to the new address with the token of the email sent to it.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
