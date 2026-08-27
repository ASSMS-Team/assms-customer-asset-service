using CustomerAssetService.DTOs;
using CustomerAssetService.Services;
using Microsoft.AspNetCore.Mvc;

namespace CustomerAssetService.Controllers;

[ApiController]
[Route("api/assets")]
[Produces("application/json")]
public class AssetsController : ControllerBase
{
    private readonly AssetService _assetService;

    public AssetsController(AssetService assetService)
    {
        _assetService = assetService;
    }

    // [ApiController] returns 400 with ValidationProblemDetails before this runs,
    // so there is no validation code here - only the business rules to branch on.
    /// <summary>
    /// Registers a new asset against a customer. The asset is created with a
    /// server-generated id; the serial number is stored exactly as supplied.
    /// </summary>
    /// <param name="request">Owning customer id, asset type, model, serial number, installation date and location; notes are optional.</param>
    /// <response code="201">Asset created. The body is the stored asset and the Location header points at GET /api/assets/{id}.</response>
    /// <response code="400">A field failed validation - a missing customer id, asset type, model, serial number, installation date or location, a value over its maximum length, or an asset type other than the five permitted values. Errors are keyed by field name.</response>
    /// <response code="409">Either no customer exists with the supplied customer id, or that customer is not active, or another asset already holds this serial number. The three are told apart by the key in the body: "customerId" for the first two, "serialNumber" for the last.</response>
    [HttpPost]
    [ProducesResponseType(typeof(AssetResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateAssetRequest request)
    {
        var result = await _assetService.CreateAsync(request);

        // Keyed on "customerId" rather than returned as a 404: the addressed
        // resource is the assets collection, which exists. It is the field in
        // the body that is wrong, so it renders against that input.
        if (result.Error == ServiceError.CustomerNotFound)
        {
            return Conflict(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["customerId"] = new[] { "No customer exists with this id." }
            })
            {
                Status = StatusCodes.Status409Conflict
            });
        }

        if (result.Error == ServiceError.CustomerInactive)
        {
            return Conflict(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["customerId"] = new[] { "This customer has been deactivated and cannot have new assets registered." }
            })
            {
                Status = StatusCodes.Status409Conflict
            });
        }

        if (result.Error == ServiceError.DuplicateSerial)
        {
            // Keyed on "serialNumber" so the frontend renders it against the
            // serial number input exactly like a DataAnnotations failure.
            return Conflict(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["serialNumber"] = new[] { "An asset already exists with this serial number." }
            })
            {
                Status = StatusCodes.Status409Conflict
            });
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// Returns a single asset by id.
    /// </summary>
    /// <param name="id">The server-generated asset id (a GUID string) returned when the asset was created.</param>
    /// <response code="200">The asset with this id.</response>
    /// <response code="404">No asset exists with this id.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(AssetResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id)
    {
        var asset = await _assetService.GetByIdAsync(id);

        if (asset is null)
        {
            return NotFound();
        }

        return Ok(asset);
    }
}
