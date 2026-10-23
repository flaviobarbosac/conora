using Conora.Domain.Ports;
using Conora.Services.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace Conora.Services;

public static class DependencyInjection
{
    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<UserService>();
        services.AddScoped<AuditService>();
        services.AddScoped<ConsoleService>();
        services.AddScoped<AuthService>();
        services.AddScoped<LgpdService>();

        // Finance domain
        services.AddScoped<PlanService>();
        services.AddScoped<IStoreSubscriptionBilling, StoreSubscriptionBilling>();
        services.AddScoped<MonthService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<MemberService>();
        services.AddScoped<FamilyGroupService>();
        services.AddScoped<EntryService>();
        services.AddScoped<AccountService>();
        services.AddScoped<CreditCardService>();
        services.AddScoped<BudgetService>();
        services.AddScoped<DiagnosisService>();
        services.AddScoped<ImportService>();
        services.AddScoped<LifeProjectService>();
        services.AddScoped<PatrimonyService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<GamificationService>();
        services.AddScoped<MilestoneEvaluationService>();
        services.AddScoped<GeminiService>();
        services.AddScoped<VoiceCommandService>();
        services.AddScoped<WhatsAppService>();
        services.AddScoped<ReceiptIntakeService>();
        services.AddScoped<SesFeedbackService>();
        services.AddScoped<FeedbackService>();
        services.AddSingleton<HelpService>();
        return services;
    }
}
