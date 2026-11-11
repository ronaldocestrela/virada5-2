using Application.UseCases.Companies;
using Application.UseCases.Demand;
using Application.UseCases.Financial;
using Application.UseCases.Rosters;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.Extensions;

/// <summary>
/// Registra os casos de uso da camada Application no container de DI do Blazor Server.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<GetCurrentCompanySettingsUseCase>();
        services.AddScoped<SaveCompanySettingsUseCase>();
        services.AddScoped<GetCurrentDemandWorkspaceUseCase>();
        services.AddScoped<ParseSalesHistoryUseCase>();
        services.AddScoped<GenerateDemandDiagnosisUseCase>();
        services.AddScoped<SimulateFinancialImpactUseCase>();
        services.AddScoped<GenerateRoster5x2UseCase>();
        services.AddScoped<ValidateRosterComplianceUseCase>();
        services.AddScoped<RequestShiftSwapUseCase>();
        services.AddScoped<RespondShiftSwapPeerUseCase>();
        services.AddScoped<ApproveShiftSwapUseCase>();
        services.AddScoped<RejectShiftSwapUseCase>();
        services.AddScoped<IToastService, ToastService>();

        return services;
    }
}

