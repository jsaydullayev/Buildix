using System.Security.Claims;
using Buildix.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Buildix.API.Controllers;

/// <summary>
/// Base controller that maps an application-layer <see cref="Result"/> /
/// <see cref="Result{T}"/> to an HTTP response, reproducing the previous
/// per-action try/catch contract EXACTLY:
///   success             -> Ok(value)                          [200]
///   Code == "NOT_FOUND" -> NotFound()                         [404, no body]
///   other failure       -> BadRequest(new { message = ... })  [400]
///
/// A base method (not an extension) is used deliberately: Ok()/NotFound()/
/// BadRequest() are protected members of <see cref="ControllerBase"/>, so
/// reusing them here guarantees byte-identical status codes and serialization.
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    protected const string NotFoundCode = "NOT_FOUND";

    /// <summary>
    /// Runtime check that the current caller effectively holds
    /// <paramref name="permissionKey"/>. Owner/SuperAdmin bypass (their JWT
    /// deliberately carries NO "perm" claims — see JwtService); Admin/Seller must
    /// carry a matching "perm" claim. This mirrors PermissionAuthorizationHandler
    /// exactly, so a programmatic check inside an action agrees with the
    /// [RequirePermission] attribute gate — and, unlike a hardcoded role literal,
    /// honours an Owner who has revoked the permission from a specific Admin.
    /// </summary>
    protected bool HasPermission(string permissionKey)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (role is "Owner" or "SuperAdmin")
            return true;
        return User.HasClaim("perm", permissionKey);
    }

    /// <summary>Maps a Result&lt;T&gt; whose success renders as Ok(value).</summary>
    protected ActionResult<T> ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);
        if (result.Code == NotFoundCode)
            return NotFound(new { message = result.Error });
        return BadRequest(new { message = result.Error });
    }

    /// <summary>
    /// Maps a Result&lt;T&gt; whose success is rendered by <paramref name="onSuccess"/>
    /// — e.g. CreateSale which must stay CreatedAtAction (201 + Location). The
    /// failure mapping is identical to the plain overload.
    /// </summary>
    protected ActionResult<T> ToActionResult<T>(Result<T> result, Func<T, ActionResult> onSuccess)
    {
        if (result.IsSuccess)
            return onSuccess(result.Value);
        if (result.Code == NotFoundCode)
            return NotFound(new { message = result.Error });
        return BadRequest(new { message = result.Error });
    }
}
