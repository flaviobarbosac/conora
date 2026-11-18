using System.Globalization;
using System.Text;
using System.Text.Json;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Conora.Services.Common;
using Conora.Services.Contracts;

namespace Conora.Services;

public sealed partial class WhatsAppService
{
    private async Task<WhatsAppReply> StartConversationAsync(string phone, string text, CancellationToken ct)
    {
        var parsed = await ParseEntryAsync(text, ct);
        var description = parsed?.Description?.Trim();
        if (string.IsNullOrWhiteSpace(description))
            description = text.Trim();

        var type = parsed?.Type ?? (IncomeWords.Any(text.ToLowerInvariant().Contains) ? EntryType.Income : EntryType.Expense);
        var occurredAt = parsed?.OccurredAt ?? DateTime.UtcNow;
        var amount = parsed is { Amount: > 0 } ? parsed.Amount : null;

        WhatsAppSessionState state;
        string reply;
        if (amount is null)
        {
            state = new WhatsAppSessionState(WhatsAppSessionSteps.NeedAmount, type, null, description, occurredAt);
            reply = "Beleza. Me diz o valor desse lançamento, por favor. Exemplo: 45,90.";
        }
        else
        {
            state = new WhatsAppSessionState(WhatsAppSessionSteps.ConfirmAmount, type, amount, description, occurredAt);
            reply = BuildAmountConfirmPrompt(state);
        }

        await SaveSessionAsync(phone, state, ct);
        return new WhatsAppReply(phone, reply);
    }

    private async Task<WhatsAppReply> ContinueConversationAsync(
        WhatsAppDraft draft,
        WhatsAppSessionState state,
        string text,
        string phone,
        CancellationToken ct)
    {
        if (IsCancel(text))
        {
            draft.Discard();
            await _uow.SaveChangesAsync(ct);
            return new WhatsAppReply(phone, "Tudo bem, cancelei essa conversa. Quando quiser lançar de novo, é só mandar.");
        }

        return state.Step switch
        {
            WhatsAppSessionSteps.NeedAmount => await HandleNeedAmountAsync(draft, state, text, phone, ct),
            WhatsAppSessionSteps.ConfirmAmount => await HandleConfirmAmountAsync(draft, state, text, phone, ct),
            WhatsAppSessionSteps.NeedCategory => await HandleNeedCategoryAsync(draft, state, text, phone, ct),
            WhatsAppSessionSteps.ConfirmCategory => await HandleConfirmCategoryAsync(draft, state, text, phone, ct),
            WhatsAppSessionSteps.NeedAccount => await HandleNeedAccountAsync(draft, state, text, phone, ct),
            WhatsAppSessionSteps.ConfirmAccount => await HandleConfirmAccountAsync(draft, state, text, phone, ct),
            WhatsAppSessionSteps.NeedDebtChoice => await HandleNeedDebtChoiceAsync(draft, state, text, phone, ct),
            WhatsAppSessionSteps.ConfirmLaunch => await HandleConfirmLaunchAsync(draft, state, text, phone, ct),
            _ => await RestartFromTextAsync(draft, text, phone, ct)
        };
    }

    private async Task<WhatsAppReply> HandleNeedAmountAsync(
        WhatsAppDraft draft, WhatsAppSessionState state, string text, string phone, CancellationToken ct)
    {
        var amount = TryParseAmountOnly(text) ?? (await ParseEntryAsync(text, ct))?.Amount;
        if (amount is null or <= 0)
            return new WhatsAppReply(phone, "Ainda não peguei o valor. Pode mandar só o número? Exemplo: 120,50.");

        var next = state with { Step = WhatsAppSessionSteps.ConfirmAmount, Amount = amount };
        await ReplaceSessionAsync(draft, next, ct);
        return new WhatsAppReply(phone, BuildAmountConfirmPrompt(next));
    }

