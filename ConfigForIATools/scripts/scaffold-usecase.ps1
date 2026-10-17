param (
  [Parameter(Mandatory = $true)]
  [string]$UsecaseName
)

$UsecaseName = $UsecaseName.Trim()
$FolderName  = $UsecaseName + "Usecase"
$BaseDir     = "$PSScriptRoot/../Application/Usecases/$FolderName"

if (Test-Path $BaseDir) {
  Write-Error "Usecase folder already exists at $BaseDir"
  return
}

New-Item -ItemType Directory -Path $BaseDir -Force | Out-Null

# 1. Response Adapter (output DTO)
$AdapterContent = @"
namespace Application.Adapters;

public class ${UsecaseName}Adapter
{
    public string Message { get; set; } = string.Empty;
}
"@
$AdapterPath = "$PSScriptRoot/../Application/Adapters/${UsecaseName}Adapter.cs"
if (!(Test-Path $AdapterPath)) {
  $AdapterContent | Out-File -FilePath $AdapterPath -Encoding utf8
  Write-Host "Created Response Adapter : $AdapterPath"
}

# 2. Request Adapter (input DTO)
$RequestAdapterContent = @"
namespace Application.Adapters;

public class ${UsecaseName}RequestAdapter
{
    // TODO: define input fields
    public string ExampleField { get; set; } = string.Empty;
}
"@
$RequestAdapterPath = "$PSScriptRoot/../Application/Adapters/${UsecaseName}RequestAdapter.cs"
if (!(Test-Path $RequestAdapterPath)) {
  $RequestAdapterContent | Out-File -FilePath $RequestAdapterPath -Encoding utf8
  Write-Host "Created Request Adapter  : $RequestAdapterPath"
}

# 3. Request Adapter Validator
$ValidatorContent = @"
using Application.Adapters;
using Domain.Catalogs;
using FluentValidation;

namespace Application.Usecases.${FolderName};

public class ${UsecaseName}RequestAdapterValidator : AbstractValidator<${UsecaseName}RequestAdapter>
{
    public ${UsecaseName}RequestAdapterValidator()
    {
        // TODO: add validation rules using MessageCatalog codes
        // RuleFor(x => x.ExampleField)
        //     .NotEmpty().WithMessage(MessageCatalog.GetErrorByCode(21002)).WithErrorCode("21002")
        //     .MaximumLength(100).WithMessage(MessageCatalog.GetErrorByCode(21005)).WithErrorCode("21005");
    }
}
"@
$ValidatorContent | Out-File -FilePath "$BaseDir/${UsecaseName}RequestAdapterValidator.cs" -Encoding utf8
Write-Host "Created Validator        : $BaseDir/${UsecaseName}RequestAdapterValidator.cs"

# 4. Port Interface
$PortContent = @"
using Application.Adapters;
using Application.Internals.Adapters;
using Application.Internals.Executors;

namespace Application.Ports;

public interface I${UsecaseName}Port
{
    Task<EasyResult<${UsecaseName}Adapter>> ExecuteAsync(${UsecaseName}RequestAdapter request, TraceIdentifierAdapter header, CancellationToken ct = default);
}
"@
$PortPath = "$PSScriptRoot/../Application/Ports/I${UsecaseName}Port.cs"
$PortContent | Out-File -FilePath $PortPath -Encoding utf8
Write-Host "Created Port Interface   : $PortPath"

# 5. Use Case Class
$ClassContent = @"
using Application.Adapters;
using Application.Internals.Adapters;
using Application.Internals.Executors;
using Application.Ports;
using Microsoft.Extensions.Logging;

namespace Application.Usecases.${FolderName};

public class ${UsecaseName}Case : I${UsecaseName}Port
{
    private readonly ILogger<${UsecaseName}Case> _logger;

    public ${UsecaseName}Case(ILogger<${UsecaseName}Case> logger)
    {
        _logger = logger;
    }

    public Task<EasyResult<${UsecaseName}Adapter>> ExecuteAsync(
        ${UsecaseName}RequestAdapter request,
        TraceIdentifierAdapter header,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "${UsecaseName}Case started. MessageId={MessageId} DeviceId={DeviceId}",
            header.MessageIdentifier,
            header.DeviceIdentifier);

        var headerErrors = FluentValidationExecutor.Execute(header, new TraceIdentifierAdapterValidator());
        if (headerErrors.Any())
            return Task.FromResult(EasyResult<${UsecaseName}Adapter>.Failure(422, headerErrors));

        var requestErrors = FluentValidationExecutor.Execute(request, new ${UsecaseName}RequestAdapterValidator());
        if (requestErrors.Any())
            return Task.FromResult(EasyResult<${UsecaseName}Adapter>.Failure(400, requestErrors));

        // TODO: add business logic (domain mapping, repository calls, etc.)
        var result = new ${UsecaseName}Adapter { Message = "Success" };

        _logger.LogInformation(
            "${UsecaseName}Case completed. MessageId={MessageId}",
            header.MessageIdentifier);

        return Task.FromResult(EasyResult<${UsecaseName}Adapter>.Success(result));
    }
}
"@
$ClassContent | Out-File -FilePath "$BaseDir/${UsecaseName}Case.cs" -Encoding utf8
Write-Host "Created Use Case         : $BaseDir/${UsecaseName}Case.cs"

Write-Host ""
Write-Host "Scaffolding completed!" -ForegroundColor Green
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Register in SystemAPI/ApplicationSetting.cs:" -ForegroundColor Yellow
Write-Host "       services.AddTransient<I${UsecaseName}Port, ${UsecaseName}Case>();" -ForegroundColor Cyan
Write-Host "  2. Define fields in ${UsecaseName}RequestAdapter.cs" -ForegroundColor Yellow
Write-Host "  3. Add FluentValidation rules in ${UsecaseName}RequestAdapterValidator.cs" -ForegroundColor Yellow
Write-Host "  4. Implement business logic in ${UsecaseName}Case.cs" -ForegroundColor Yellow
