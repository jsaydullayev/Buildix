namespace Buildix.Application.Interfaces;

/// <summary>
/// Telegram Bot API transport. Best-effort: silently no-ops when the bot token
/// is not configured or the recipient hasn't saved their Telegram ID, and never
/// throws into the caller's flow (a Telegram outage must not break a sale).
///
/// Recipients are identified by <see cref="Domain.Entities.User.TelegramChatId"/> —
/// each employee saves their own numeric ID on the Account screen.
/// </summary>
public interface ITelegramNotifier
{
    /// <summary>Send to the market owner, if they linked their Telegram ID.</summary>
    Task SendToOwnerAsync(int marketId, string message, CancellationToken cancellationToken = default);

    /// <summary>Send a text message to one chat.</summary>
    Task SendToChatAsync(long chatId, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a file (Excel report, PDF invoice) to one chat. <paramref name="caption"/>
    /// is the HTML text shown under the attachment.
    /// </summary>
    Task SendDocumentAsync(long chatId, byte[] content, string fileName, string? caption = null,
        CancellationToken cancellationToken = default);
}
