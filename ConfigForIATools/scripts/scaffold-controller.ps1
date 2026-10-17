param (
  [Parameter(Mandatory = $true)]
  [string]$UsecaseName,

  [Parameter(Mandatory = $false)]
  [string]$Route = "v1/api/[controller]"
)

$UsecaseName    = $UsecaseName.Trim()
$ControllerName = $UsecaseName + "Controller"
$PortInterface  = "I" + $UsecaseName + "Port"
$BaseDir        = "$PSScriptRoot/../SystemAPI/Controllers/$UsecaseName"

if (!(Test-Path $BaseDir)) {
  New-Item -ItemType Directory -Path $BaseDir -Force | Out-Null
}

$ControllerPath = "$BaseDir/$ControllerName.cs"

if (Test-Path $ControllerPath) {
  Write-Error "Controller already exists at $ControllerPath"
  return
}

$Content = @"
using Application.Adapters;
using Application.Internals.Adapters;
using Application.Ports;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using SystemAPI.Helpers;

namespace SystemAPI.Controllers.${UsecaseName};

[Route("${Route}")]
[ApiController]
public class ${ControllerName} : ControllerBase
{
    private readonly ${PortInterface} _usecase;
    private readonly ILogger<${ControllerName}> _logger;

    public ${ControllerName}(ILogger<${ControllerName}> logger, ${PortInterface} usecase)
    {
        _logger  = logger;
        _usecase = usecase;
    }

    [HttpPost("execute")]
    public async Task<IActionResult> Execute(
        [FromBody] ${UsecaseName}RequestAdapter request,
        [FromHeader(Name = "x-device-identifier")] string? deviceIdentifier,
        [FromHeader(Name = "x-message-identifier")] string? messageIdentifier,
        [FromHeader(Name = "x-channel-identifier")] string? channelIdentifier,
        CancellationToken ct
    )
    {
        var headers = new TraceIdentifierAdapter
        {
            ChannelIdentifier = channelIdentifier,
            DeviceIdentifier  = deviceIdentifier,
            MessageIdentifier = messageIdentifier,
        };

        try
        {
            var result = await _usecase.ExecuteAsync(request, headers, ct);

            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "TraceId=[{Headers}] Validation=[{ValidationErrors}]",
                    LoggerMapperHelper.ToString(headers),
                    LoggerMapperHelper.ToString(result.ValidationValues.FirstOrDefault()!));

                return StatusCode(result.Status, EasyResponseHelper.WarningResponse(result.ValidationValues));
            }

            return Ok(EasyResponseHelper.SuccessResponse(result));
        }
        catch (Exception ex)
        {
            var tracer = Regex.Replace(ex.StackTrace ?? string.Empty, @"\sat\s(.*?)\sin\s", string.Empty).Trim();
            _logger.LogError(
                "Message=[{Message}] TraceId=[{Headers}] StackTrace={Trace}",
                ex.Message,
                LoggerMapperHelper.ToString(headers),
                tracer);

            return StatusCode(500, EasyResponseHelper.ErrorResponse("11033"));
        }
    }
}
"@

$Content | Out-File -FilePath $ControllerPath -Encoding utf8
Write-Host "Created Controller: $ControllerPath" -ForegroundColor Green
