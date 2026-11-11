using Application.DTOs.Demand;

namespace Application.UseCases.Demand;

public sealed record ParseSalesHistoryCommand(
    DemandImportSource Source,
    Guid? CompanyId = null,
    Stream? FileStream = null,
    string? FileName = null,
    IReadOnlyList<ManualDemandEntryDto>? ManualEntries = null,
    bool FillMissingOperatingHoursWithZero = true);

