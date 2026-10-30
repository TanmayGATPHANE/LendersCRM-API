using AMS.Api.Repositories;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface ICustomerService
{
    Task<List<CustomerItem>> ListAsync(string? search);
    Task<Customer> CreateAsync(NewCustomerRequest req);
    /** Add Entry dialog (§4.1): returns the customer matching the name
        (case-insensitive, trimmed) or creates one with the typed address. */
    Task<Customer> FindOrCreateAsync(string name, string address);
    Task<Customer> UpdateAsync(int id, UpdateCustomerRequest req);
}

/** Customers: list with derived counts, create/update, find-or-create.
    Outstanding balances are computed, never stored (plan §3.2, §4.1, §5). */
public sealed class CustomerService(ICustomerRepository customers) : ICustomerService
{
    public async Task<List<CustomerItem>> ListAsync(string? search)
    {
        var list = await customers.ListAsync(search);
        var stats = await customers.ActiveStatsAsync();
        var items = list.Select(c => new CustomerItem(
            Id: c.Id,
            Name: c.Name,
            Phone: c.Phone,
            Address: c.Address,
            ActiveCount: stats.GetValueOrDefault(c.Id).ActiveCount,
            Outstanding: stats.GetValueOrDefault(c.Id).Outstanding
        )).ToList();
        return items;
    }

    public async Task<Customer> CreateAsync(NewCustomerRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) throw ApiException.BadRequest("Name is required");
        var customer = new Customer
        {
            Name = req.Name.Trim(),
            Phone = (req.Phone ?? "").Trim(),
            Address = (req.Address ?? "").Trim(),
        };
        await customers.AddAsync(customer);
        await customers.SaveAsync();
        return customer;
    }

    public async Task<Customer> FindOrCreateAsync(string name, string address)
    {
        var found = await customers.FindByNameAsync(name);
        if (found is not null) return found;

        var customer = new Customer
        {
            Name = name.Trim(),
            Phone = "",
            Address = (address ?? "").Trim(),
        };
        await customers.AddAsync(customer);
        await customers.SaveAsync();
        return customer;
    }

    public async Task<Customer> UpdateAsync(int id, UpdateCustomerRequest req)
    {
        var customer = await customers.FindByIdAsync(id)
            ?? throw ApiException.NotFound("Customer not found");
        if (string.IsNullOrWhiteSpace(req.Name)) throw ApiException.BadRequest("Name is required");

        customer.Name = req.Name.Trim();
        customer.Phone = (req.Phone ?? "").Trim();
        customer.Address = (req.Address ?? "").Trim();
        await customers.SaveAsync();
        return customer;
    }
}