namespace Genasys.Api.Entities;

public class InventoryReservation
{
    public Guid Id { get; set; }
    public string ProductId { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public int Quantity { get; set; }
    public ReservationStatus Status { get; set; }
    public DateTime ReservedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
}
