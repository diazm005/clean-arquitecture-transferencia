using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace SystemAPI.Models.Internals;

public class HeaderRequestModel
{
    [FromHeader]
    public string? DeviceIdentifier { set; get; }

    [FromHeader]
    public string? MessageIdentifier { set; get; }

    [FromHeader]
    public string? ChannelIdentifier { set; get; }
}

//public class HeaderRequestAdapterValidator : AbstractValidator<HeaderRequestModel>
//{
//    public HeaderRequestAdapterValidator()
//    {
//        RuleFor(x => x.userIdentity)
//            .NotEmpty().WithMessage(MessageException.GetErrorByCode(21002)).WithErrorCode("21002")
//            .MinimumLength(5).WithMessage(MessageException.GetErrorByCode(21004)).WithErrorCode("21004")
//            .MaximumLength(42).WithMessage(MessageException.GetErrorByCode(21005)).WithErrorCode("21005");
//    }
//}
