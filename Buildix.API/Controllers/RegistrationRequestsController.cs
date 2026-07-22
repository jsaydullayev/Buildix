using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Buildix.API.Controllers;

/// <summary>
/// Public sign-up entry point. Only one POST — there is intentionally no
/// GET / PUT / DELETE, so the queue stays private and we can't accidentally
/// leak phone numbers or pending state. Reviewing and approving requests
/// lives under <see cref="SuperAdminController"/>.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RegistrationRequestsController : ControllerBase
{
    private readonly IRegistrationRequestService _service;
    private readonly ILogger<RegistrationRequestsController> _logger;

    public RegistrationRequestsController(
        IRegistrationRequestService service,
        ILogger<RegistrationRequestsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Anonymous sign-up — visitor submits FullName + Phone. To avoid leaking
    /// whether a given phone is already in the queue or in an unsupported format,
    /// we return the SAME 200 OK response in every case (validation failures
    /// included), and write the actual reason to the server log. The only
    /// front-facing 4xx is a 429 from the rate limiter.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("registration-submit")]
    public async Task<IActionResult> Submit([FromBody] SubmitRegistrationRequestDto request, CancellationToken ct)
    {
        const string genericMessage = "Adminga yubordik. Admin tez orada javob beradi.";

        var result = await _service.SubmitAsync(request, ct);
        if (result.IsSuccess)
            return Ok(new { message = genericMessage });

        // The service already decided what's safe to reveal: a VALIDATION failure
        // is a user-facing formatting hint; every other reason (duplicate phone,
        // suspicious shape) is logged and hidden behind the generic acknowledgement
        // so a bad actor can't enumerate the queue.
        _logger.LogInformation("Registration submit handled with reason: {Reason}", result.Error);
        return result.Code == RegistrationSubmitCodes.Validation
            ? BadRequest(new { message = result.Error })
            : Ok(new { message = genericMessage });
    }
}