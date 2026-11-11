using Application.Common.Interfaces;
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

    public GetCurrentEmployeesWorkspaceUseCase(
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        ICurrentUserService currentUserService)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);

        _companyRepository = companyRepository;
        _employeeRepository = employeeRepository;
        _currentUserService = currentUserService;
    }

    public async Task<Result<EmployeesWorkspaceDto>> ExecuteAsync(
        Guid? companyId = null,
        CancellationToken ct = default)
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

