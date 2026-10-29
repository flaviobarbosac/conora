using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Conora.Api.Exceptions;

public sealed class DomainExceptionHandler : IExceptionHandler
{
    private readonly ILogger<DomainExceptionHandler> _logger;
    private readonly IServiceScopeFactory _scopes;

    public DomainExceptionHandler(ILogger<DomainExceptionHandler> logger, IServiceScopeFactory scopes)
    {
        _logger = logger;
        _scopes = scopes;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
            return false;

        var (statusCode, title) = Map(exception);
        var recordedStatus = statusCode ?? StatusCodes.Status500InternalServerError;
        var recordedTitle = statusCode is null ? "Erro interno" : title;
        await RecordAsync(httpContext, recordedStatus, recordedTitle);

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

    private async Task RecordAsync(HttpContext httpContext, int statusCode, string title)
    {
        if (httpContext.Request.Path.StartsWithSegments("/health"))
            return;
        if (httpContext.Items.ContainsKey("conora-failure-recorded"))
            return;

        httpContext.Items["conora-failure-recorded"] = true;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RequestFailures.Add(RequestFailure.Record(statusCode, title, Area(httpContext.Request.Path)));
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao registrar erro operacional.");
        }
    }

    private static string Area(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value))
            return "raiz";

        var head = value.Trim('/');
        var slash = head.IndexOf('/');
        if (slash >= 0)
            head = head[..slash];
        if (head.Length == 0)
            return "raiz";
        return head.Length <= 40 ? head.ToLowerInvariant() : head[..40].ToLowerInvariant();
    }

    private static (int? StatusCode, string Title) Map(Exception exception) => exception switch
    {
        ValidationException => (StatusCodes.Status400BadRequest, "Requisição inválida"),
        NotFoundException => (StatusCodes.Status404NotFound, "Recurso não encontrado"),
        MonthClosedException => (StatusCodes.Status409Conflict, "Mês fechado"),
        SystemCategoryProtectedException => (StatusCodes.Status409Conflict, "Conta padrão protegida"),
        PlanReadOnlyException => (StatusCodes.Status402PaymentRequired, "Modo somente leitura"),
        PlanBlockedException => (StatusCodes.Status402PaymentRequired, "Conta bloqueada"),
        ForbiddenException => (StatusCodes.Status403Forbidden, "Acesso negado"),
        UserNotFoundException => (StatusCodes.Status404NotFound, "Usuário não encontrado"),
        DuplicateEmailException => (StatusCodes.Status409Conflict, "Email duplicado"),
        DuplicateCpfException => (StatusCodes.Status409Conflict, "CPF duplicado"),
        SocialAccountExistsException => (StatusCodes.Status409Conflict, "Conta já existe"),
        AccountNotFoundException => (StatusCodes.Status404NotFound, "Conta não encontrada"),
        InvalidCredentialsException => (StatusCodes.Status401Unauthorized, "Não autorizado"),
        InvalidTokenException => (StatusCodes.Status401Unauthorized, "Token inválido"),
        GoogleAuthDisabledException => (StatusCodes.Status503ServiceUnavailable, "Google indisponível"),
        AppleAuthDisabledException => (StatusCodes.Status503ServiceUnavailable, "Apple indisponível"),
        DomainException => (StatusCodes.Status400BadRequest, "Erro de negócio"),
        _ => (null, "Erro interno")
    };
}
