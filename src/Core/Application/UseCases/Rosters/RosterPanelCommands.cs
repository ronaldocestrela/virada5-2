using Domain.Enums;

namespace Application.UseCases.Rosters;

public sealed record GetCurrentRosterWorkspaceCommand(
    DateOnly? WeekStartDate = null,
    Guid? CompanyId = null);

public sealed record AssignManualShiftCommand(
    Guid RosterId,
    Guid EmployeeId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    StationRole AssignedRole);

public sealed record RemoveManualShiftCommand(
    Guid RosterId,
    Guid ShiftId);

public sealed record ReassignManualShiftCommand(
    Guid RosterId,
    Guid ShiftId,
    Guid TargetEmployeeId);

public sealed record SwapManualShiftsCommand(
    Guid RosterId,
    Guid FirstShiftId,
    Guid SecondShiftId);

public sealed record PublishRosterCommand(
    Guid RosterId);

public sealed record ExportRosterCsvCommand(
    Guid RosterId);

public sealed record GenerateRosterShareLinkCommand(
    Guid RosterId,
    int ExpirationDays = 14);
