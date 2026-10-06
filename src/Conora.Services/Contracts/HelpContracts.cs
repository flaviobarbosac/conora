namespace Conora.Services.Contracts;

public sealed record HelpTermResponse(string Key, string Term, string Definition);

public sealed record HelpStepResponse(int Order, string Key, string Title, string Text);

public sealed record HelpResponse(IReadOnlyList<HelpStepResponse> Steps, IReadOnlyList<HelpTermResponse> Glossary);
