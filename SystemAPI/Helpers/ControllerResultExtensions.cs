using Application.Internals.Adapters;
using Application.Internals.Executors;
using Microsoft.AspNetCore.Mvc;

namespace SystemAPI.Helpers;

internal static class ControllerResultExtensions
{
    /// <summary>
    /// Builds the request trace from the channel headers. When MessageIdentifier is missing it uses the ASP.NET identifier so correlation is not lost.
    /// </summary>
    public static TraceIdentifierAdapter ToTrace(this HttpRequest request) => new()
    {
        DeviceIdentifier = request.Headers["DeviceIdentifier"].FirstOrDefault(),
        MessageIdentifier = request.Headers["MessageIdentifier"].FirstOrDefault() ?? request.HttpContext.TraceIdentifier,
        ChannelIdentifier = request.Headers["ChannelIdentifier"].FirstOrDefault()
    };

    /// <summary>
    /// Translates the handler result to a BIAN HTTP response: the body wrapped as "{Name}Response", or the "errors" list, or only the status.
    /// </summary>
    public static IActionResult ToActionResult<T>(this ControllerBase controller, EasyResult<T> result)
    {
        if (result.SuccessValue is not null)
            return controller.StatusCode(result.Status, EasyBianResponseHelper.SuccessResponse(result.SuccessValue));
        if (result.ValidationValues.Count > 0)
            return controller.StatusCode(result.Status, EasyBianResponseHelper.WarningResponse(result.ValidationValues));
        return controller.StatusCode(result.Status);
    }
}
