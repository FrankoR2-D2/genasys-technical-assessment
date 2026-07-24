using Genasys.Api.Common;
using Genasys.Api.Contracts.Common;
using Genasys.Api.Contracts.Customers;
using Genasys.Api.Data;
using Genasys.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Genasys.Api.Services;

public class CustomerService(AppDbContext db, ILogger<CustomerService> logger) : ICustomerService
{
    public async Task<PagedResult<CustomerResponse>> ListAsync(CustomerListRequest request, CancellationToken cancellationToken)
    {
        var query = db.Customers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(c => c.Name.Contains(term) || c.Email.Contains(term));
        }

        var sort = SortSpec.Parse(request.Sort, "name");
        query = sort.Field.ToLowerInvariant() switch
        {
            "email" => sort.Descending ? query.OrderByDescending(c => c.Email) : query.OrderBy(c => c.Email),
            "createdat" => sort.Descending ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
            _ => sort.Descending ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var customers = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<CustomerResponse>.Create(customers.Select(ToResponse).ToList(), request.Page, request.PageSize, totalCount);
    }

    public async Task<CustomerResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Customer '{id}' was not found.");
        return ToResponse(customer);
    }

    public async Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        if (await db.Customers.IgnoreQueryFilters().AnyAsync(c => c.Email == request.Email, cancellationToken))
        {
            throw new ConflictException($"A customer with email '{request.Email}' already exists.");
        }

        var now = DateTime.UtcNow;
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Email = request.Email,
            ShippingAddress = AddressMapper.ToEntity(request.ShippingAddress),
            BillingAddress = AddressMapper.ToEntity(request.BillingAddress),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created customer {CustomerId}", customer.Id);

        return ToResponse(customer);
    }

    public async Task<CustomerResponse> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Customer '{id}' was not found.");

        if (!string.Equals(customer.Email, request.Email, StringComparison.OrdinalIgnoreCase)
            && await db.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id != id && c.Email == request.Email, cancellationToken))
        {
            throw new ConflictException($"A customer with email '{request.Email}' already exists.");
        }

        customer.Name = request.Name;
        customer.Email = request.Email;
        customer.ShippingAddress = AddressMapper.ToEntity(request.ShippingAddress);
        customer.BillingAddress = AddressMapper.ToEntity(request.BillingAddress);
        customer.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(customer);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Customer '{id}' was not found.");

        customer.IsDeleted = true;
        customer.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Soft-deleted customer {CustomerId}", id);
    }

    private static CustomerResponse ToResponse(Customer customer) => new(
        customer.Id,
        customer.Name,
        customer.Email,
        AddressMapper.ToResponse(customer.ShippingAddress),
        AddressMapper.ToResponse(customer.BillingAddress),
        customer.CreatedAt,
        customer.UpdatedAt);
}
