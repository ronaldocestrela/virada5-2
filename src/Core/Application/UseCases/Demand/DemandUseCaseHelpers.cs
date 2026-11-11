using Application.Common.Interfaces;
using Application.DTOs.Demand;
using Application.UseCases.Common;
using Domain.Entities;
using Domain.Repositories;

namespace Application.UseCases.Demand;

internal static class DemandUseCaseHelpers
{
    internal static Task<Result<(Guid TenantId, Company Company)>> ResolveCompanyAsync(
        Guid? companyId,
        ICompanyRepository companyRepository,
        ICurrentUserService currentUserService,
        CancellationToken ct) =>
        CompanyResolutionHelper.ResolveCompanyAsync(
            companyId,
            companyRepository,
            currentUserService,
            "Demand.EmptyTenant",
            ct);

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

