using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Enums;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso para aprovação pelo gestor de uma solicitação de troca de turno,
/// revalidando integralmente as regras CLT antes de efetivar a troca atômica na escala.
/// </summary>
public sealed class ApproveShiftSwapUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly IShiftSwapRequestRepository _shiftSwapRequestRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IShiftSwapNotificationService? _notificationService;

    public ApproveShiftSwapUseCase(
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IDemandSlotRepository demandSlotRepository,
        IRosterRepository rosterRepository,
        IShiftSwapRequestRepository shiftSwapRequestRepository,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IShiftSwapNotificationService? notificationService = null)
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
    }

    public async Task<Result<ShiftSwapActionResultDto>> ExecuteAsync(
        ApproveShiftSwapCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Validation("ShiftSwap.InvalidCommand", "Comando de aprovação de troca inválido."));
        }

        if (_currentUserService.TenantId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Validation("ShiftSwap.EmptyTenant", "O identificador do tenant autenticado é obrigatório."));
        }

        if (command.ShiftSwapRequestId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Validation("ShiftSwap.InvalidRequestId", "O identificador da solicitação de troca é obrigatório."));
        }

        var swapRequest = await _shiftSwapRequestRepository.GetByIdAsync(command.ShiftSwapRequestId, ct);
        if (swapRequest is null)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.NotFound("ShiftSwap.NotFound", "Solicitação de troca não encontrada para o tenant atual."));
        }

        if (swapRequest.Status != ShiftSwapStatus.PendingManagerApproval)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Conflict(
                    "ShiftSwapRequest.NotPendingManagerApproval",
                    "A solicitação precisa estar pendente de aprovação do gestor para ser aprovada."));
        }

        var validationResult = await ShiftSwapValidationHelper.ValidateAsync(
            swapRequest.RosterId,
            swapRequest.RequesterEmployeeId,
            swapRequest.RequesterShiftId,
            swapRequest.TargetEmployeeId,
            swapRequest.TargetShiftId,
            swapRequest.CompanyId,
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

        var approvalTransitionResult = swapRequest.Approve(_currentUserService.UserId, command.Notes);
        if (approvalTransitionResult.IsFailure)
        {
            return Result.Failure<ShiftSwapActionResultDto>(approvalTransitionResult.Error);
        }

        var mutationResult = context.TargetShift is not null
            ? context.Roster.SwapShiftEmployees(
                context.RequesterShift.Id,
                context.TargetEmployee,
                context.TargetShift.Id,
                context.RequesterEmployee,
                context.RequesterShiftDemandLevel,
                context.TargetShiftDemandLevel!.Value,
                context.TargetBoundaryContext,
                context.RequesterBoundaryContext)
            : context.Roster.ReassignShiftEmployee(
                context.RequesterShift.Id,
                context.TargetEmployee,
                context.RequesterShiftDemandLevel,
                context.TargetBoundaryContext);

        if (mutationResult.IsFailure)
        {
            return Result.Failure<ShiftSwapActionResultDto>(mutationResult.Error);
        }

        _rosterRepository.Update(context.Roster);
        _shiftSwapRequestRepository.Update(swapRequest);
        await _unitOfWork.SaveChangesAsync(ct);
        _notificationService?.NotifyTenantSwapsChanged(_currentUserService.TenantId);

        return Result.Success(ShiftSwapValidationHelper.BuildActionResult(swapRequest, context));
    }
}

