namespace Genasys.Api.Entities;

public class Order : IHasRowVersion
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }

    // Snapshotted at order time — a soft-deleted Customer (filtered out of
    // queries) must never make a historical order's customer name disappear.
    public string CustomerName { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public Address? ShippingAddress { get; set; }
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; }
    public Guid RowVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<OrderItem> Items { get; set; } = [];
    public List<OrderStatusHistory> StatusHistory { get; set; } = [];
}
