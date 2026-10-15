namespace Conora.Repository.Interface;

public interface IConsoleRepository
{
    Task<ConsoleReadModel> ReadAsync(CancellationToken ct);
}