    private async Task<WhatsAppReply> HandleConfirmAmountAsync(
        WhatsAppDraft draft, WhatsAppSessionState state, string text, string phone, CancellationToken ct)
    {
        if (IsNo(text))
        {
            var next = state with { Step = WhatsAppSessionSteps.NeedAmount, Amount = null };
            await ReplaceSessionAsync(draft, next, ct);
            return new WhatsAppReply(phone, "Sem problema. Qual é o valor certo?");
        }

        if (!IsYes(text))
        {
            var corrected = TryParseAmountOnly(text);
            if (corrected is > 0)
            {
                var next = state with { Step = WhatsAppSessionSteps.ConfirmAmount, Amount = corrected };
                await ReplaceSessionAsync(draft, next, ct);
                return new WhatsAppReply(phone, BuildAmountConfirmPrompt(next));
            }

            return new WhatsAppReply(phone, $"{BuildAmountConfirmPrompt(state)}\nResponda sim para confirmar, ou mande o valor certo.");
        }

        return await AdvanceToCategoryAsync(draft, state, phone, ct);
    }

    private async Task<WhatsAppReply> AdvanceToCategoryAsync(
        WhatsAppDraft draft, WhatsAppSessionState state, string phone, CancellationToken ct)
    {
        var suggestion = await SuggestCategoryAsync(state, ct);
        if (suggestion is not null)
        {
            var next = state with
            {
                Step = WhatsAppSessionSteps.ConfirmCategory,
                CategoryId = suggestion.Value.Id,
                CategoryName = suggestion.Value.Name,
                ChoiceIds = null,
                ChoiceLabels = null
            };
            await ReplaceSessionAsync(draft, next, ct);
            return new WhatsAppReply(phone, BuildCategoryConfirmPrompt(next));
        }

        return await PromptCategoryListAsync(draft, state, phone, ct,
            "Não achei uma categoria com segurança. Escolha o número da lista:");
    }

    private async Task<WhatsAppReply> HandleConfirmCategoryAsync(
        WhatsAppDraft draft, WhatsAppSessionState state, string text, string phone, CancellationToken ct)
    {
        if (IsNo(text))
            return await PromptCategoryListAsync(draft, state with { CategoryId = null, CategoryName = null }, phone, ct,
                "Tranquilo. Qual categoria encaixa melhor? Responda com o número:");

        if (!IsYes(text))
        {
            if (TryPickChoice(state, text, out var pickedId, out var pickedLabel))
            {
                var picked = state with
                {
                    Step = WhatsAppSessionSteps.ConfirmCategory,
                    CategoryId = pickedId,
                    CategoryName = pickedLabel,
                    ChoiceIds = null,
                    ChoiceLabels = null
                };
                await ReplaceSessionAsync(draft, picked, ct);
                return new WhatsAppReply(phone, BuildCategoryConfirmPrompt(picked));
            }

            return new WhatsAppReply(phone, $"{BuildCategoryConfirmPrompt(state)}\nResponda sim para confirmar, ou não para ver outras opções.");
        }

        return await AdvanceToAccountAsync(draft, state, phone, ct);
    }

    private async Task<WhatsAppReply> HandleNeedCategoryAsync(
        WhatsAppDraft draft, WhatsAppSessionState state, string text, string phone, CancellationToken ct)
    {
        if (TryPickChoice(state, text, out var id, out var label))
        {
            var next = state with
            {
                Step = WhatsAppSessionSteps.ConfirmCategory,
                CategoryId = id,
                CategoryName = label,
                ChoiceIds = null,
                ChoiceLabels = null
            };
            await ReplaceSessionAsync(draft, next, ct);
            return new WhatsAppReply(phone, BuildCategoryConfirmPrompt(next));
        }

        var match = await FindCategoryByTextAsync(state.Type, text, ct);
        if (match is not null)
        {
            var next = state with
            {
                Step = WhatsAppSessionSteps.ConfirmCategory,
                CategoryId = match.Value.Id,
                CategoryName = match.Value.Name,
                ChoiceIds = null,
                ChoiceLabels = null
            };
            await ReplaceSessionAsync(draft, next, ct);
            return new WhatsAppReply(phone, BuildCategoryConfirmPrompt(next));
        }

        return await PromptCategoryListAsync(draft, state, phone, ct,
            "Não entendi essa categoria. Escolha pelo número da lista:");
    }

