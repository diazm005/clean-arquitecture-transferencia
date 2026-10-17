using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;

namespace SystemAPI.Controllers.Healthcheck;

[Route("/")]
[ApiController]
public class HealthcheckController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;

    public HealthcheckController(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    //[Authorize(Policy = "ReadScope")] // Validar mediante una declaración del token JWT
    //[ArixAuthorize("WriteSetProf")] //Validar el permiso mediante la cabecera axscope
    [HttpGet]
    public IActionResult GetInfo()
    {
        return Ok(new { info = "Template for System Layer API", status = "Service Running ..." });
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        var healthData = new
        {
            status = "Healthy",
            timestamp = DateTime.UtcNow,
            uptime = Process.GetCurrentProcess().StartTime,
            version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"
        };

        return Ok(healthData);
    }

    //[Authorize(Policy = "ReadScope")] // Validar mediante una declaración del token JWT
    [HttpGet("health/detailed")]
    public async Task<IActionResult> HealthDetailed()
    {
        var healthReport = await _healthCheckService.CheckHealthAsync();

        var healthData = new
        {
            status = healthReport.Status.ToString(),
            timestamp = DateTime.UtcNow,
            uptime = Process.GetCurrentProcess().StartTime,
            version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development",
            checks = healthReport.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                duration = entry.Value.Duration.TotalMilliseconds
            }).ToList()
        };

        return Ok(healthData);
    }

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { message = "pong", timestamp = DateTime.UtcNow });
    }

    //[Authorize] // Solo requiere autenticación, no scope específico
    [HttpGet("debug-claims")]
    public IActionResult DebugClaims()
    {
        var claims = User.Claims.Select(c => new {
            Type = c.Type,
            Value = c.Value
        }).ToList();

        var scopeClaim = User.FindFirst("scope")?.Value;
        var scopes = scopeClaim?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? new string[0];

        return Ok(new
        {
            message = "JWT Claims Debug",
            isAuthenticated = User.Identity?.IsAuthenticated ?? false,
            claims = claims,
            scopeClaim = scopeClaim,
            scopesArray = scopes,
            hasReadScope = scopes.Contains("api/r:correspd"),
            requiredScope = "api/r:correspd"
        });
    }

    [HttpGet("health/ready")]
    public async Task<IActionResult> HealthReady()
    {
        var healthReport = await _healthCheckService.CheckHealthAsync(reg => reg.Tags.Contains("ready"));

        if (healthReport.Status == HealthStatus.Healthy)
        {
            return Ok(new { status = "Ready", timestamp = DateTime.UtcNow });
        }

        return StatusCode(503, new { status = "Not Ready", timestamp = DateTime.UtcNow });
    }

    [HttpGet("health/live")]
    public async Task<IActionResult> HealthLive()
    {
        var healthReport = await _healthCheckService.CheckHealthAsync(reg => reg.Tags.Contains("live"));

        if (healthReport.Status == HealthStatus.Healthy)
        {
            return Ok(new { status = "Alive", timestamp = DateTime.UtcNow });
        }

        return StatusCode(503, new { status = "Not Alive", timestamp = DateTime.UtcNow });
    }

    [HttpGet("healthcheck")]
    public IActionResult HealthCheck()
    {
        return Health();
    }
}
