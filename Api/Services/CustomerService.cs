using AMS.Api.Repositories;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface ICustomerService
{
    Task<List<CustomerItem>> ListAsync(string? search);
    Task<Customer> CreateAsync(NewCustomerRequest req, Actor actor);
    /** Add Entry dialog (§4.1): returns the customer matching the name
        (case-insensitive, trimmed) or creates one with the typed address. */
    Task<Customer> FindOrCreateAsync(string name, string address);
    Task<Customer> UpdateAsync(int id, UpdateCustomerRequest req, Actor actor);
}

/** Customers: list with derived counts, create/update, find-or-create.
    Outstanding balances are computed, never stored (plan §3.2, §4.1, §5). */
public sealed class CustomerService(ICustomerRepository customers, IActivityRepository activity) : ICustomerService
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

    public async Task<Customer> CreateAsync(NewCustomerRequest req, Actor actor)
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

        // The audit row: who added this borrower to the directory.
        await activity.AddAsync(actor, "Created", "Customer", customer.Id, groupId: null,
            $"Created customer {customer.Name}" +
            (string.IsNullOrWhiteSpace(customer.Address) ? "" : $" ({customer.Address})"));
        await activity.SaveAsync();
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

    public async Task<Customer> UpdateAsync(int id, UpdateCustomerRequest req, Actor actor)
    {
        var customer = await customers.FindByIdAsync(id)
            ?? throw ApiException.NotFound("Customer not found");
        if (string.IsNullOrWhiteSpace(req.Name)) throw ApiException.BadRequest("Name is required");

        // The audit diff — old values before anything mutates.
        var oldName = customer.Name;
        var oldPhone = customer.Phone;
        var oldAddress = customer.Address;

        customer.Name = req.Name.Trim();
        customer.Phone = (req.Phone ?? "").Trim();
        customer.Address = (req.Address ?? "").Trim();
        await customers.SaveAsync();

        var changes = new List<string>();
        if (customer.Name != oldName) changes.Add($"name {oldName} → {customer.Name}");
        if (customer.Phone != oldPhone) changes.Add("phone updated");
        if (customer.Address != oldAddress) changes.Add("address updated");

        // The audit row: who edited this customer's details.
        await activity.AddAsync(actor, "Updated", "Customer", id, groupId: null,
            $"Updated customer — {string.Join("; ", changes)}");
        await activity.SaveAsync();
        return customer;
    }
}