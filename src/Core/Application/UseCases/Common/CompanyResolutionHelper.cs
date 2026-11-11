using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Repositories;

namespace Application.UseCases.Common;

internal static class CompanyResolutionHelper
{
    internal static async Task<Result<(Guid TenantId, Company Company)>> ResolveCompanyAsync(
        Guid? companyId,
        ICompanyRepository companyRepository,
        ICurrentUserService currentUserService,
        string emptyTenantErrorCode,
        CancellationToken ct)
    {
        var tenantId = currentUserService.TenantId;
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<(Guid, Company)>(
                Error.Validation(emptyTenantErrorCode, "O identificador do tenant autenticado é obrigatório."));
        }

        var company = companyId.HasValue && companyId.Value != Guid.Empty
            ? await companyRepository.GetByIdAsync(companyId.Value, ct)
            : await companyRepository.GetCurrentAsync(ct);

        if (company is null)
        {
            return Result.Failure<(Guid, Company)>(
                Error.NotFound("Company.NotFound", "Estabelecimento não encontrado para o tenant atual."));
        }

        return Result.Success((tenantId, company));
    }
}

