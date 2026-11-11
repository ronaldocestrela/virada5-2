using Application.Common.Interfaces;
using Application.DTOs.Demand;
using Domain.Entities;
using Domain.Repositories;

namespace Application.UseCases.Demand;

internal static class DemandUseCaseHelpers
{
    internal static async Task<Result<(Guid TenantId, Company Company)>> ResolveCompanyAsync(
        Guid? companyId,
        ICompanyRepository companyRepository,
        ICurrentUserService currentUserService,
        CancellationToken ct)
    {
        var tenantId = currentUserService.TenantId;
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<(Guid, Company)>(
                Error.Validation("Demand.EmptyTenant", "O identificador do tenant autenticado é obrigatório."));
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

    internal static List<DemandSlotSnapshotDto> MapToOrderedSnapshots(
        IEnumerable<DemandSlot> slots,
        Company company) =>
        slots
            .OrderBy(s => s.DayOfWeek)
            .ThenBy(s => s.Hour)
            .Select(s => new DemandSlotSnapshotDto(
                s.Id,
                s.DayOfWeek,
                s.Hour,
                s.ExpectedVolume,
                s.RelativeVolume,
                s.Classification,
                company.IsOpenAt(s.DayOfWeek, s.Hour)))
            .ToList();
}

