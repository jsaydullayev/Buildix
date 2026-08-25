using System;

namespace Buildix.Domain.Common;

public abstract class BaseEntity : IUpdateTracked
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Oxirgi o'zgarish vaqti — <c>AppDbContext.SaveChanges</c> qo'yadi,
    /// qo'lda EMAS. Sabab va istisnolar: <see cref="IUpdateTracked"/>.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
