using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Infrastructure.ResponseHandling;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using InvalidOperationException = CompanyEmployees.Domain.Exceptions.InvalidOperationException;

namespace CompanyEmployees.Infrastructure.ExceptionHandling
{
    public class GlobalExceptionHandler
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandler> _logger;

        public GlobalExceptionHandler(RequestDelegate next, ILogger<GlobalExceptionHandler> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception exception)
            {
                // The JSON envelope is an API contract; for Blazor page requests we
                // rethrow so the framework shows an error page instead of raw JSON.
                if (!context.Request.Path.StartsWithSegments("/api"))
                    throw;

                await HandleExceptionAsync(context, exception);
            }
        }
        private Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            _logger.LogError(exception, "Unknown error.");
            var response = context.Response;
            response.ContentType = "application/json";

            var result = exception switch
            {
                EntityNotFoundException => EnvelopeExtensions.Failure<object>(exception.Message, StatusCodes.Status404NotFound),
                UnauthorizedException => EnvelopeExtensions.Failure<object>(exception.Message, StatusCodes.Status401Unauthorized),
                InvalidOperationException => EnvelopeExtensions.Failure<object>(exception.Message, StatusCodes.Status400BadRequest),

                _ => EnvelopeExtensions.Failure<object>("Unknown error.", StatusCodes.Status500InternalServerError)
            };
            response.StatusCode = result.StatusCode;

            return response.WriteAsJsonAsync(result);
        }
    }
}
