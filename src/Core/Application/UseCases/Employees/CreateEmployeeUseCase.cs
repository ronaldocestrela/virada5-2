using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Employees;
using Application.UseCases.Common;
using Domain.Entities;
using Domain.Repositories;

namespace Application.UseCases.Employees;

/// <summary>
/// Cria e persiste um novo colaborador com sua função principal, matriz de polivalência e restrições individuais.
/// </summary>
public sealed class CreateEmployeeUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IResultTelemetry? _telemetry;

    public CreateEmployeeUseCase(
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IResultTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _companyRepository = companyRepository;
        _employeeRepository = employeeRepository;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _telemetry = telemetry;
    }

    public Task<Result<EmployeeDto>> ExecuteAsync(
        CreateEmployeeCommand command,
        CancellationToken ct = default)
        => ApplicationTelemetry.ObserveAsync(
            "CreateEmployee",
            _currentUserService.TenantId,
            _telemetry,
            () => ExecuteCoreAsync(command, ct));

    private async Task<Result<EmployeeDto>> ExecuteCoreAsync(
        CreateEmployeeCommand command,
        CancellationToken ct)
    {
        if (command is null)
        {
            return Result.Failure<EmployeeDto>(
                Error.Validation("Employee.InvalidCommand", "Comando de criação de colaborador inválido."));
        }

        var contextResult = await CompanyResolutionHelper.ResolveCompanyAsync(
            command.CompanyId,
            _companyRepository,
            _currentUserService,
            "Employee.EmptyTenantId",
            ct);

        if (contextResult.IsFailure)
        {
            return Result.Failure<EmployeeDto>(contextResult.Error);
        }

        var (tenantId, company) = contextResult.Value;

        var skillsResult = EmployeeMapper.ToDomainSecondarySkills(command.SecondarySkills);
        if (skillsResult.IsFailure)
        {
            return Result.Failure<EmployeeDto>(skillsResult.Error);
        }

        var employeeResult = Employee.Create(
            tenantId,
            company.Id,
            command.Name,
            command.PrimaryRole,
            skillsResult.Value,
            command.PreferredDaysOff,
            command.UnavailableDays);

        if (employeeResult.IsFailure)
        {
            return Result.Failure<EmployeeDto>(employeeResult.Error);
        }

        var employee = employeeResult.Value;
        await _employeeRepository.AddAsync(employee, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(EmployeeMapper.ToDto(employee));
    }
}

