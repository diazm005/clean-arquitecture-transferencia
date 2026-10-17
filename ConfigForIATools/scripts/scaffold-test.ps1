param (
  [Parameter(Mandatory = $true)]
  [string]$UsecaseName
)

$UsecaseName = $UsecaseName.Trim()
$FolderName  = $UsecaseName + "Usecase"
$TestDir     = "$PSScriptRoot/../Application.Tests/Usecases/$FolderName"

if (!(Test-Path $TestDir)) {
  New-Item -ItemType Directory -Path $TestDir -Force | Out-Null
}

$TestPath = "$TestDir/${UsecaseName}CaseTests.cs"

if (Test-Path $TestPath) {
  Write-Error "Test file already exists at $TestPath"
  return
}

$Content = @"
using Application.Adapters;
using Application.Internals.Adapters;
using Application.Usecases.${FolderName};
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// Uncomment if mocks are needed:
// using Moq;
// using Domain.Interfaces;

namespace Application.Tests.Usecases.${FolderName};

public class ${UsecaseName}CaseTests : IDisposable
{
    private ${UsecaseName}Case? _sut;
    // private Mock<I${UsecaseName}Query>? _mockQuery;

    #region Setup/Teardown
    public ${UsecaseName}CaseTests()
    {
        // _mockQuery = new Mock<I${UsecaseName}Query>();
        _sut = new ${UsecaseName}Case(NullLogger<${UsecaseName}Case>.Instance);
    }

    public void Dispose()
    {
        _sut = null;
        // _mockQuery = null;
    }
    #endregion

    #region Helper Methods
    private TraceIdentifierAdapter CreateTestHeader(string suffix = "test")
    {
        return new TraceIdentifierAdapter
        {
            DeviceIdentifier  = $"device-{suffix}",
            MessageIdentifier = $"message-{suffix}",
            ChannelIdentifier = $"channel-{suffix}",
        };
    }

    private ${UsecaseName}RequestAdapter CreateTestRequest()
    {
        return new ${UsecaseName}RequestAdapter
        {
            // TODO: populate with valid default values
            ExampleField = "valid-value",
        };
    }
    #endregion

    #region Constructor Tests
    [Fact]
    public void Constructor_ShouldInitialize_Successfully()
    {
        // Assert
        Assert.NotNull(_sut);
    }
    #endregion

    #region Method Tests - ExecuteAsync
    [Fact]
    public async Task ExecuteAsync_ShouldReturnSuccess_WhenRequestIsValid()
    {
        // Arrange
        var header  = CreateTestHeader();
        var request = CreateTestRequest();

        // Act
        var result = await _sut!.ExecuteAsync(request, header);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.SuccessValue);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnFailure_WhenHeaderIsInvalid()
    {
        // Arrange
        var header  = new TraceIdentifierAdapter(); // all fields empty — invalid
        var request = CreateTestRequest();

        // Act
        var result = await _sut!.ExecuteAsync(request, header);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.Status);
        Assert.NotEmpty(result.ValidationValues);
    }
    #endregion
}
"@

$Content | Out-File -FilePath $TestPath -Encoding utf8
Write-Host "Created Test: $TestPath" -ForegroundColor Green
