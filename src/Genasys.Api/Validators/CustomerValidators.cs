using FluentValidation;
using Genasys.Api.Contracts.Customers;

namespace Genasys.Api.Validators;

public class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.ShippingAddress!)
            .SetValidator(new AddressRequestValidator())
            .When(x => x.ShippingAddress is not null);
        RuleFor(x => x.BillingAddress!)
            .SetValidator(new AddressRequestValidator())
            .When(x => x.BillingAddress is not null);
    }
}

public class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.ShippingAddress!)
            .SetValidator(new AddressRequestValidator())
            .When(x => x.ShippingAddress is not null);
        RuleFor(x => x.BillingAddress!)
            .SetValidator(new AddressRequestValidator())
            .When(x => x.BillingAddress is not null);
    }
}
