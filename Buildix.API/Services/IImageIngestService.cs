using Buildix.API.Validation;
using Buildix.Application.Common;
using Buildix.Application.DTOs;

namespace Buildix.API.Services;

/// <summary>Successful product-image ingest: the decoded bytes plus the file
/// extension derived from the sniffed magic bytes.</summary>
public sealed record IngestedProductImage(byte[] Bytes, string Extension);

/// <summary>
/// Parses an inbound image upload — multipart/form-data OR a JSON base64 body —
/// into validated content. This is web-layer plumbing (multipart-vs-JSON, size
/// caps, magic-byte sniffing) lifted verbatim out of the controllers so they
/// stay thin. Product and profile uploads keep their own distinct rules, so each
/// gets its own method rather than being forced into one shared shape:
///  • product images are fully validated on both paths and returned as bytes;
///  • profile avatars validate the multipart path and normalise it to a data-URL,
///    but pass a JSON body straight through (UserService does the base64 check).
/// On a validation failure the <see cref="Result"/> carries the exact user-facing
/// message the controllers used to return inline.
/// </summary>
public interface IImageIngestService
{
    Task<Result<IngestedProductImage>> ReadProductImageAsync(HttpRequest request, CancellationToken cancellationToken = default);
    Task<Result<UpdateProfileImageDto>> ReadProfileImageAsync(HttpRequest request);
}
