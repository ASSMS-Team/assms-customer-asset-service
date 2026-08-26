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

    public CustomersController(CustomerService customerService)
    {
        _customerService = customerService;
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
    /// Returns every registered customer, newest first.
    /// </summary>
    /// <response code="200">The customers. An empty list when none are registered - that is still a 200, not a 404.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CustomerResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var customers = await _customerService.GetAllAsync();

        return Ok(customers);
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
