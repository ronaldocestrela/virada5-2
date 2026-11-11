using Application.UseCases.Demand;
using Application.UseCases.Financial;

namespace WebApp.Blazor.Extensions;

/// <summary>
/// Registra os casos de uso da camada Application no container de DI do Blazor Server.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ParseSalesHistoryUseCase>();
        services.AddScoped<GenerateDemandDiagnosisUseCase>();
        services.AddScoped<SimulateFinancialImpactUseCase>();

        return services;
    }
}

