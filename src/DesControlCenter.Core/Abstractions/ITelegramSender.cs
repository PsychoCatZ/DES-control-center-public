namespace DesControlCenter.Core.Abstractions;

/// <summary>Sends a plain-text message to the owner's own Telegram chat via their own bot.</summary>
public interface ITelegramSender
{
    /// <summary>False until a bot token and chat id are in place — everything then quietly does nothing.</summary>
    bool IsConfigured { get; }

    /// <summary>True when Telegram accepted the message. Never throws.</summary>
    Task<bool> SendAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>Remembers when the last weekly report went out, so a restart doesn't repeat it.</summary>
public interface IReportStateStore
{
    DateTimeOffset? LoadLastSent();
    void SaveLastSent(DateTimeOffset sentAt);
}
