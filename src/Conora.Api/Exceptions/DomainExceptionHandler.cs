using Conora.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Conora.Api.Exceptions;

public sealed class DomainExceptionHandler : IExceptionHandler
{
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(ILogger<DomainExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = Map(exception);
        if (statusCode is null)
            return false;

        _logger.LogWarning(exception, "Requisição rejeitada com {StatusCode}: {Title}", statusCode, title);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = httpContext.Request.Path
        };

        if (exception is ValidationException validationException)
            problemDetails.Extensions["errors"] = validationException.Errors;

        httpContext.Response.StatusCode = statusCode.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    private static (int? StatusCode, string Title) Map(Exception exception) => exception switch
    {
        ValidationException => (StatusCodes.Status400BadRequest, "Requisição inválida"),
        UserNotFoundException => (StatusCodes.Status404NotFound, "Usuário não encontrado"),
        DuplicateEmailException => (StatusCodes.Status409Conflict, "Email duplicado"),
        DomainException => (StatusCodes.Status400BadRequest, "Erro de negócio"),
        _ => (null, "Erro interno")
    };
}
