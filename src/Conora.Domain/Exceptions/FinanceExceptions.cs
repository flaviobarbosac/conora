namespace Conora.Domain.Exceptions;

public sealed class NotFoundException : DomainException
{
    public NotFoundException(string entity, Guid id) : base($"{entity} '{id}' não encontrado(a).")
    {
    }

    public NotFoundException(string message) : base(message)
    {
    }
}

public sealed class SystemCategoryProtectedException : DomainException
{
    public SystemCategoryProtectedException() : base("Conta padrão do sistema não pode ser alterada ou excluída.")
    {
    }
}

public sealed class MonthClosedException : DomainException
{
    public string CompetenceYm { get; }

    public MonthClosedException(string competenceYm)
        : base($"O mês {competenceYm} está fechado e não aceita alterações. Reabra o mês para editar.")
    {
        CompetenceYm = competenceYm;
    }
}

public sealed class PlanReadOnlyException : DomainException
{
    public PlanReadOnlyException()
        : base("Assinatura vencida: o Conora está em modo somente leitura. Renove o plano para lançar ou editar.")
    {
    }
}

public sealed class ForbiddenException : DomainException
{
    public ForbiddenException(string message) : base(message)
    {
    }
}
