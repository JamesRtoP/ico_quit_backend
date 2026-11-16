using System.Diagnostics;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Diagnostics;

namespace MedApp.Api.Infrastructure;

/// <summary>
/// Turns every unhandled exception into a generic ProblemDetails response (HIPAA rule 2).
/// Logs the exception TYPE, route and trace id only — never the message or inner data, since FHIR
/// errors (OperationOutcome text) and serializer errors can echo resource content.
/// </summary>
internal sealed partial class SafeExceptionHandler(IProblemDetailsService problemDetails, ILogger<SafeExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            BadHttpRequestException bad => (bad.StatusCode, "Invalid request."),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => (499, "Request cancelled."),
            HttpRequestException or TaskCanceledException => (StatusCodes.Status503ServiceUnavailable, "A dependency is unavailable. Try again later."),
            FhirOperationException => (StatusCodes.Status502BadGateway, "A dependency returned an error."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
        };

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        // The middleware clears the request's endpoint before invoking handlers; the feature keeps it.
        var endpoint = httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint ?? httpContext.GetEndpoint();
        var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? "(no route)";
        var fhirStatus = (exception as FhirOperationException)?.Status;
        LogHandled(logger, exception.GetType().FullName ?? exception.GetType().Name, httpContext.Request.Method, route,
            status, fhirStatus is null ? null : (int)fhirStatus, traceId);

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = null, // never let the exception reach the response writer
            ProblemDetails = { Status = status, Title = title, Extensions = { ["traceId"] = traceId } },
        });
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Request failed with {ExceptionType} on {Method} {Route}; responded {StatusCode} (upstream FHIR status {FhirStatus}); traceId {TraceId}")]
    private static partial void LogHandled(ILogger logger, string exceptionType, string method, string route, int statusCode, int? fhirStatus, string traceId);
}
