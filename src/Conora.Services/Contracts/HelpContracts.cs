namespace Conora.Services.Contracts;

public sealed record HelpTermResponse(string Key, string Term, string Definition);

public sealed record HelpStepResponse(int Order, string Key, string Title, string Text);

public sealed record HelpFieldResponse(string Name, string Description);

public sealed record HelpModuleResponse(
    string Key,
    string Title,
    string Summary,
    IReadOnlyList<HelpFieldResponse> Fields);

public sealed record HelpResponse(
    IReadOnlyList<HelpStepResponse> Steps,
    IReadOnlyList<HelpTermResponse> Glossary,
    IReadOnlyList<HelpModuleResponse> Modules);
