namespace Buildix.Application.DTOs;

/// <summary>
/// The produced .xlsx bytes plus the filename. The filename is computed INSIDE
/// the export service (timestamped from Tashkent "now" at build time) so the
/// controller never recomputes the clock — that would race the filename to a
/// later second/day. Shared by the report and sales Excel exporters.
/// </summary>
public sealed record ExcelExportResult(byte[] Content, string FileName);
