namespace Application.UseCases.Demand;

public sealed record GetCurrentDemandWorkspaceCommand(Guid? CompanyId = null);

