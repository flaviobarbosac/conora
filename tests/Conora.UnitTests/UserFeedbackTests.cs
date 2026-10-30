using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Domain.Ports;
using Conora.Repository.Interface;
using Conora.Services;
using Conora.Services.Contracts;
using System.Linq.Expressions;
using Xunit;

namespace Conora.UnitTests;

public class UserFeedbackTests
{
    [Fact]
    public void Reply_sets_status_to_respondido()
    {
        var feedback = UserFeedback.Create(Guid.NewGuid(), "Ana", "Orçamento", "/orcamento", "Android", 2, "Travou no salvamento");
        var adminId = Guid.NewGuid();

        feedback.Reply(adminId, "Flavio", "Já estamos olhando isso.", DateTime.UtcNow);

        Assert.Equal("Android", feedback.Plataforma);
        Assert.Equal(FeedbackStatus.Respondido, feedback.Status);
        Assert.Equal("Já estamos olhando isso.", feedback.Resposta);
        Assert.Equal(adminId, feedback.RespondidoPorUsuarioId);
        Assert.NotNull(feedback.RespondidoEm);
        Assert.NotNull(feedback.LidoEm);
    }

    [Fact]
    public async Task CreateAsync_rejects_invalid_nota()
    {
        var svc = CreateService();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateAsync(new CreateFeedbackRequest("Orçamento", "/orcamento", "Web", 0, "Comentário válido"), default));

        Assert.True(ex.Errors.ContainsKey("nota"));
    }

    [Fact]
    public async Task CreateAsync_rejects_empty_comentario()
    {
        var svc = CreateService();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateAsync(new CreateFeedbackRequest("Orçamento", "/orcamento", "iOS", 4, "  "), default));

        Assert.True(ex.Errors.ContainsKey("comentario"));
    }

    [Fact]
    public async Task ReplyAdminAsync_sets_respondido()
    {
        var usuarioId = Guid.NewGuid();
        var feedback = UserFeedback.Create(usuarioId, "Ana", "Orçamento", "/orcamento", "Web", 2, "Travou");
        var repo = new FakeFeedbackRepo(feedback);
        var svc = new FeedbackService(repo, new FakeUserRepo(usuarioId), new FakeUow(), new FakeTenant(usuarioId));
        var adminId = Guid.NewGuid();

        var result = await svc.ReplyAdminAsync(
            feedback.Id,
            adminId,
            "Flavio",
            new ReplyFeedbackRequest("Obrigado, vamos corrigir."),
            default);

        Assert.NotNull(result);
        Assert.Equal(FeedbackStatus.Respondido, result!.Status);
        Assert.Equal("Obrigado, vamos corrigir.", result.Resposta);
    }

    private static FeedbackService CreateService()
    {
        var usuarioId = Guid.NewGuid();
        return new FeedbackService(
            new FakeFeedbackRepo(),
            new FakeUserRepo(usuarioId),
            new FakeUow(),
            new FakeTenant(usuarioId));
    }

    private sealed class FakeTenant(Guid id) : ITenantContext
    {
        public Guid? UsuarioId => id;
        public bool IsAuthenticated => true;
        public void Set(Guid usuarioId) { }
    }

    private sealed class FakeUow : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
            => action(ct);
    }

    private sealed class FakeUserRepo(Guid id) : IUserRepository
    {
        private readonly User _user = User.Create("Ana", "ana@example.com");

        public Task<User?> GetByIdAsync(Guid _, CancellationToken ct = default)
            => Task.FromResult<User?>(_user);

        public Task AddAsync(User entity, CancellationToken ct = default) => Task.CompletedTask;
        public void Add(User entity) { }
        public Task SoftDeleteAsync(Guid entityId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<User?> FindAsync(Expression<Func<User, bool>> predicate, CancellationToken ct = default)
            => Task.FromResult<User?>(null);
        public Task<IReadOnlyList<User>> FindAllAsync(Expression<Func<User, bool>> predicate, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
        public Task<bool> ExistsAsync(Guid entityId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User?> FindByCpfAsync(string cpf, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User?> FindByGoogleIdAsync(string googleId, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User?> FindByAppleIdAsync(string appleId, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User?> FindByPasswordResetHashAsync(string tokenHash, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User?> FindByEmailVerificationHashAsync(string tokenHash, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<IReadOnlyList<Guid>> ListActiveUserIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>(new[] { id });
        public Task<IReadOnlyList<User>> ListExpiredDeletionsAsync(DateTime cutoffUtc, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
        public void Remove(User user) { }
    }

    private sealed class FakeFeedbackRepo(UserFeedback? seed = null) : IUserFeedbackRepository
    {
        private readonly List<UserFeedback> _items = seed is null ? [] : [seed];

        public Task AddAsync(UserFeedback entity, CancellationToken ct = default)
        {
            _items.Add(entity);
            return Task.CompletedTask;
        }

        public void Add(UserFeedback entity) => _items.Add(entity);
        public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<UserFeedback?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(f => f.Id == id));
        public Task<UserFeedback?> FindAsync(Expression<Func<UserFeedback, bool>> predicate, CancellationToken ct = default)
            => Task.FromResult(_items.AsQueryable().FirstOrDefault(predicate));
        public Task<IReadOnlyList<UserFeedback>> FindAllAsync(Expression<Func<UserFeedback, bool>> predicate, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UserFeedback>>(_items.AsQueryable().Where(predicate).ToList());
        public Task<IReadOnlyList<UserFeedback>> ListMineAsync(Guid usuarioId, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UserFeedback>>(_items.Where(f => f.UsuarioId == usuarioId).Take(take).ToList());
        public Task<UserFeedback?> GetMineAsync(Guid id, Guid usuarioId, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(f => f.Id == id && f.UsuarioId == usuarioId));
        public Task<UserFeedback?> GetMineWithReplyAsync(Guid id, Guid usuarioId, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(f => f.Id == id && f.UsuarioId == usuarioId && f.Resposta != null));
        public Task<IReadOnlyList<UserFeedback>> ListAdminAsync(
            Guid? usuarioId, FeedbackStatus? status, DateTime? from, DateTime? to,
            string? modulo, int? nota, int? notaMin, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UserFeedback>>(_items.Take(take).ToList());
        public Task<IReadOnlyList<(Guid UsuarioId, string UsuarioNome)>> ListAdminUsuariosAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(Guid, string)>>(_items.Select(f => (f.UsuarioId, f.UsuarioNome)).Distinct().ToList());
        public Task<UserFeedback?> GetAdminAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(f => f.Id == id));

        public Task<UserFeedback?> MarkReadAdminAsync(
            Guid id, Guid adminId, string adminNome, DateTime when, CancellationToken ct = default)
        {
            var entity = _items.FirstOrDefault(f => f.Id == id);
            entity?.MarkReadByAdmin(adminId, adminNome, when);
            return Task.FromResult(entity);
        }

        public Task<UserFeedback?> ReplyAdminAsync(
            Guid id, Guid adminId, string adminNome, string resposta, DateTime when, CancellationToken ct = default)
        {
            var entity = _items.FirstOrDefault(f => f.Id == id);
            entity?.Reply(adminId, adminNome, resposta, when);
            return Task.FromResult(entity);
        }
    }
}