    private async Task<WhatsAppReply> AdvanceToAccountAsync(
        WhatsAppDraft draft, WhatsAppSessionState state, string phone, CancellationToken ct)
    {
        var accounts = await ListBankAccountsAsync(ct);
        if (accounts.Count == 0)
        {
            var withoutAccount = state with
            {
                Step = WhatsAppSessionSteps.ConfirmLaunch,
                AccountId = null,
                AccountName = null,
                ChoiceIds = null,
                ChoiceLabels = null
            };
            // Still ask debts before final confirm when category has open debts.
            return await AdvanceAfterAccountAsync(draft, withoutAccount, phone, ct);
        }

        if (accounts.Count == 1)
        {
            var only = accounts[0];
            var next = state with
            {
                Step = WhatsAppSessionSteps.ConfirmAccount,
                AccountId = only.Id,
                AccountName = only.Name,
                ChoiceIds = null,
                ChoiceLabels = null
            };
            await ReplaceSessionAsync(draft, next, ct);
            return new WhatsAppReply(phone, BuildAccountConfirmPrompt(next));
        }

        return await PromptAccountListAsync(draft, state, accounts, phone, ct,
            "Qual conta bancária usou nesse lançamento? Responda com o número:");
    }

    private async Task<WhatsAppReply> HandleConfirmAccountAsync(
        WhatsAppDraft draft, WhatsAppSessionState state, string text, string phone, CancellationToken ct)
    {
        if (IsNo(text))
        {
            var accounts = await ListBankAccountsAsync(ct);
            return await PromptAccountListAsync(draft, state with { AccountId = null, AccountName = null }, accounts, phone, ct,
                "Sem problema. Qual conta então? Responda com o número:");
        }

        if (!IsYes(text))
        {
            if (TryPickChoice(state, text, out var pickedId, out var pickedLabel))
            {
                var picked = state with
                {
                    Step = WhatsAppSessionSteps.ConfirmAccount,
                    AccountId = pickedId,
                    AccountName = pickedLabel,
                    ChoiceIds = null,
                    ChoiceLabels = null
                };
                await ReplaceSessionAsync(draft, picked, ct);
                return new WhatsAppReply(phone, BuildAccountConfirmPrompt(picked));
            }

            return new WhatsAppReply(phone, $"{BuildAccountConfirmPrompt(state)}\nResponda sim para confirmar, ou não para escolher outra.");
        }

        return await AdvanceAfterAccountAsync(draft, state, phone, ct);
    }

    private async Task<WhatsAppReply> HandleNeedAccountAsync(
        WhatsAppDraft draft, WhatsAppSessionState state, string text, string phone, CancellationToken ct)
    {
        if (TryPickChoice(state, text, out var id, out var label))
        {
            var next = state with
            {
                Step = WhatsAppSessionSteps.ConfirmAccount,
                AccountId = id,
                AccountName = label,
                ChoiceIds = null,
                ChoiceLabels = null
            };
            await ReplaceSessionAsync(draft, next, ct);
            return new WhatsAppReply(phone, BuildAccountConfirmPrompt(next));
        }

        var accounts = await ListBankAccountsAsync(ct);
        var byName = accounts.FirstOrDefault(a =>
            a.Name.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            var next = state with
            {
                Step = WhatsAppSessionSteps.ConfirmAccount,
                AccountId = byName.Id,
                AccountName = byName.Name,
                ChoiceIds = null,
                ChoiceLabels = null
            };
            await ReplaceSessionAsync(draft, next, ct);
            return new WhatsAppReply(phone, BuildAccountConfirmPrompt(next));
        }

        return await PromptAccountListAsync(draft, state, accounts, phone, ct,
            "Não achei essa conta. Escolha pelo número:");
    }

