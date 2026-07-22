using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>
/// «Рекомендуем заказать» — computes reorder suggestions from recent sales
/// velocity so the owner sees which products to restock and by how much.
/// </summary>
public interface IReorderService
{
    Task<IReadOnlyList<ReorderSuggestionDto>> GetSuggestionsAsync(int limit = 20, CancellationToken cancellationToken = default);
}
