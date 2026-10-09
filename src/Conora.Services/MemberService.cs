using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Repository.Interface;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed class MemberService
{
    private readonly IFinanceRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly PlanService _plan;

    public MemberService(IFinanceRepository repo, IUnitOfWork uow, PlanService plan)
    {
        _repo = repo;
        _uow = uow;
        _plan = plan;
    }

    public async Task<IReadOnlyList<MemberResponse>> ListAsync(CancellationToken ct)
    {
        var items = await _repo.ListAsync<FamilyMember>(null, ct);
        return items.OrderBy(m => m.Name).Select(ToResponse).ToList();
    }

    public async Task<MemberResponse> CreateAsync(MemberRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var member = FamilyMember.Create(request.Name);
        _repo.Add(member);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(member);
    }

    public async Task<MemberResponse> UpdateAsync(Guid id, MemberRequest request, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var member = await _repo.GetAsync<FamilyMember>(id, ct) ?? throw new NotFoundException("Membro", id);
        member.Update(request.Name, request.IsActive);
        await _uow.SaveChangesAsync(ct);
        return ToResponse(member);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _plan.EnsureWritableAsync(ct);
        var member = await _repo.GetAsync<FamilyMember>(id, ct) ?? throw new NotFoundException("Membro", id);
        _repo.SoftDelete(member);
        await _uow.SaveChangesAsync(ct);
    }

    private static MemberResponse ToResponse(FamilyMember m) => new(m.Id, m.Name, m.IsActive);
}
