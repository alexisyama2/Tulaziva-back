namespace Tulaziva.Application.Common;

/// <summary>
/// An expected failure of a use case. MessageKey is a key of the localized messages, so the API
/// can answer in the language of the request without the handlers knowing about it.
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string messageKey) : base(messageKey)
    {
        MessageKey = messageKey;
    }

    public string MessageKey { get; }
}

/// <summary>The request clashes with existing data, for example an email that is already registered.</summary>
public sealed class ConflictException : AppException
{
    public ConflictException(string messageKey) : base(messageKey) { }
}

/// <summary>The caller could not be authenticated. Never says which part of the credentials was wrong.</summary>
public sealed class UnauthenticatedException : AppException
{
    public UnauthenticatedException(string messageKey) : base(messageKey) { }
}

/// <summary>The caller is signed in but is not allowed to do this, for example editing an ad of another user.</summary>
public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string messageKey) : base(messageKey) { }
}

public sealed class NotFoundException : AppException
{
    public NotFoundException(string messageKey) : base(messageKey) { }
}
