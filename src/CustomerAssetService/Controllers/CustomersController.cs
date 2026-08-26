using CustomerAssetService.DTOs;
using CustomerAssetService.Services;
using Microsoft.AspNetCore.Mvc;

namespace CustomerAssetService.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly CustomerService _customerService;

    public CustomersController(CustomerService customerService)
    {
        _customerService = customerService;
    }

    // [ApiController] returns 400 with ValidationProblemDetails before this runs,
    // so there is no validation code here - only the duplicate rule to branch on.
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
