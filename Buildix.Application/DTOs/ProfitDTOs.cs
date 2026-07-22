using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

public record ProfitSummaryDto(
    [property: JsonPropertyName("todayProfit")] decimal TodayProfit,
    [property: JsonPropertyName("weekProfit")] decimal WeekProfit,
    [property: JsonPropertyName("monthProfit")] decimal MonthProfit,
    [property: JsonPropertyName("totalProfit")] decimal TotalProfit
);
