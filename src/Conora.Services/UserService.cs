using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class UserService
{
    private readonly IUserRepository _users;
    private readonly IAuditEventRepository _audits;
    private readonly IUnitOfWork _uow;
    private readonly ICorrelationContext _correlation;
    private readonly IDomainMetrics _metrics;

    public UserService(
        IUserRepository users,
        IAuditEventRepository audits,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        IDomainMetrics metrics)
    {
        _users = users;
        _audits = audits;
        _uow = uow;
        _correlation = correlation;
        _metrics = metrics;
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
            errors["name"] = ["Nome é obrigatório."];
        if (string.IsNullOrWhiteSpace(request.Email))
            errors["email"] = ["Email é obrigatório."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var user = User.Create(request.Name, request.Email);
        _users.Add(user);

        AuditRecorder.Record(_audits, _correlation, "User", user.Id, "UserCreated", new
        {
            user.Name,
            user.Email
        });

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName.Contains("Email", StringComparison.OrdinalIgnoreCase))
        {
            throw new DuplicateEmailException(request.Email);
        }

        _metrics.RecordUserCreated();
        return ToResponse(user);
    }

    public async Task<UserResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null)
            throw new UserNotFoundException(id);

        return ToResponse(user);
    }

    private static UserResponse ToResponse(User user) =>
        new(user.Id, user.Name, user.Email, user.CreatedAtUtc);
}
