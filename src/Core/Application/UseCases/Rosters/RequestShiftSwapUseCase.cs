using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Entities;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso para solicitação de troca mútua ou cessão de turno entre colaboradores,
/// com pré-validação integral de elegibilidade e regras CLT sem mutação prematura da escala.
/// </summary>
public sealed class RequestShiftSwapUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly IShiftSwapRequestRepository _shiftSwapRequestRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IShiftSwapNotificationService? _notificationService;
    private readonly IResultTelemetry? _telemetry;

    public RequestShiftSwapUseCase(
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IDemandSlotRepository demandSlotRepository,
        IRosterRepository rosterRepository,
        IShiftSwapRequestRepository shiftSwapRequestRepository,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IShiftSwapNotificationService? notificationService = null,
        IResultTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(demandSlotRepository);
        ArgumentNullException.ThrowIfNull(rosterRepository);
        ArgumentNullException.ThrowIfNull(shiftSwapRequestRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _companyRepository = companyRepository;
        _employeeRepository = employeeRepository;
        _demandSlotRepository = demandSlotRepository;
        _rosterRepository = rosterRepository;
        _shiftSwapRequestRepository = shiftSwapRequestRepository;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
        _telemetry = telemetry;
    }

    public Task<Result<ShiftSwapActionResultDto>> ExecuteAsync(
        RequestShiftSwapCommand command,
        CancellationToken ct = default)
        => ApplicationTelemetry.ObserveAsync(
            "RequestShiftSwap",
            _currentUserService.TenantId,
            _telemetry,
            () => ExecuteCoreAsync(command, ct));

    private async Task<Result<ShiftSwapActionResultDto>> ExecuteCoreAsync(
        RequestShiftSwapCommand command,
        CancellationToken ct)
    {
        if (command is null)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Validation("ShiftSwap.InvalidCommand", "Comando de solicitação de troca inválido."));
        }

        if (command.RosterId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Validation("ShiftSwapRequest.EmptyRosterId", "O identificador da escala é obrigatório."));
        }

        if (command.RequesterEmployeeId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Validation("ShiftSwapRequest.EmptyRequesterEmployeeId", "O colaborador solicitante é obrigatório."));
        }

        if (command.RequesterShiftId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Validation("ShiftSwapRequest.EmptyRequesterShiftId", "O turno de origem é obrigatório."));
        }

        if (command.TargetEmployeeId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Validation("ShiftSwapRequest.EmptyTargetEmployeeId", "O colaborador destinatário é obrigatório."));
        }

        if (command.TargetShiftId.HasValue && command.TargetShiftId.Value == Guid.Empty)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Validation("ShiftSwapRequest.EmptyTargetShiftId", "O identificador do turno de destino não pode ser vazio."));
        }

        var requesterShiftBusy = await _shiftSwapRequestRepository.HasActiveRequestForShiftAsync(command.RequesterShiftId, ct);
        if (requesterShiftBusy)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Conflict("ShiftSwap.ActiveRequestExists", "Já existe uma solicitação de troca pendente envolvendo o turno de origem."));
        }

        if (command.TargetShiftId.HasValue)
        {
            var targetShiftBusy = await _shiftSwapRequestRepository.HasActiveRequestForShiftAsync(command.TargetShiftId.Value, ct);
            if (targetShiftBusy)
            {
                return Result.Failure<ShiftSwapActionResultDto>(
                    Error.Conflict("ShiftSwap.ActiveRequestExists", "Já existe uma solicitação de troca pendente envolvendo o turno de destino."));
            }
        }

        var validationResult = await ShiftSwapValidationHelper.ValidateAsync(
            command.RosterId,
            command.RequesterEmployeeId,
            command.RequesterShiftId,
            command.TargetEmployeeId,
            command.TargetShiftId,
            command.CompanyId,
            _companyRepository,
            _employeeRepository,
            _demandSlotRepository,
            _rosterRepository,
            _currentUserService,
            ct);

        if (validationResult.IsFailure)
        {
            return Result.Failure<ShiftSwapActionResultDto>(validationResult.Error);
        }

        var context = validationResult.Value;

        if (!command.TargetShiftId.HasValue)
        {
            var rosterSwaps = await _shiftSwapRequestRepository.GetByRosterAsync(context.Roster.Id, ct);
            var pendingCoverageCheck = ShiftSwapValidationHelper.ValidatePendingOneWayCoverageConflict(
                context.Roster,
                rosterSwaps,
                context.TargetEmployee.Id,
                context.RequesterShift.Date);

            if (pendingCoverageCheck.IsFailure)
            {
                return Result.Failure<ShiftSwapActionResultDto>(pendingCoverageCheck.Error);
            }
        }

        var requestResult = ShiftSwapRequest.Create(
            _currentUserService.TenantId,
            context.Company.Id,
            context.Roster.Id,
            context.RequesterEmployee.Id,
            context.RequesterShift.Id,
            context.TargetEmployee.Id,
            context.TargetShift?.Id,
            command.Reason,
            command.RequirePeerApproval);

        if (requestResult.IsFailure)
        {
            return Result.Failure<ShiftSwapActionResultDto>(requestResult.Error);
        }

        await _shiftSwapRequestRepository.AddAsync(requestResult.Value, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        if (!command.RequirePeerApproval)
        {
            _notificationService?.NotifyTenantSwapsChanged(_currentUserService.TenantId);
        }

        return Result.Success(ShiftSwapValidationHelper.BuildActionResult(requestResult.Value, context));
    }
}

