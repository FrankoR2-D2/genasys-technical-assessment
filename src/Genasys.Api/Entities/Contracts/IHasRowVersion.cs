namespace Genasys.Api.Entities.Contracts;

// EF Core InMemory has no server-generated rowversion column, so AppDbContext
// bumps this manually on every save for any entity that implements it.
public interface IHasRowVersion
{
    Guid RowVersion { get; set; }
}
