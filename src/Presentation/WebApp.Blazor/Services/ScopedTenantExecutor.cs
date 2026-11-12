using Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace WebApp.Blazor.Services;

/// <summary>
/// Executa casos de uso em um escopo de DI isolado com <see cref="IScopedTenantContext"/> ativo,
/// evitando concorrência sobre o <c>AppDbContext</c> principal do circuito Blazor Server.
/// </summary>
internal static class ScopedTenantExecutor
{
    public static async Task<TResult> ExecuteAsync<TUseCase, TResult>(
        this IServiceScopeFactory scopeFactory,
        Guid tenantId,
        Func<TUseCase, Task<TResult>> action)
        where TUseCase : notnull
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(action);

        await using var scope = scopeFactory.CreateAsyncScope();
        var scopedTenantContext = scope.ServiceProvider.GetService<IScopedTenantContext>();
        using var tenantScope = tenantId != Guid.Empty && scopedTenantContext is not null
            ? scopedTenantContext.BeginScope(tenantId)
            : null;

        var useCase = scope.ServiceProvider.GetRequiredService<TUseCase>();
        return await action(useCase);
    }
}

