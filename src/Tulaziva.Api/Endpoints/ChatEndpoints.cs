using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Tulaziva.Application.Chat;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;

namespace Tulaziva.Api.Endpoints;

public sealed record StartConversationRequest(Guid AdId);

public sealed record SendMessageRequest(string? Body);

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        // Everything here needs a session. A conversation belongs to its two participants: for anyone else it does not exist (404).
        var group = app.MapGroup("/api/conversations").WithTags("Chat").RequireAuthorization();

        group.MapPost("/", async (StartConversationRequest request, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.SendAsync(new StartConversationCommand(principal.GetUserId(), request.AdId), cancellationToken);
                var location = $"/api/conversations/{result.Conversation.Id}";

                return result.Created ? Results.Created(location, result.Conversation) : Results.Ok(result.Conversation);
            })
            .WithSummary("Opens the chat with the owner of an ad (201), or returns the one that already exists (200). There is one per ad and user.")
            .Produces<ConversationDto>(StatusCodes.Status201Created)
            .Produces<ConversationDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", async (ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken, int page = 1, int pageSize = 20) =>
                Results.Ok(await dispatcher.QueryAsync(new GetMyConversationsQuery(principal.GetUserId(), page, pageSize), cancellationToken)))
            .WithSummary("My chats, the most recently active first, with the last message and how many are unread.")
            .Produces<PagedResult<ConversationDto>>();

        group.MapGet("/unread-count", async (ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.QueryAsync(new GetUnreadCountQuery(principal.GetUserId()), cancellationToken)))
            .WithSummary("Unread messages in all my chats.")
            .Produces<UnreadCountDto>();

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.QueryAsync(new GetConversationQuery(principal.GetUserId(), id), cancellationToken)))
            .WithSummary("One of my chats.")
            .Produces<ConversationDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/messages", async (Guid id, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken, int page = 1, int pageSize = 30) =>
                Results.Ok(await dispatcher.QueryAsync(new GetMessagesQuery(principal.GetUserId(), id, page, pageSize), cancellationToken)))
            .WithSummary("The messages of one of my chats, newest first. Reading them does not mark them as read: use POST .../read.")
            .Produces<PagedResult<MessageDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/messages", async (Guid id, SendMessageRequest request, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                var message = await dispatcher.SendAsync(new SendMessageCommand(principal.GetUserId(), id, request.Body), cancellationToken);
                return Results.Created($"/api/conversations/{id}/messages", message);
            })
            .WithSummary("Sends a text message.")
            .Produces<MessageDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/messages/files", async (
                    Guid id,
                    IFormFileCollection files,
                    [FromForm] string? body,
                    ClaimsPrincipal principal,
                    IDispatcher dispatcher,
                    CancellationToken cancellationToken) =>
            {
                var streams = files.Select(f => f.OpenReadStream()).ToList();

                try
                {
                    var incoming = files.Select((f, i) => new IncomingFile(f.FileName, f.Length, streams[i])).ToList();
                    var message = await dispatcher.SendAsync(new SendMessageCommand(principal.GetUserId(), id, body, incoming), cancellationToken);

                    return Results.Created($"/api/conversations/{id}/messages", message);
                }
                finally
                {
                    foreach (var stream in streams)
                        await stream.DisposeAsync();
                }
            })
            .WithSummary("Sends a message with up to 5 images (.jpg, .jpeg or .png, up to 10 MB each) and, optionally, a text in the form field 'body'. Nothing else is accepted.")
            // The API authenticates with a bearer token, not with cookies, so there is no antiforgery token to check.
            .DisableAntiforgery()
            .Produces<MessageDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/read", async (Guid id, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(new MarkConversationReadCommand(principal.GetUserId(), id), cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Marks as read the messages the other person sent in this chat.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/attachments/{attachmentId:guid}", async (Guid id, Guid attachmentId, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                var file = await dispatcher.QueryAsync(new GetAttachmentQuery(id, attachmentId, principal.ToRequester()), cancellationToken);

                // Images are shown in the page; everything else is a download, so the browser never runs it.
                return file.IsImage
                    ? Results.Stream(file.Content, file.ContentType)
                    : Results.Stream(file.Content, file.ContentType, file.FileName);
            })
            .WithSummary("A file of a chat. Only the two people in the chat and the admins can get it.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
