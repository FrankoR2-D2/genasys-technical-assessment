using Genasys.Api.Entities.Contracts;

namespace Genasys.Api.Entities;

public class InventoryItem : IHasRowVersion
{
    public string ProductId { get; set; } = string.Empty;
    public int TotalQuantity { get; set; }
    public int AvailableQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public Guid RowVersion { get; set; }

    public List<InventoryReservation> Reservations { get; set; } = [];
}
