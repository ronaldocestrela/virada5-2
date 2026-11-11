namespace Application.UseCases.Employees;

public sealed record SetEmployeeActiveStatusCommand(
    Guid EmployeeId,
    bool IsActive,
    Guid? CompanyId = null);

