using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>Bulut tomoni: do'kondan kelgan yozuvlarni qabul qiladi.</summary>
public interface ISyncPushService
{
    Task<SyncPushResultDto> AcceptAsync(int marketId, SyncPushDto payload, CancellationToken ct = default);
}
