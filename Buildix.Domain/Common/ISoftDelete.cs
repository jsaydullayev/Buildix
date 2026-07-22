namespace Buildix.Domain.Common;

/// <summary>
/// Soft-deletable entity. Rows are hidden by a global query filter
/// (<c>!IsDeleted</c>) rather than physically removed, preserving financial /
/// audit history. <see cref="DeletedAt"/> records WHEN it happened (UTC) so the
/// full contract is uniform across every implementer.
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}
