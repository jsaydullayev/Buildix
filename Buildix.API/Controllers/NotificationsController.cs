using Buildix.API.Authorization;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Buildix.API.Controllers;

/// <summary>In-app bildirishnomalar — Уведомления feed + o'qilgan holati.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    /// <summary>Feed — kategoriya filtri bilan (Warehouse/Debt/Shift/Supply).</summary>
    [HttpGet]
    [RequirePermission(PermissionKeys.NotificationsAccess)]
    public async Task<ActionResult<NotificationFeedDto>> GetFeed(
        [FromQuery] string? category = null, CancellationToken ct = default)
        => Ok(await _notifications.GetFeedAsync(category, 50, ct));

    /// <summary>O'qilmaganlar soni — header qo'ng'irog'i badge'i.</summary>
    [HttpGet("unread-count")]
    [RequirePermission(PermissionKeys.NotificationsAccess)]
    public async Task<ActionResult<int>> GetUnreadCount(CancellationToken ct = default)
        => Ok(new { count = await _notifications.GetUnreadCountAsync(ct) });

    [HttpPost("{id:guid}/read")]
    [RequirePermission(PermissionKeys.NotificationsAccess)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct = default)
    {
        await _notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    [RequirePermission(PermissionKeys.NotificationsAccess)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct = default)
    {
        await _notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}
