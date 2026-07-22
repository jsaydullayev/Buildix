using System.Text.Json;
using Buildix.API.Validation;
using Buildix.Application.Common;
using Buildix.Application.DTOs;

namespace Buildix.API.Services;

/// <summary>
/// Image-upload parsing lifted verbatim out of ProductsController.SetImage and
/// UsersController.UpdateProfileImage. The two flows deliberately differ (see
/// <see cref="IImageIngestService"/>); the shared bits are the 5MB cap and the
/// magic-byte sniff via <see cref="ImageContentValidator"/>.
/// </summary>
public sealed class ImageIngestService : IImageIngestService
{
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private const string TooLargeMessage = "Rasm hajmi juda katta. Maksimum rasm hajmi 5MB.";
    private const string NotAnImageMessage =
        "Fayl tasvir emas yoki qo'llab-quvvatlanmaydigan formatda (JPEG/PNG/GIF/WebP qabul qilinadi).";

    /// <summary>
    /// Product image — accepts multipart (`image` file) or JSON
    /// ({ "image": "data:image/...;base64,..." }). Both paths are validated for
    /// size and magic bytes; returns the decoded bytes + file extension.
    /// </summary>
    public async Task<Result<IngestedProductImage>> ReadProductImageAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        byte[] imageBytes;

        if (request.HasFormContentType && request.Form.Files.Count > 0)
        {
            var image = request.Form.Files[0];
            if (image.Length == 0)
                return Result.Failure<IngestedProductImage>("Rasm fayli bo'sh.");
            if (image.Length > MaxImageBytes)
                return Result.Failure<IngestedProductImage>(TooLargeMessage);

            using var memoryStream = new MemoryStream();
            await image.CopyToAsync(memoryStream, cancellationToken);
            imageBytes = memoryStream.ToArray();
        }
        else
        {
            SetProductImageRequest? body;
            try
            {
                body = await request.ReadFromJsonAsync<SetProductImageRequest>(cancellationToken);
            }
            catch
            {
                return Result.Failure<IngestedProductImage>("So'rov noto'g'ri formatda.");
            }

            var dataUrl = body?.Image;
            if (string.IsNullOrWhiteSpace(dataUrl))
                return Result.Failure<IngestedProductImage>("Rasm yuborilmadi.");

            // "data:image/png;base64,XXXX" yoki to'g'ridan-to'g'ri base64.
            var base64 = dataUrl.Contains(',') ? dataUrl[(dataUrl.IndexOf(',') + 1)..] : dataUrl;
            try
            {
                imageBytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return Result.Failure<IngestedProductImage>("Rasm ma'lumoti noto'g'ri (base64 emas).");
            }

            if (imageBytes.Length == 0)
                return Result.Failure<IngestedProductImage>("Rasm fayli bo'sh.");
            if (imageBytes.Length > MaxImageBytes)
                return Result.Failure<IngestedProductImage>(TooLargeMessage);
        }

        // Baytlarga ISHONISHDAN OLDIN magic-byte'larni tekshiramiz.
        var kind = ImageContentValidator.Detect(imageBytes);
        if (kind == ImageKind.Unknown)
            return Result.Failure<IngestedProductImage>(NotAnImageMessage);

        var extension = ImageContentValidator.ToExtension(kind);
        return Result.Success(new IngestedProductImage(imageBytes, extension));
    }

    /// <summary>
    /// Profile avatar — a multipart upload is validated (size + magic bytes) and
    /// normalised into a `data:` URL DTO; a JSON body is returned as-is. A
    /// malformed JSON payload lets <c>ReadFromJsonAsync</c> throw so the caller's
    /// outer catch maps it exactly as before (this is why there is no try here).
    /// </summary>
    public async Task<Result<UpdateProfileImageDto>> ReadProfileImageAsync(HttpRequest request)
    {
        // Check if request contains file upload (multipart/form-data)
        if (request.HasFormContentType && request.Form != null && request.Form.Files.Count > 0)
        {
            var image = request.Form.Files[0];

            if (image == null || image.Length == 0)
                return Result.Failure<UpdateProfileImageDto>("Rasm fayli tanlanmadi.");

            if (image.Length > MaxImageBytes)
                return Result.Failure<UpdateProfileImageDto>(TooLargeMessage);

            // Read into memory so we can inspect magic bytes BEFORE trusting the file.
            using var memoryStream = new MemoryStream();
            await image.CopyToAsync(memoryStream);
            var imageBytes = memoryStream.ToArray();

            // Magic-byte sniff — a renamed `payload.exe.png` would pass the extension
            // check but fail here. We trust the file's actual bytes, not its name.
            var kind = ImageContentValidator.Detect(imageBytes);
            if (kind == ImageKind.Unknown)
                return Result.Failure<UpdateProfileImageDto>(NotAnImageMessage);

            var base64Image = Convert.ToBase64String(imageBytes);
            var mimeType = ImageContentValidator.ToMimeType(kind);

            return Result.Success(new UpdateProfileImageDto(
                ProfileImage: $"data:{mimeType};base64,{base64Image}"));
        }

        // Handle JSON body
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var dto = await request.ReadFromJsonAsync<UpdateProfileImageDto>(options);

        if (dto == null)
            return Result.Failure<UpdateProfileImageDto>("So'rov noto'g'ri formatda.");

        return Result.Success(dto);
    }
}
