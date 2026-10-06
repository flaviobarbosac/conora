namespace Conora.Services.Contracts;

public sealed record CreateUserRequest(string Name, string Email);

public sealed record UserResponse(Guid Id, string Name, string Email, DateTime CreatedAtUtc);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Skip, int Take, int TotalCount);
