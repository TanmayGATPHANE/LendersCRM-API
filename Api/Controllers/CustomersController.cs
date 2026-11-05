using AMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Api.Controllers;

/// Customers: list with derived counts, create, update, find-or-create
/// (plan §3.2, §4.1, §6).
[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customers;

    public CustomersController(ICustomerService customers) => _customers = customers;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search) =>
        Ok(await _customers.ListAsync(search));

    [HttpPost]
    [Authorize(Policy = "Writer")]
    public async Task<IActionResult> Create(NewCustomerRequest req)
    {
        var customer = await _customers.CreateAsync(req, CurrentUser.Actor(User));
        return Created($"/api/customers/{customer.Id}", customer);
    }

    /// Add Entry dialog: an unknown name is created on the spot (§4.1).
    [HttpPost("find-or-create")]
    [Authorize(Policy = "Writer")]
    public async Task<IActionResult> FindOrCreate(FindOrCreateCustomerRequest req) =>
        Ok(await _customers.FindOrCreateAsync(req.Name, req.Address));

    [HttpPut("{id}")]
    [Authorize(Policy = "Writer")]
    public async Task<IActionResult> Update(int id, UpdateCustomerRequest req)
    {
        var customer = await _customers.UpdateAsync(id, req, CurrentUser.Actor(User));
        return Ok(customer);
    }
}