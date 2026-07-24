using Genasys.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Genasys.Api.Data.Seed;

// Dummy data for the InMemory database — reseeded on every run since nothing persists.
public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Users.AnyAsync())
        {
            return;
        }

        var electronics = new ProductCategory { Id = Guid.NewGuid(), Name = "Electronics" };
        var homeGoods = new ProductCategory { Id = Guid.NewGuid(), Name = "Home Goods" };
        db.ProductCategories.AddRange(electronics, homeGoods);

        var now = DateTime.UtcNow;
        var products = new List<Product>
        {
            new()
            {
                ProductId = "SKU-001", Name = "Wireless Headphones", Sku = "SKU-001",
                Description = "Over-ear Bluetooth headphones", UnitPrice = 89.99m,
                CategoryId = electronics.Id, CreatedAt = now, UpdatedAt = now
            },
            new()
            {
                ProductId = "SKU-002", Name = "Mechanical Keyboard", Sku = "SKU-002",
                Description = "Hot-swappable 75% keyboard", UnitPrice = 129.50m,
                CategoryId = electronics.Id, CreatedAt = now, UpdatedAt = now
            },
            new()
            {
                ProductId = "SKU-003", Name = "USB-C Hub", Sku = "SKU-003",
                Description = "7-in-1 USB-C hub", UnitPrice = 34.99m,
                CategoryId = electronics.Id, CreatedAt = now, UpdatedAt = now
            },
            new()
            {
                ProductId = "SKU-004", Name = "Ceramic Mug Set", Sku = "SKU-004",
                Description = "Set of 4 ceramic mugs", UnitPrice = 24.00m,
                CategoryId = homeGoods.Id, CreatedAt = now, UpdatedAt = now
            },
            new()
            {
                ProductId = "SKU-005", Name = "Desk Lamp", Sku = "SKU-005",
                Description = "Adjustable LED desk lamp", UnitPrice = 42.75m,
                CategoryId = homeGoods.Id, CreatedAt = now, UpdatedAt = now
            }
        };
        db.Products.AddRange(products);

        var stockLevels = new Dictionary<string, int>
        {
            ["SKU-001"] = 50,
            ["SKU-002"] = 30,
            ["SKU-003"] = 100,
            ["SKU-004"] = 5,
            ["SKU-005"] = 0
        };
        foreach (var product in products)
        {
            var total = stockLevels[product.ProductId];
            db.InventoryItems.Add(new InventoryItem
            {
                ProductId = product.ProductId,
                TotalQuantity = total,
                AvailableQuantity = total,
                ReservedQuantity = 0
            });
        }

        var customers = new List<Customer>
        {
            new()
            {
                Id = Guid.NewGuid(), Name = "Ada Lovelace", Email = "ada@example.com",
                ShippingAddress = new Address { Line1 = "12 Analytical Engine Way", City = "London", State = "", PostalCode = "SW1A 1AA", Country = "GB" },
                CreatedAt = now, UpdatedAt = now
            },
            new()
            {
                Id = Guid.NewGuid(), Name = "Grace Hopper", Email = "grace@example.com",
                ShippingAddress = new Address { Line1 = "1 Compiler Ave", City = "Arlington", State = "VA", PostalCode = "22201", Country = "US" },
                CreatedAt = now, UpdatedAt = now
            },
            new()
            {
                Id = Guid.NewGuid(), Name = "Alan Turing", Email = "alan@example.com",
                ShippingAddress = new Address { Line1 = "Bletchley Park", City = "Milton Keynes", State = "", PostalCode = "MK3 6EB", Country = "GB" },
                CreatedAt = now, UpdatedAt = now
            }
        };
        db.Customers.AddRange(customers);

        db.Users.AddRange(
            new User
            {
                Id = Guid.NewGuid(), Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                Role = UserRole.Admin, CreatedAt = now
            },
            new User
            {
                // Read-only principal — demonstrates [Authorize(Roles = "Admin")] actually rejecting a non-admin caller.
                Id = Guid.NewGuid(), Username = "viewer",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Viewer123!"),
                Role = UserRole.Customer, CreatedAt = now
            }
        );

        await db.SaveChangesAsync();
    }
}
