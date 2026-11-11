using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Employees;
using Application.UseCases.Common;
using Domain.Repositories;

namespace Application.UseCases.Employees;

/// <summary>
/// Caso de uso somente leitura para carregar o workspace de gestão de equipe e polivalência do estabelecimento.
/// </summary>
public sealed class GetCurrentEmployeesWorkspaceUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IResultTelemetry? _telemetry;

    public GetCurrentEmployeesWorkspaceUseCase(
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        ICurrentUserService currentUserService,
        IResultTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);

        _companyRepository = companyRepository;
        _employeeRepository = employeeRepository;
        _currentUserService = currentUserService;
        _telemetry = telemetry;
    }

    public Task<Result<EmployeesWorkspaceDto>> ExecuteAsync(
        Guid? companyId = null,
        CancellationToken ct = default)
        => ApplicationTelemetry.ObserveAsync(
            "GetCurrentEmployeesWorkspace",
            _currentUserService.TenantId,
            _telemetry,
            () => ExecuteCoreAsync(companyId, ct));

    private async Task<Result<EmployeesWorkspaceDto>> ExecuteCoreAsync(
        Guid? companyId,
        CancellationToken ct)
    {
        var contextResult = await CompanyResolutionHelper.ResolveCompanyAsync(
            companyId,
            _companyRepository,
            _currentUserService,
            "EmployeeWorkspace.EmptyTenant",
            ct);

        if (contextResult.IsFailure)
        {
            return Result.Failure<EmployeesWorkspaceDto>(contextResult.Error);
        }

        var (_, company) = contextResult.Value;
        var employees = await _employeeRepository.GetByCompanyAsync(company.Id, ct);

        var employeeDtos = employees
            .Select(EmployeeMapper.ToDto)
            .ToList();

        return Result.Success(new EmployeesWorkspaceDto(
            company.Id,
            company.Name,
            company.ScheduleModel,
            company.ClosedDays.OrderBy(day => day == DayOfWeek.Sunday ? 7 : (int)day).ToList(),
            employeeDtos));
    }
}

