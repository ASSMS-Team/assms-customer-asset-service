using System.IdentityModel.Tokens.Jwt;

using CustomerAssetService.DTOs;
using CustomerAssetService.Models;
using CustomerAssetService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerAssetService.Controllers;

/// <summary>Manager-controlled provisioning of staff accounts for all four supported roles.</summary>
[ApiController, Route("api/staff-accounts"), Produces("application/json")]
[Authorize(Roles = "Manager")]
public sealed class StaffAccountsController : ControllerBase
{
    private readonly StaffAccountService _staffAccountService;

    public StaffAccountsController(StaffAccountService staffAccountService) =>
        _staffAccountService = staffAccountService;

    private static StaffAccountResponse ToResponse(StaffAccount account) =>
        new(account.Id, account.FullName, account.Username, account.Email,
            account.Role, account.Status, account.TechnicianId,
            account.CreatedAt, account.CreatedBy,
            account.DispatchSyncStatus);

    /// <summary>Returns registered staff accounts with optional status filter. Passwords and hashes are never returned.</summary>
    /// <response code="200">A list of registered staff accounts.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<StaffAccountResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] string? status = null)
    {
        var accounts = await _staffAccountService.GetAllAsync(status);
        return Ok(accounts.Select(ToResponse));
    }

    /// <summary>Returns a single registered staff account by ID. Passwords and hashes are never returned.</summary>
    /// <response code="200">The registered staff account details.</response>
    /// <response code="404">The staff account was not found.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(StaffAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id)
    {
        var account = await _staffAccountService.GetByIdAsync(id);
        return account is null
            ? NotFound(new ProblemDetails { Status = 404, Title = "Staff account not found." })
            : Ok(ToResponse(account));
    }

    /// <summary>Creates a new staff account with the specified role. Only Managers may call this endpoint.</summary>
    /// <response code="201">The staff account was created successfully.</response>
    /// <response code="400">Request validation failed.</response>
    /// <response code="409">The username, email or technician link is already taken.</response>
    /// <response code="503">The Dispatch service could not be reached to verify the technician.</response>
    [HttpPost]
    [ProducesResponseType(typeof(StaffAccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create([FromBody] CreateStaffAccountRequest request, CancellationToken cancellationToken)
    {
        var managerId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? string.Empty;
        var authorization = Request.Headers.Authorization.ToString();

        var result = await _staffAccountService.CreateAsync(request, managerId, authorization, cancellationToken);
        if (result.IsSuccess)
            return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, ToResponse(result.Value!));

        return result.Error switch
        {
            ServiceError.DuplicateIdentifier => Conflict(new ProblemDetails
            {
                Status = 409,
                Title = "Duplicate identifier.",
                Detail = "The username, email or technician link is already in use."
            }),
            ServiceError.TechnicianRequired => BadRequest(new ProblemDetails
            {
                Status = 400,
                Title = "Technician link required.",
                Detail = "A Dispatch technician UUID is required when creating a Technician account."
            }),
            ServiceError.TechnicianNotFound => BadRequest(new ProblemDetails
            {
                Status = 400,
                Title = "Technician not found.",
                Detail = "The specified Dispatch technician does not exist."
            }),
            ServiceError.DispatchUnavailable => Problem(
                statusCode: 503,
                title: "Dispatch service unavailable.",
                detail: "Could not verify the technician with the Dispatch service. Try again later."),
            ServiceError.ValidationFailed => BadRequest(new ProblemDetails
            {
                Status = 400,
                Title = "Validation failed.",
                Detail = "Check that the role is Agent, Dispatcher, Technician or Manager."
            }),
            _ => Problem(statusCode: 500, title: "An unexpected error occurred.")
        };
    }

    /// <summary>Deactivates a staff account. Only Managers may call this endpoint. Self-deactivation and deactivating the last active Manager are rejected.</summary>
    /// <response code="200">The staff account was successfully deactivated.</response>
    /// <response code="400">Self-deactivation is not permitted.</response>
    /// <response code="404">The staff account was not found.</response>
    /// <response code="409">The last active Manager account cannot be deactivated.</response>
    [HttpPost("{id}/deactivate")]
    [ProducesResponseType(typeof(StaffAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(string id, CancellationToken cancellationToken = default)
    {
        var managerId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? string.Empty;
        var authorization = Request.Headers.Authorization.ToString();
        var result = await _staffAccountService.DeactivateAsync(id, managerId, authorization, cancellationToken);
        if (result.IsSuccess)
        {
            var response = ToResponse(result.Value!);
            if (response.DispatchSyncStatus == "PENDING_RETRY")
            {
                Response.Headers["X-Dispatch-Sync-Status"] = "PENDING_RETRY";
            }
            return Ok(response);
        }

        return result.Error switch
        {
            ServiceError.NotFound => NotFound(new ProblemDetails
            {
                Status = 404,
                Title = "Staff account not found.",
                Detail = "The specified staff account does not exist."
            }),
            ServiceError.SelfDeactivationNotAllowed => BadRequest(new ProblemDetails
            {
                Status = 400,
                Title = "Self-deactivation is not permitted.",
                Detail = "You cannot deactivate your own currently authenticated Manager account."
            }),
            ServiceError.LastActiveManager => Conflict(new ProblemDetails
            {
                Status = 409,
                Title = "Last active Manager safeguard.",
                Detail = "Cannot deactivate the last active Manager account. At least one active Manager must remain."
            }),
            ServiceError.TechnicianHasOpenAssignments => Conflict(new ProblemDetails
            {
                Status = 409,
                Title = "Technician has open assignments.",
                Detail = "Reassign or close the technician's open jobs before deactivation."
            }),
            _ => Problem(statusCode: 500, title: "An unexpected error occurred.")
        };
    }
}