    private async Task<WhatsAppReply> AdvanceAfterAccountAsync(
        WhatsAppDraft draft, WhatsAppSessionState state, string phone, CancellationToken ct)
    {
        if (state.Type == EntryType.Expense && state.CategoryId is Guid categoryId)
        {
            var debts = (await _debts.ListOpenAsync(ct))
                .Where(d => d.CategoryId == categoryId)
                .ToList();
            if (debts.Count > 0)
            {
                var labels = debts
                    .Select(d => $"{d.CategoryName} ({d.CompetenceYm}) — aberto R$ {d.OpenAmount.ToString("N2", PtBr)}")
                    .ToList();
                var next = state with
                {
                    Step = WhatsAppSessionSteps.NeedDebtChoice,
                    ChoiceIds = debts.Select(d => d.Id).ToList(),
                    ChoiceLabels = labels,
                    OpenDebtIds = null
                };
                await ReplaceSessionAsync(draft, next, ct);
                var sb = new StringBuilder();
                sb.AppendLine("Vi dívida em aberto nessa categoria. Quer abater alguma com esse lançamento?");
                for (var i = 0; i < labels.Count; i++)
                    sb.AppendLine($"{i + 1}. {labels[i]}");
                sb.Append("0. Não abater agora");
                return new WhatsAppReply(phone, sb.ToString());
            }
        }

        return await PromptLaunchConfirmAsync(draft, state with { OpenDebtIds = null, ChoiceIds = null, ChoiceLabels = null }, phone, ct);
    }

