using MedApp.Api.Infrastructure;
using MedApp.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MedApp.Api.Tests;

/// <summary>HIPAA rule 2: failures return a generic error and log only the exception type, never its message.</summary>
public sealed class SafeExceptionHandlerTests
{
    [Fact]
    public async Task UnexpectedError_Returns500_WithoutLeakingExceptionText()
    {
        var (status, body, logs) = await Handle(new InvalidOperationException("Avery Quillfeather born 1971-03-14"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Contains("traceId", body);
        Assert.DoesNotContain("Quillfeather", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Quillfeather", logs.AllText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1971-03-14", logs.AllText);
        Assert.Contains(logs.Lines, l => l.Contains("InvalidOperationException"));
    }

    [Fact]
    public async Task UpstreamUnavailable_Returns503()
    {
        var (status, _, _) = await Handle(new HttpRequestException("connection refused"));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
    }

    private static async Task<(int Status, string Body, CapturingLoggerProvider Logs)> Handle(Exception exception)
    {
        var logs = new CapturingLoggerProvider();
        using var services = new ServiceCollection()
            .AddLogging(l => l.SetMinimumLevel(LogLevel.Trace).AddProvider(logs))
            .AddProblemDetails()
            .BuildServiceProvider();
        var handler = new SafeExceptionHandler(
            services.GetRequiredService<IProblemDetailsService>(), services.GetRequiredService<ILogger<SafeExceptionHandler>>());
        using var body = new MemoryStream();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Response.Body = body;

        Assert.True(await handler.TryHandleAsync(http, exception, CancellationToken.None));
        return (http.Response.StatusCode, System.Text.Encoding.UTF8.GetString(body.ToArray()), logs);
    }
}
