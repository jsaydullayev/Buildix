using Buildix.Application.DTOs;
using Buildix.Domain.Entities;

namespace Buildix.Application.Interfaces;

public interface IUserService
{
    Task<UserDto?> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserDto?> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<IEnumerable<UserDto>> GetAllUsersAsync(string? search = null, string? role = null, CancellationToken cancellationToken = default);
    Task<UserDto> CreateUserAsync(CreateUserDto request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<UserDto?> UpdateUserAsync(UpdateUserDto request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<UserDto?> UpdateProfileAsync(Guid userId, UpdateProfileDto request, CancellationToken cancellationToken = default);
    Task<UserDto?> UpdateProfileImageAsync(Guid userId, UpdateProfileImageDto request, CancellationToken cancellationToken = default);
    Task<bool> DeleteUserAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeactivateUserAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> ActivateUserAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<UserDto?> UpdateShiftAsync(Guid id, UpdateShiftDto request, Guid actorUserId, CancellationToken cancellationToken = default);

    // Owner RBAC — per-user permission configuration (Owner-only).
    Task<UserPermissionsDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserPermissionsDto?> UpdateUserPermissionsAsync(Guid id, UpdatePermissionsDto request, Guid actorUserId, CancellationToken cancellationToken = default);
}
