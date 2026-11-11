namespace WebApp.Blazor.Components.DesignSystem.RosterGrid;

public readonly record struct RosterDayOffCell(
    Guid EmployeeId,
    string EmployeeName,
    DateOnly Date);

