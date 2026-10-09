using Conora.Domain.Entities;
using Conora.Domain.Ports;
using Conora.Infrastructure.Persistence;
using Conora.Repository.Interface;

namespace Conora.Repository;

public sealed class LgpdRequestRepository : BaseRepository<LgpdRequest>, ILgpdRequestRepository
{
    public LgpdRequestRepository(AppDbContext context, ITenantContext tenant) : base(context, tenant)
    {
    }
}
