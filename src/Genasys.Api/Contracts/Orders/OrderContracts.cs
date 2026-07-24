using Genasys.Api.Contracts.Common;
using Genasys.Api.Entities;

namespace Genasys.Api.Contracts.Orders;

public class CreateOrderItemRequest
{
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class CreateOrderRequest
{
    public Guid CustomerId { get; set; }
    public List<CreateOrderItemRequest> Items { get; set; } = [];
    public AddressRequest? ShippingAddress { get; set; }

    // Not part of the spec's Order model, but a payment can't be processed
    // without picking a method — defaults to the simulated gateway.
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.MockGateway;

    // Pass "DECLINE" to deterministically exercise the payment-failure path.
    public string? PaymentInstrumentReference { get; set; }
}

public class UpdateOrderStatusRequest
{
    public OrderStatus Status { get; set; }
    public string? Reason { get; set; }
}

public class OrderListRequest : PagedRequest
{
    public OrderStatus? Status { get; set; }
    public Guid? CustomerId { get; set; }
}

public record OrderItemResponse(
    string ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public record OrderStatusHistoryEntry(
    string? FromStatus,
    string ToStatus,
    string? Reason,
    DateTime ChangedAt);

public record OrderResponse(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    IReadOnlyList<OrderItemResponse> Items,
    decimal TotalAmount,
    string Status,
    AddressResponse? ShippingAddress,
    IReadOnlyList<OrderStatusHistoryEntry> StatusHistory,
    DateTime CreatedAt,
    DateTime UpdatedAt);
