using Conora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Conora.Infrastructure.Persistence;

public static class DbContextChangeTrackerExtensions
{
    public static void DetachLocal<T>(this DbContext context, Guid id) where T : ModelBase
    {
        var local = context.Set<T>().Local.FirstOrDefault(e => e.Id == id);
        if (local is not null)
            context.Entry(local).State = EntityState.Detached;
    }
}
