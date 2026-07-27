using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.API.Authorization;
using Buildix.API.Services;
using Buildix.Domain.Constants;
using Buildix.Domain.Interfaces;
using System.Security.Claims;
using System.Text.Json;
using Serilog;

namespace Buildix.API.Controllers;
//new code
[ApiController]
[Route("api/[controller]/[action]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IImageIngestService _imageIngest;
    private readonly IConfiguration _config;

    public UsersController(
        IUserService userService,
        IImageIngestService imageIngest,
        IConfiguration config)
    {
        _userService = userService;
        _imageIngest = imageIngest;
        _config = config;
    }

    /// <summary>The authenticated caller's user id, taken from the JWT — recorded
    /// as the actor on audit entries. Guid.Empty if the claim is somehow absent.</summary>
    private Guid CurrentUserId() =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    [HttpGet("{id}")]
    [RequirePermission(PermissionKeys.UsersAccess)]
    public async Task<ActionResult<UserDto>> GetUser(Guid id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        if (user is null)
            return NotFound();

        return Ok(user);
    }

    [HttpGet]
    public async Task<ActionResult<UserDto>> MyProfile()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var user = await _userService.GetUserByIdAsync(userId);
        if (user is null)
            return NotFound();

        return Ok(user);
    }

    /// <summary>
    /// Platforma botining @username'i — Akkaunt sahifasidagi «Botni ochish»
    /// tugmasi uchun. Konfiguratsiyadan keladi (Telegram:BotUsername), shuning
    /// uchun bot almashtirilsa frontend'ni qayta yig'ish shart emas. Bo'sh
    /// bo'lsa frontend tugmani ko'rsatmaydi.
    /// </summary>
    [HttpGet]
    public ActionResult<object> TelegramBotInfo()
    {
        var username = _config["Telegram:BotUsername"]?.TrimStart('@');
        return Ok(new { botUsername = string.IsNullOrWhiteSpace(username) ? null : username });
    }

    [HttpGet]
    [RequirePermission(PermissionKeys.UsersAccess)]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetAllUsers(
        [FromQuery] string? search = null, [FromQuery] string? role = null)
    {
        // GetAllUsersAsync already scopes to the caller's market (throws for a
        // market-less SuperAdmin), so the controller no longer re-filters.
        return Ok(await _userService.GetAllUsersAsync(search, role));
    }

    [HttpPost]
    [RequirePermission(PermissionKeys.UsersManage)]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserDto request)
    {
        try
        {
            var user = await _userService.CreateUserAsync(request, CurrentUserId());
            return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id}")]
    [RequirePermission(PermissionKeys.UsersManage)]
    public async Task<ActionResult<UserDto>> UpdateUser(Guid id, [FromBody] UpdateUserDto request)
    {
        if (id != request.Id)
            return BadRequest("ID mismatch");

        try
        {
            var user = await _userService.UpdateUserAsync(request, CurrentUserId());
            if (user is null)
                return NotFound();

            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut]
    public async Task<ActionResult<UserDto>> UpdateMyProfile([FromBody] UpdateProfileDto request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        try
        {
            var user = await _userService.UpdateProfileAsync(userId, request);
            if (user is null)
                return NotFound();

            return Ok(user);
        }
        catch (UnauthorizedAccessException ex)
        {
            // H-12: wrong CURRENT password is a business validation error, not an
            // auth failure — return 400 so the client's 401→token-refresh
            // interceptor doesn't fire (which would waste a refresh or log the
            // user out just for mistyping their old password).
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut]
    public async Task<ActionResult<UserDto>> UpdateProfileImage()
    {
        Log.Information("=== UPDATE PROFILE IMAGE START ===");
        Log.Information("Request Content-Type: {ContentType}", Request.ContentType);
        Log.Information("HasFormContentType: {HasFormContentType}", Request.HasFormContentType);
        // `Request.Form` throws InvalidOperationException on a non-multipart
        // request (e.g. when the client sends a JSON base64 body). Gate the
        // log on HasFormContentType so the endpoint doesn't 500 on its own
        // diagnostic line before any business logic runs.
        if (Request.HasFormContentType)
        {
            Log.Information("Files count: {FilesCount}", Request.Form.Files.Count);
        }

        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Log.Information("User ID from token: {UserId}", userIdStr);

        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        try
        {
            // Multipart faylni yoki JSON-base64 tanani parse qilish endi
            // IImageIngestService ichida: multipart magic-byte bilan tekshirilib
            // data-URL'ga aylantiriladi; JSON tana xom o'tadi (base64 validatsiyasi
            // UserService'da). Buzuq JSON'da ReadFromJsonAsync throw qiladi va
            // quyidagi catch uni aynan ilgarigidek ushlaydi — shu bois try ichida.
            var ingest = await _imageIngest.ReadProfileImageAsync(Request);
            if (ingest.IsFailure)
                return BadRequest(ingest.Error);

            var request = ingest.Value;

            var user = await _userService.UpdateProfileImageAsync(userId, request);
            if (user is null)
                return NotFound();

            return Ok(user);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error(ex, "Unauthorized access while updating profile image for user: {UserId}", userId);
            return Unauthorized(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            Log.Error(ex, "Invalid operation while updating profile image for user: {UserId}", userId);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating profile image for user: {UserId}", userId);
            return BadRequest($"Rasmni yangilashda xatolik: {ex.Message}");
        }
    }

    [HttpDelete("{id}")]
    [RequirePermission(PermissionKeys.UsersManage)]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var result = await _userService.DeleteUserAsync(id, CurrentUserId());
        if (!result)
            return NotFound();

        return NoContent();
    }

    [HttpPost("{id}/deactivate")]
    [RequirePermission(PermissionKeys.UsersManage)]
    public async Task<IActionResult> DeactivateUser(Guid id)
    {
        var result = await _userService.DeactivateUserAsync(id, CurrentUserId());
        if (!result)
            return NotFound();

        return Ok(new { message = "User deactivated" });
    }

    [HttpPost("{id}/activate")]
    [RequirePermission(PermissionKeys.UsersManage)]
    public async Task<IActionResult> ActivateUser(Guid id)
    {
        var result = await _userService.ActivateUserAsync(id, CurrentUserId());
        if (!result)
            return NotFound();

        return Ok(new { message = "User activated" });
    }

    /// <summary>
    /// Set a seller's work shift — "Active", "Blocked" or "Scheduled" (with a
    /// [startUtc, endUtc] window). Admin/Owner only.
    /// </summary>
    [HttpPut("{id}/shift")]
    [RequirePermission(PermissionKeys.UsersShift)]
    public async Task<ActionResult<UserDto>> UpdateShift(Guid id, [FromBody] UpdateShiftDto request)
    {
        try
        {
            var user = await _userService.UpdateShiftAsync(id, request, CurrentUserId());
            if (user is null)
                return NotFound();

            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Owner RBAC — read a user's permission configuration (effective set,
    /// role defaults and the full catalogue). Owner-only, scoped to the
    /// caller's own market.
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<ActionResult<UserPermissionsDto>> GetUserPermissions(Guid id)
    {
        var permissions = await _userService.GetUserPermissionsAsync(id);
        if (permissions is null)
            return NotFound();

        return Ok(permissions);
    }

    /// <summary>
    /// Owner RBAC — overwrite a user's explicit permission set. Send an empty
    /// list to reset the user to its role default. Owner/SuperAdmin cannot be
    /// edited. The change takes effect on the user's next login/token refresh.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<ActionResult<UserPermissionsDto>> UpdateUserPermissions(Guid id, [FromBody] UpdatePermissionsDto request)
    {
        try
        {
            var permissions = await _userService.UpdateUserPermissionsAsync(id, request, CurrentUserId());
            if (permissions is null)
                return NotFound();

            return Ok(permissions);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
