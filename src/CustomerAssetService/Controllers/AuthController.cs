using System.IdentityModel.Tokens.Jwt;

using CustomerAssetService.DTOs;
using CustomerAssetService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerAssetService.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthenticationService _authenticationService;

    public AuthController(AuthenticationService authenticationService) => _authenticationService = authenticationService;

    /// <summary>Authenticates an active internal staff account and returns a time-limited bearer token.</summary>
    /// <response code="200">Credentials accepted. Returns a JWT and the authenticated staff identity.</response>
    /// <response code="401">Credentials are invalid or the account is inactive. The response is deliberately generic.</response>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authenticationService.LoginAsync(request);
        return result is null
            ? Unauthorized(new ProblemDetails { Title = "Authentication failed.", Detail = "Invalid username/email or password.", Status = 401 })
            : Ok(result);
    }

    /// <summary>Returns the identity and role carried by the current valid JWT.</summary>
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new
    {
        id = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        username = User.Identity?.Name,
        role = User.FindFirst("role")?.Value
    });
}
