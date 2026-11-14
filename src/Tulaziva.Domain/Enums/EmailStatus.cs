namespace Tulaziva.Domain.Enums;

/// <summary>Where an email is in its life, as recorded in the email log.</summary>
public enum EmailStatus
{
    /// <summary>Waiting to be sent, or being tried again (see EmailLog.Attempts).</summary>
    Queued = 0,

    /// <summary>The mail server accepted it. That is not proof that it reached the inbox.</summary>
    Sent = 1,

    /// <summary>Every attempt failed. EmailLog.LastError says why.</summary>
    Failed = 2,

    /// <summary>SMTP is not configured, so the email was only written to the application log.</summary>
    Logged = 3
}
