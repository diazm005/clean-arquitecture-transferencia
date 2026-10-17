using FluentValidation;
namespace Application.Adapters;

/// <summary>
/// Validates the shape of the request (required fields, format, length and amount). Business rules that need external data stay in the handler.
/// </summary>
public sealed class InitiatePaymentExecutionRequestValidator : AbstractValidator<InitiatePaymentExecutionRequest>
{
    public InitiatePaymentExecutionRequestValidator()
    {
        RuleFor(x => x.TransactionReference).NotEmpty().WithMessage("Indica un ID de operación.");
        RuleFor(x => x.CustomerNationalId).NotEmpty().Matches(@"^\d{8}$").WithMessage("El DNI debe tener 8 dígitos.");
        RuleFor(x => x.SourceAccountNumber).NotEmpty().MaximumLength(30)
            .WithMessage("Indica la cuenta de origen (máximo 30 caracteres).");
        RuleFor(x => x.DestinationAccountNumber).NotEmpty().MaximumLength(30)
            .WithMessage("Indica la cuenta de destino (máximo 30 caracteres).");
        RuleFor(x => x.DestinationBank).NotEmpty().MaximumLength(20)
            .WithMessage("Indica el banco de destino (máximo 20 caracteres).");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("El monto debe ser positivo.")
            .Must(m => decimal.Round(m, 2) == m).WithMessage("El monto admite como máximo dos decimales.");
    }
}
