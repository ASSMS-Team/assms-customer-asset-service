using CustomerAssetService.DTOs;
using CustomerAssetService.Models;
using CustomerAssetService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerAssetService.Controllers;

/// <summary>Restricted provisioning of Technician logins; never assigns other staff roles.</summary>
[ApiController, Route("api/auth/technician-accounts"), Produces("application/json")]
[Authorize(Roles = "Dispatcher,Manager")]
public sealed class TechnicianAccountsController(TechnicianAccountService accounts, IDispatchTechnicianVerifier verifier) : ControllerBase
{
    private static TechnicianAccountResponse ToResponse(StaffAccount account) =>
        new(account.Id, account.Username, account.Email, account.TechnicianId!);

    [HttpGet("{technicianId:guid}")]
    public async Task<IActionResult> Get(Guid technicianId)
    {
        var account = await accounts.GetAsync(technicianId.ToString());
        return account is null ? NotFound() : Ok(ToResponse(account));
    }

    [HttpPost("{technicianId:guid}")]
    public async Task<IActionResult> Create(Guid technicianId, CreateTechnicianAccountRequest request, CancellationToken cancellationToken)
    {
        var status = await verifier.VerifyAsync(technicianId.ToString(), Request.Headers.Authorization.ToString(), cancellationToken);
        if (status != 200) return Problem(statusCode: status, title: status == 404 ? "Technician not found." : "Unable to verify technician with Dispatch. Try again later.");
        var account = await accounts.CreateAsync(technicianId.ToString(), request);
        return account is null ? Conflict(new ProblemDetails { Status = 409, Title = "Username, email or technician is already linked. Use existing-login setup if appropriate." })
            : CreatedAtAction(nameof(Get), new { technicianId }, ToResponse(account));
    }

    [HttpPut("{technicianId:guid}/link")]
    public async Task<IActionResult> Link(Guid technicianId, LinkTechnicianAccountRequest request, CancellationToken cancellationToken)
    {
        var status = await verifier.VerifyAsync(technicianId.ToString(), Request.Headers.Authorization.ToString(), cancellationToken);
        if (status != 200) return Problem(statusCode: status, title: status == 404 ? "Technician not found." : "Unable to verify technician with Dispatch. Try again later.");
        var account = await accounts.LinkAsync(technicianId.ToString(), request.Identifier);
        return account is null ? Conflict(new ProblemDetails { Status = 409, Title = "An active, unlinked Technician login is required. Existing identity links cannot be reassigned." })
            : Ok(ToResponse(account));
    }
}