    private async Task<WhatsAppReply> HandleNeedDebtChoiceAsync(
        WhatsAppDraft draft, WhatsAppSessionState state, string text, string phone, CancellationToken ct)
    {
        var trimmed = text.Trim();
        if (trimmed is "0" || IsNo(trimmed))
            return await PromptLaunchConfirmAsync(draft, state with { OpenDebtIds = null, ChoiceIds = null, ChoiceLabels = null }, phone, ct);

        if (IsYes(trimmed) && state.ChoiceIds is { Count: 1 })
            return await PromptLaunchConfirmAsync(draft, state with
            {
                OpenDebtIds = state.ChoiceIds,
                ChoiceIds = null,
                ChoiceLabels = null
            }, phone, ct);

        var selected = new List<Guid>();
        foreach (var part in trimmed.Split([' ', ',', ';', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                continue;
            if (state.ChoiceIds is null || index < 1 || index > state.ChoiceIds.Count)
                continue;
            selected.Add(state.ChoiceIds[index - 1]);
        }

        if (selected.Count == 0)
        {
            return new WhatsAppReply(phone,
                "Responda com o número da dívida, vários números na ordem, ou 0 para não abater.");
        }

        return await PromptLaunchConfirmAsync(draft, state with
        {
            OpenDebtIds = selected,
            ChoiceIds = null,
            ChoiceLabels = null
        }, phone, ct);
    }

    private async Task<WhatsAppReply> HandleConfirmLaunchAsync(
        WhatsAppDraft draft, WhatsAppSessionState state, string text, string phone, CancellationToken ct)
    {
        if (IsNo(text))
        {
            var next = state with { Step = WhatsAppSessionSteps.ConfirmAmount };
            await ReplaceSessionAsync(draft, next, ct);
            return new WhatsAppReply(phone,
                "Ok, vamos revisar. " + BuildAmountConfirmPrompt(next) + "\nOu diga cancelar para encerrar.");
        }

        if (!IsYes(text))
            return new WhatsAppReply(phone, $"{BuildLaunchConfirmPrompt(state)}\nResponda sim para lançar, ou não para revisar.");

        if (state.Amount is null or <= 0)
        {
            var needAmount = state with { Step = WhatsAppSessionSteps.NeedAmount };
            await ReplaceSessionAsync(draft, needAmount, ct);
            return new WhatsAppReply(phone, "Faltou o valor. Qual é o valor do lançamento?");
        }

        try
        {
            var created = await _entries.CreateAsync(new CreateEntryRequest(
                state.Type,
                state.Amount.Value,
                state.OccurredAt,
                state.Description,
                AccountId: state.AccountId,
                CategoryId: state.CategoryId,
                PendingDebtIds: state.OpenDebtIds), ct);

            var entry = created[0];
            draft.Confirm();
            AuditRecorder.Record(_audits, _correlation, "Entry", entry.Id, "WhatsAppEntryCreated",
                new { entry.Type, entry.Amount, Phone = phone, Conversation = true });
            await _uow.SaveChangesAsync(ct);

            var kind = entry.Type == EntryType.Income ? "receita" : "despesa";
            var amount = entry.Amount.ToString("N2", PtBr);
            var categoryBit = string.IsNullOrWhiteSpace(state.CategoryName) ? "" : $" em {state.CategoryName}";
            var accountBit = string.IsNullOrWhiteSpace(state.AccountName) ? "" : $", conta {state.AccountName}";
            return new WhatsAppReply(phone,
                $"Pronto. Lançamento gravado: {kind} de R$ {amount}{categoryBit}{accountBit} ({entry.Description}).");
        }
        catch (ValidationException ex)
        {
            var detail = ex.Errors.SelectMany(pair => pair.Value).FirstOrDefault() ?? ex.Message;
            return new WhatsAppReply(phone, $"Não consegui gravar o lançamento: {detail}");
        }
        catch (DomainException ex)
        {
            return new WhatsAppReply(phone, $"Não consegui gravar o lançamento: {ex.Message}");
        }
    }

    private async Task<WhatsAppReply> PromptLaunchConfirmAsync(
        WhatsAppDraft draft, WhatsAppSessionState state, string phone, CancellationToken ct)
    {
        var next = state with { Step = WhatsAppSessionSteps.ConfirmLaunch };
        await ReplaceSessionAsync(draft, next, ct);
        return new WhatsAppReply(phone, BuildLaunchConfirmPrompt(next));
    }

    private async Task<WhatsAppReply> PromptCategoryListAsync(
        WhatsAppDraft draft,
        WhatsAppSessionState state,
        string phone,
        CancellationToken ct,
        string header)
    {
        var categories = await ListPostableCategoriesAsync(state.Type, ct);
        if (categories.Count == 0)
            return new WhatsAppReply(phone, "Não encontrei categorias ativas nessa conta. Cadastre uma no app e tente de novo.");

        var ranked = RankCategories(categories, state.Description).Take(8).ToList();
        var next = state with
        {
            Step = WhatsAppSessionSteps.NeedCategory,
            ChoiceIds = ranked.Select(c => c.Id).ToList(),
            ChoiceLabels = ranked.Select(c => c.Name).ToList(),
            CategoryId = null,
            CategoryName = null
        };
        await ReplaceSessionAsync(draft, next, ct);

        var sb = new StringBuilder(header);
        for (var i = 0; i < ranked.Count; i++)
            sb.AppendLine().Append($"{i + 1}. {ranked[i].Name}");
        return new WhatsAppReply(phone, sb.ToString());
    }

    private async Task<WhatsAppReply> PromptAccountListAsync(
        WhatsAppDraft draft,
        WhatsAppSessionState state,
        IReadOnlyList<Account> accounts,
        string phone,
        CancellationToken ct,
        string header)
    {
        if (accounts.Count == 0)
            return await AdvanceAfterAccountAsync(draft, state, phone, ct);

        var next = state with
        {
            Step = WhatsAppSessionSteps.NeedAccount,
            ChoiceIds = accounts.Select(a => a.Id).ToList(),
            ChoiceLabels = accounts.Select(a => a.Name).ToList(),
            AccountId = null,
            AccountName = null
        };
        await ReplaceSessionAsync(draft, next, ct);

        var sb = new StringBuilder(header);
        for (var i = 0; i < accounts.Count; i++)
            sb.AppendLine().Append($"{i + 1}. {accounts[i].Name}");
        return new WhatsAppReply(phone, sb.ToString());
    }

    private async Task<WhatsAppReply> RestartFromTextAsync(WhatsAppDraft draft, string text, string phone, CancellationToken ct)
    {
        draft.Discard();
        await _uow.SaveChangesAsync(ct);
        return await StartConversationAsync(phone, text, ct);
    }

    private static string BuildAmountConfirmPrompt(WhatsAppSessionState state)
    {
        var amount = state.Amount?.ToString("N2", PtBr) ?? "—";
        var topic = WhatsAppInbound.TopicFromDescription(state.Description);
        return string.IsNullOrWhiteSpace(topic)
            ? $"Entendi o valor de R$ {amount}. Esse valor está certo?"
            : $"Referente a {topic}, entendi R$ {amount}. Esse valor está certo?";
    }

    private static string BuildCategoryConfirmPrompt(WhatsAppSessionState state)
        => WhatsAppInbound.CategoryConfirmPrompt(state.Description, state.CategoryName ?? "essa categoria");

    private static string BuildAccountConfirmPrompt(WhatsAppSessionState state)
    {
        var account = state.AccountName ?? "essa conta";
        return $"Para a conta bancária, eu achei {account}. Você confirma?";
    }

    private static string BuildLaunchConfirmPrompt(WhatsAppSessionState state)
    {
        var kind = state.Type == EntryType.Income ? "receita" : "despesa";
        var amount = state.Amount?.ToString("N2", PtBr) ?? "—";
        var sb = new StringBuilder();
        sb.Append($"Resumo antes de lançar: {kind} de R$ {amount}");
        if (!string.IsNullOrWhiteSpace(state.CategoryName))
            sb.Append($", categoria {state.CategoryName}");
        if (!string.IsNullOrWhiteSpace(state.AccountName))
            sb.Append($", conta {state.AccountName}");
        if (state.OpenDebtIds is { Count: > 0 })
            sb.Append($", abatendo {state.OpenDebtIds.Count} dívida(s)");
        sb.Append($". Descrição: {state.Description}. Posso lançar?");
        return sb.ToString();
    }

    private async Task<(Guid Id, string Name)?> SuggestCategoryAsync(WhatsAppSessionState state, CancellationToken ct)
    {
        var fromHistory = await _entries.SuggestAccountAsync(state.Description, ct);
        if (fromHistory.CategoryId is Guid historyId && !string.IsNullOrWhiteSpace(fromHistory.CategoryName))
            return (historyId, fromHistory.CategoryName!);

        return await FindCategoryByTextAsync(state.Type, state.Description, ct);
    }

    private async Task<(Guid Id, string Name)?> FindCategoryByTextAsync(EntryType type, string text, CancellationToken ct)
    {
        var categories = await ListPostableCategoriesAsync(type, ct);
        var topic = WhatsAppInbound.TopicFromDescription(text);
        var tokens = topic.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var top = categories
            .Select(c => (c, score: CategoryMatchScore(c.Name, topic, tokens)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.c)
            .FirstOrDefault();
        return top is null ? null : (top.Id, top.Name);
    }

    private async Task<IReadOnlyList<Category>> ListPostableCategoriesAsync(EntryType type, CancellationToken ct)
    {
        var sections = type == EntryType.Income
            ? new[] { CategorySection.Income, CategorySection.LifeProject, CategorySection.Asset, CategorySection.Liability }
            : new[] { CategorySection.Expense, CategorySection.Liability, CategorySection.Asset, CategorySection.LifeProject };

        return await _repo.ListAsync<Category>(
            c => c.Level == CategoryLevel.Analytical && c.IsActive && !c.IsPendingReview && sections.Contains(c.Section),
            ct);
    }

    private static IEnumerable<Category> RankCategories(IReadOnlyList<Category> categories, string text)
    {
        var topic = WhatsAppInbound.TopicFromDescription(text);
        var tokens = topic.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return categories
            .Select(c => (c, score: CategoryMatchScore(c.Name, topic, tokens)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.c)
            .Concat(categories.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            .DistinctBy(c => c.Id);
    }

    private static int CategoryMatchScore(string categoryName, string topic, IReadOnlyList<string> tokens)
    {
        var name = categoryName.ToLowerInvariant();
        var score = 0;
        if (!string.IsNullOrWhiteSpace(topic) && name.Contains(topic, StringComparison.OrdinalIgnoreCase))
            score += 50;
        foreach (var token in tokens)
        {
            if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
                score += 10 + Math.Min(token.Length, 8);
        }
        return score;
    }

    private async Task<IReadOnlyList<Account>> ListBankAccountsAsync(CancellationToken ct)
        => await _repo.ListAsync<Account>(a => !a.IsArchived, ct);

    private static bool TryPickChoice(
        WhatsAppSessionState state,
        string text,
        out Guid id,
        out string label)
    {
        id = default;
        label = "";
        if (state.ChoiceIds is null || state.ChoiceLabels is null)
            return false;
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            return false;
        if (index < 1 || index > state.ChoiceIds.Count || index > state.ChoiceLabels.Count)
            return false;
        id = state.ChoiceIds[index - 1];
        label = state.ChoiceLabels[index - 1];
        return true;
    }

    private static decimal? TryParseAmountOnly(string text)
    {
        var match = AmountRegex().Match(text);
        if (!match.Success)
            return null;
        var amount = ImportParser.ParseAmount(match.Value);
        if (amount is null or <= 0 or > 99_999_999.99m)
            return null;
        return decimal.Round(amount.Value, 2);
    }

    private async Task<(WhatsAppDraft Draft, WhatsAppSessionState State)?> FindPendingSessionAsync(
        string phone,
        CancellationToken ct)
    {
        var draft = await _repo.FirstOrDefaultAsync<WhatsAppDraft>(
            d => d.PhoneE164 == phone && d.Status == WhatsAppDraftStatus.Pending,
            ct,
            track: true);
        if (draft is null)
            return null;

        try
        {
            var state = JsonSerializer.Deserialize<WhatsAppSessionState>(draft.PayloadJson, SessionJson);
            if (state is null || string.IsNullOrWhiteSpace(state.Step))
                return null;
            return (draft, state);
        }
        catch (JsonException)
        {
            draft.Discard();
            await _uow.SaveChangesAsync(ct);
            return null;
        }
    }

    private async Task SaveSessionAsync(string phone, WhatsAppSessionState state, CancellationToken ct)
    {
        var existing = await _repo.ListAsync<WhatsAppDraft>(
            d => d.PhoneE164 == phone && d.Status == WhatsAppDraftStatus.Pending,
            ct,
            track: true);
        foreach (var old in existing)
            old.Discard();

        var draft = WhatsAppDraft.Create(phone, JsonSerializer.Serialize(state, SessionJson));
        _repo.Add(draft);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task ReplaceSessionAsync(WhatsAppDraft draft, WhatsAppSessionState state, CancellationToken ct)
    {
        draft.ReplacePayload(JsonSerializer.Serialize(state, SessionJson));
        await _uow.SaveChangesAsync(ct);
    }

    private async Task DiscardSessionAsync(string phone, CancellationToken ct)
    {
        var existing = await _repo.ListAsync<WhatsAppDraft>(
            d => d.PhoneE164 == phone && d.Status == WhatsAppDraftStatus.Pending,
            ct,
            track: true);
        if (existing.Count == 0)
            return;
        foreach (var draft in existing)
            draft.Discard();
        await _uow.SaveChangesAsync(ct);
    }

    private static bool IsCancel(string text)
    {
        var lowered = text.Trim().ToLowerInvariant();
        return CancelWords.Any(w => lowered == w || lowered.StartsWith(w + " ", StringComparison.Ordinal));
    }

    private static bool IsYes(string text)
    {
        var lowered = Fold(text.Trim());
        return YesWords.Any(w => lowered == Fold(w));
    }

    private static bool IsNo(string text)
    {
        var lowered = Fold(text.Trim());
        return NoWords.Any(w => lowered == Fold(w) || lowered.StartsWith(Fold(w) + " ", StringComparison.Ordinal));
    }

    private static string Fold(string value)
        => value.ToLowerInvariant()
            .Replace("á", "a").Replace("à", "a").Replace("ã", "a").Replace("â", "a")
            .Replace("é", "e").Replace("ê", "e")
            .Replace("í", "i")
            .Replace("ó", "o").Replace("ô", "o").Replace("õ", "o")
            .Replace("ú", "u").Replace("ç", "c");
}
