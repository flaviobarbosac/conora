using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;

namespace Conora.Repository;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(
            action,
            async (_, op, token) =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(token);
                await op(token);
                await transaction.CommitAsync(token);
                return true;
            },
            verifySucceeded: null,
            cancellationToken: ct);
    }
}
