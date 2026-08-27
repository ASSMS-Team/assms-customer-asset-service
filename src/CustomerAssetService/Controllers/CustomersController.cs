using System.ComponentModel.DataAnnotations;

using CustomerAssetService.DTOs;
using CustomerAssetService.Services;
using Microsoft.AspNetCore.Mvc;

namespace CustomerAssetService.Controllers;

[ApiController]
[Route("api/customers")]
[Produces("application/json")]
public class CustomersController : ControllerBase
{
    private readonly CustomerService _customerService;
    // A customer's assets are addressed under the customer, so the route lives
    // here - but listing them is still the asset service's job, hence the
    // second dependency rather than a copy of that logic on this side.
    private readonly AssetService _assetService;

    public CustomersController(CustomerService customerService, AssetService assetService)
    {
        _customerService = customerService;
        _assetService = assetService;
    }

    // [ApiController] returns 400 with ValidationProblemDetails before this runs,
    // so there is no validation code here - only the duplicate rule to branch on.
    /// <summary>
    /// Registers a new customer. The customer is created with status ACTIVE and a
    /// server-generated id; the phone number is stored exactly as supplied.
    /// </summary>
    /// <param name="request">Name, phone, address and customer type of the customer to create; email is optional.</param>
    /// <response code="201">Customer created. The body is the stored customer and the Location header points at GET /api/customers/{id}.</response>
    /// <response code="400">A field failed validation - missing name, phone, address or customer type, a value over its maximum length, a customer type other than INDIVIDUAL or BUSINESS, or a malformed email. Errors are keyed by field name.</response>
    /// <response code="409">An active customer already exists with this phone number. Keyed on "phone" so it renders against the phone input like a validation error.</response>
    [HttpPost]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCustomerRequest request)
    {
        var result = await _customerService.CreateAsync(request);

        if (result.Error == ServiceError.DuplicatePhone)
        {
            // Keyed on "phone" so the frontend renders it against the phone input
            // exactly like a DataAnnotations failure.
            var problem = new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["phone"] = new[] { "An active customer already exists with this phone number." }
            })
            {
                Status = StatusCodes.Status409Conflict
            };

            return Conflict(problem);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// Updates an existing active customer. The id, status and creation time are
    /// left as they are; only name, phone, address, customer type and email change.
    /// </summary>
    /// <param name="id">The server-generated customer id (a GUID string) of the customer to update.</param>
    /// <param name="request">The new name, phone, address and customer type; email is optional.</param>
    /// <response code="200">Customer updated. The body is the stored customer as it now stands.</response>
    /// <response code="400">A field failed validation - missing name, phone, address or customer type, a value over its maximum length, a customer type other than INDIVIDUAL or BUSINESS, or a malformed email. Errors are keyed by field name.</response>
    /// <response code="404">No customer exists with this id.</response>
    /// <response code="409">Either the customer is not active and so cannot be edited, or another active customer already holds this phone number. The two are told apart by the body: the inactive case is a plain message, the duplicate is keyed on "phone".</response>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateCustomerRequest request)
    {
        var result = await _customerService.UpdateAsync(id, request);

        if (result.Error == ServiceError.NotFound)
        {
            return NotFound();
        }

        if (result.Error == ServiceError.CustomerInactive)
        {
            // A 409 as well, but a different body from the duplicate case so the
            // frontend can tell the two apart without guessing from the status code.
            return Conflict(new ProblemDetails
            {
                Title = "Customer is not active.",
                Detail = "This customer has been deactivated and can no longer be edited.",
                Status = StatusCodes.Status409Conflict
            });
        }

        if (result.Error == ServiceError.DuplicatePhone)
        {
            // Keyed on "phone" so the frontend renders it against the phone input
            // exactly like a DataAnnotations failure.
            var problem = new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["phone"] = new[] { "Another active customer already exists with this phone number." }
            })
            {
                Status = StatusCodes.Status409Conflict
            };

            return Conflict(problem);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Deactivates a customer. The record is kept and only its status changes to
    /// INACTIVE, which also releases its phone number for a new active customer.
    /// Deactivating a customer that is already inactive is not an error: the call
    /// succeeds and returns the customer unchanged, without writing to it, so
    /// repeating it leaves the record and its last-updated time exactly as they were.
    /// </summary>
    /// <param name="id">The server-generated customer id (a GUID string) of the customer to deactivate.</param>
    /// <response code="200">The customer as it now stands, with status INACTIVE. Returned whether this call deactivated it or it was already inactive.</response>
    /// <response code="404">No customer exists with this id.</response>
    [HttpPost("{id}/deactivate")]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(string id)
    {
        var result = await _customerService.DeactivateAsync(id);

        if (result.Error == ServiceError.NotFound)
        {
            return NotFound();
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Returns registered customers, newest first. Without a status the list is
    /// every customer whatever their status; with one it is only the customers
    /// holding that status.
    /// </summary>
    /// <param name="status">Optional status to filter on. Omit it for every customer, or pass ACTIVE or INACTIVE.</param>
    /// <response code="200">The customers. An empty list when none match - that is still a 200, not a 404.</response>
    /// <response code="400">The status is not ACTIVE or INACTIVE. Returned rather than an empty list, because an empty list reads as "no customers" and hides the typo. Keyed on "status".</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CustomerResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(
        // The annotation is what produces the 400: [ApiController] validates
        // action parameters before the body runs, exactly as it does DTO
        // properties, so there is no validation code here either. Null skips
        // every attribute but [Required], so omitting the parameter is valid.
        [FromQuery]
        [RegularExpression("^(ACTIVE|INACTIVE)$", ErrorMessage = "Status must be ACTIVE or INACTIVE.")]
        string? status)
    {
        var customers = await _customerService.GetAllAsync(status);

        return Ok(customers);
    }

    /// <summary>
    /// Returns every asset registered against a customer, newest first. The
    /// customer's own status does not narrow the list: a deactivated customer
    /// can take no new equipment, but the equipment it already has is history
    /// that stays viewable.
    /// </summary>
    /// <param name="customerId">The server-generated customer id (a GUID string) whose assets to list.</param>
    /// <response code="200">The customer's assets. An empty list when the customer has none - that is still a 200, not a 404.</response>
    /// <response code="404">No customer exists with this id. Unlike the create path's 409, the customer here is the addressed resource, so a missing one is a 404.</response>
    [HttpGet("{customerId}/assets")]
    [ProducesResponseType(typeof(IEnumerable<AssetResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAssets(string customerId)
    {
        var result = await _assetService.GetByCustomerIdAsync(customerId);

        if (result.Error == ServiceError.CustomerNotFound)
        {
            return NotFound();
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Returns a single customer by id, whatever its status.
    /// </summary>
    /// <param name="id">The server-generated customer id (a GUID string) returned when the customer was created.</param>
    /// <response code="200">The customer with this id.</response>
    /// <response code="404">No customer exists with this id.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id)
    {
        var customer = await _customerService.GetByIdAsync(id);

        if (customer is null)
        {
            return NotFound();
        }

        return Ok(customer);
    }
}
