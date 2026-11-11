using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Employees;
using Application.UseCases.Common;
using Domain.Repositories;

namespace Application.UseCases.Employees;

/// <summary>
/// Ativa ou desativa um colaborador para alocação em escalas e trocas de turno.
/// </summary>
public sealed class SetEmployeeActiveStatusUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IResultTelemetry? _telemetry;

    public SetEmployeeActiveStatusUseCase(
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
        SetEmployeeActiveStatusCommand command,
        CancellationToken ct = default)
        => ApplicationTelemetry.ObserveAsync(
            "SetEmployeeActiveStatus",
            _currentUserService.TenantId,
            _telemetry,
            () => ExecuteCoreAsync(command, ct));

    private async Task<Result<EmployeeDto>> ExecuteCoreAsync(
        SetEmployeeActiveStatusCommand command,
        CancellationToken ct)
    {
        if (command is null)
        {
            return Result.Failure<EmployeeDto>(
                Error.Validation("Employee.InvalidCommand", "Comando de alteração de status do colaborador inválido."));
        }

        if (command.EmployeeId == Guid.Empty)
        {
            return Result.Failure<EmployeeDto>(
                Error.Validation("Employee.EmptyEmployeeId", "O identificador do colaborador é obrigatório."));
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

        var (_, company) = contextResult.Value;

        var employee = await _employeeRepository.GetByIdAsync(command.EmployeeId, ct);
        if (employee is null || employee.CompanyId != company.Id)
        {
            return Result.Failure<EmployeeDto>(
                Error.NotFound("Employee.NotFound", "Colaborador não encontrado para o estabelecimento atual."));
        }

        var statusResult = command.IsActive
            ? employee.Activate()
            : employee.Deactivate();

        if (statusResult.IsFailure)
        {
            return Result.Failure<EmployeeDto>(statusResult.Error);
        }

        _employeeRepository.Update(employee);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(EmployeeMapper.ToDto(employee));
    }
}

