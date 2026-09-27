using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TeamGateway.Api.Constants;
using TeamGateway.Api.Options;

namespace TeamGateway.Api.Controllers;

[Route("api/health-check")]
[ApiController]
[EnableRateLimiting(RateLimiterPolicies.Default)]
public class HealthCheckController : ControllerBase
{
    private readonly IDatabase _redis;
    private readonly IOptions<AuthOptions> _authOptions;
    private readonly IWebHostEnvironment _environment;

    public HealthCheckController(
        IConnectionMultiplexer multiplexer,
        IOptions<AuthOptions> authOptions,
        IWebHostEnvironment environment)
    {
        _redis = multiplexer.GetDatabase();
        _authOptions = authOptions;
        _environment = environment;
    }

    [HttpGet]
    public IActionResult Healthy()
    {
        return Ok("Healthy");
    }

    [HttpGet("dependencies")]
    public async Task<IActionResult> CheckDependenciesAsync(CancellationToken cancellationToken)
    {
        var redisHealthy = await CanRoundTripRedisAsync();
        var privatePemHealthy = await CanReadPrivatePemAsync(cancellationToken);

        var result = new
        {
            redis = redisHealthy,
            privatePem = privatePemHealthy
        };

        return redisHealthy && privatePemHealthy
            ? Ok(result)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }

    private async Task<bool> CanRoundTripRedisAsync()
    {
        var key = (RedisKey)$"gateway:health-check:{Guid.NewGuid():N}";
        var value = Guid.NewGuid().ToString("N");
        var removed = false;

        try
        {
            var set = await _redis.StringSetAsync(key, value, TimeSpan.FromSeconds(30));
            var retrievedValue = await _redis.StringGetAsync(key);
            removed = await _redis.KeyDeleteAsync(key);

            return set && retrievedValue == value && removed;
        }
        catch (RedisException)
        {
            return false;
        }
        finally
        {
            if (!removed)
            {
                try
                {
                    await _redis.KeyDeleteAsync(key);
                }
                catch (RedisException)
                {
                    // The Redis check has already failed; cleanup is best effort.
                }
            }
        }
    }

    private async Task<bool> CanReadPrivatePemAsync(CancellationToken cancellationToken)
    {
        try
        {
            var configuredPath = _authOptions.Value.InternalJwt.PrivateKeyPemPath;
            var privatePemPath = Path.GetFullPath(configuredPath, _environment.ContentRootPath);
            var privatePem = await System.IO.File.ReadAllTextAsync(privatePemPath, cancellationToken);

            return !string.IsNullOrWhiteSpace(privatePem);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
