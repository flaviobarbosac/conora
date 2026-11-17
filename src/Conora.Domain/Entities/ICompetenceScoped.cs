namespace Conora.Domain.Entities;

/// <summary>Row that belongs to one competence month. A consultant share locks these to that month.</summary>
public interface ICompetenceScoped
{
    string CompetenceYm { get; }
}
