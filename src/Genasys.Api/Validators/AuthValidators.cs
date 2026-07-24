using FluentValidation;
using Genasys.Api.Contracts.Auth;

namespace Genasys.Api.Validators;

public class TokenRequestValidator : AbstractValidator<TokenRequest>
{
    public TokenRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}
