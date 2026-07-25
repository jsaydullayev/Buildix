namespace Buildix.Application.Interfaces;

/// <summary>
/// Sends Telegram notifications to a market owner (day summary, overdue debts,
/// cash-withdrawal requests). Best-effort: silently no-ops when the bot token
/// is not configured or the owner hasn't linked their chat (sent /start yet).
/// Never throws into the caller's flow.
/// </summary>
public interface ITelegramNotifier
{
    /// <summary>Send a message to the given market's owner, if enabled/linked.</summary>
    Task SendToOwnerAsync(int marketId, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send to a specific chat id — used by the bot webhook to answer the chat
    /// that issued a command (the chat may not be linked to any market yet).
    /// </summary>
    Task SendToChatAsync(long chatId, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Market whose owner has linked this chat, or null. Lets the webhook decide
    /// whether the sender is entitled to the market's figures.
    /// </summary>
    Task<int?> ResolveMarketByChatAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Links a chat to a market when the owner sends /start: matches the sender's
    /// @username to MarketSettings.OwnerTelegram and stores the chat id. Returns
    /// true when a link was made. Used by the webhook.
    /// </summary>
    Task<bool> TryLinkChatAsync(string username, long chatId, CancellationToken cancellationToken = default);
}
