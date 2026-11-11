using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Employees;
using Application.UseCases.Common;
using Domain.Entities;
using Domain.Repositories;

namespace Application.UseCases.Employees;

/// <summary>
/// Atualiza dados cadastrais, função principal, matriz de polivalência e restrições individuais de um colaborador.
/// </summary>
public sealed class UpdateEmployeeUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IResultTelemetry? _telemetry;

    public UpdateEmployeeUseCase(
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
        UpdateEmployeeCommand command,
        CancellationToken ct = default)
        => ApplicationTelemetry.ObserveAsync(
            "UpdateEmployee",
            _currentUserService.TenantId,
            _telemetry,
            () => ExecuteCoreAsync(command, ct));

    private async Task<Result<EmployeeDto>> ExecuteCoreAsync(
        UpdateEmployeeCommand command,
        CancellationToken ct)
    {
        if (command is null)
        {
            return Result.Failure<EmployeeDto>(
                Error.Validation("Employee.InvalidCommand", "Comando de atualização de colaborador inválido."));
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

        var (tenantId, company) = contextResult.Value;

        var employee = await _employeeRepository.GetByIdAsync(command.EmployeeId, ct);
        if (employee is null || employee.CompanyId != company.Id)
        {
            return Result.Failure<EmployeeDto>(
                Error.NotFound("Employee.NotFound", "Colaborador não encontrado para o estabelecimento atual."));
        }

        var skillsResult = EmployeeMapper.ToDomainSecondarySkills(command.SecondarySkills);
        if (skillsResult.IsFailure)
        {
            return Result.Failure<EmployeeDto>(skillsResult.Error);
        }

        // Valida todas as invariantes antes de aplicar mutações na instância rastreada.
        var validationCandidate = Employee.Create(
            tenantId,
            company.Id,
            command.Name,
            command.PrimaryRole,
            skillsResult.Value,
            command.PreferredDaysOff,
            command.UnavailableDays);

        if (validationCandidate.IsFailure)
        {
            return Result.Failure<EmployeeDto>(validationCandidate.Error);
        }

        var nameResult = employee.UpdateName(command.Name);
        if (nameResult.IsFailure)
        {
            return Result.Failure<EmployeeDto>(nameResult.Error);
        }

        var roleResult = employee.UpdatePrimaryRole(command.PrimaryRole);
        if (roleResult.IsFailure)
        {
            return Result.Failure<EmployeeDto>(roleResult.Error);
        }

        var desiredRoles = skillsResult.Value.Select(skill => skill.Role).ToHashSet();
        var rolesToRemove = employee.SecondarySkills
            .Select(skill => skill.Role)
            .Where(role => !desiredRoles.Contains(role))
            .ToList();

        foreach (var roleToRemove in rolesToRemove)
        {
            var removeResult = employee.RemoveSecondarySkill(roleToRemove);
            if (removeResult.IsFailure)
            {
                return Result.Failure<EmployeeDto>(removeResult.Error);
            }
        }

        foreach (var skill in skillsResult.Value)
        {
            var skillResult = employee.AddOrUpdateSecondarySkill(skill.Role, skill.Proficiency);
            if (skillResult.IsFailure)
            {
                return Result.Failure<EmployeeDto>(skillResult.Error);
            }
        }

        var availabilityResult = employee.UpdateAvailability(command.PreferredDaysOff, command.UnavailableDays);
        if (availabilityResult.IsFailure)
        {
            return Result.Failure<EmployeeDto>(availabilityResult.Error);
        }

        _employeeRepository.Update(employee);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(EmployeeMapper.ToDto(employee));
    }
}

