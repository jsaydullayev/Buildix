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

    /// <summary>
    /// Send a text message to one chat. Returns false when it did not reach
    /// Telegram (no token, transport error, non-2xx). Callers that mark work as
    /// "announced" MUST check this — a once-per-product alert that is stamped
    /// after a silent failure is lost for good.
    /// </summary>
    Task<bool> SendToChatAsync(long chatId, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a message together with the bot's button keyboard. Each inner list is
    /// one row of button captions; tapping a button sends its caption back as an
    /// ordinary message, which is how the handler routes it. Pass an empty
    /// keyboard to remove the buttons.
    /// </summary>
    Task SendWithKeyboardAsync(long chatId, string message,
        IReadOnlyList<IReadOnlyList<string>> keyboard, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a file (Excel report, PDF invoice) to one chat. <paramref name="caption"/>
    /// is the HTML text shown under the attachment.
    /// </summary>
    Task SendDocumentAsync(long chatId, byte[] content, string fileName, string? caption = null,
        CancellationToken cancellationToken = default);
}
