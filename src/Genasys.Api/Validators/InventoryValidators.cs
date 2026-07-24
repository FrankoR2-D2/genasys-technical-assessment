using FluentValidation;
using Genasys.Api.Contracts.Inventory;

namespace Genasys.Api.Validators;

public class ReserveInventoryRequestValidator : AbstractValidator<ReserveInventoryRequest>
{
    public ReserveInventoryRequestValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public class ReleaseInventoryRequestValidator : AbstractValidator<ReleaseInventoryRequest>
{
    public ReleaseInventoryRequestValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}
