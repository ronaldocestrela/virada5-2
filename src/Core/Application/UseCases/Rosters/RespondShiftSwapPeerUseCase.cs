using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Enums;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso para resposta do colaborador destinatário (aceite ou recusa) em uma solicitação de troca de turno.
/// </summary>
public sealed class RespondShiftSwapPeerUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly IShiftSwapRequestRepository _shiftSwapRequestRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;

    public RespondShiftSwapPeerUseCase(
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IDemandSlotRepository demandSlotRepository,
        IRosterRepository rosterRepository,
        IShiftSwapRequestRepository shiftSwapRequestRepository,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork)
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
    }

    public async Task<Result<ShiftSwapActionResultDto>> ExecuteAsync(
        RespondShiftSwapPeerCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Validation("ShiftSwap.InvalidCommand", "Comando de resposta do colega inválido."));
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

        if (!command.Accept)
        {
            var rejectResult = swapRequest.RejectByPeer(command.ActingEmployeeId, command.Notes);
            if (rejectResult.IsFailure)
            {
                return Result.Failure<ShiftSwapActionResultDto>(rejectResult.Error);
            }

            _shiftSwapRequestRepository.Update(swapRequest);
            await _unitOfWork.SaveChangesAsync(ct);

            var rejectedDto = await ShiftSwapValidationHelper.LoadAndMapDtoAsync(
                swapRequest,
                _rosterRepository,
                _employeeRepository,
                ct);

            return Result.Success(new ShiftSwapActionResultDto(
                rejectedDto,
                new ShiftSwapValidationSummaryDto(false, DemandClassification.Idle, null, null, null)));
        }

        if (command.ActingEmployeeId == Guid.Empty || command.ActingEmployeeId != swapRequest.TargetEmployeeId)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Conflict("ShiftSwapRequest.UnauthorizedPeer", "Somente o colaborador destinatário pode responder à solicitação."));
        }

        if (swapRequest.Status != ShiftSwapStatus.PendingPeerApproval)
        {
            return Result.Failure<ShiftSwapActionResultDto>(
                Error.Conflict("ShiftSwapRequest.InvalidStateTransition", "A solicitação não está aguardando confirmação do colega."));
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

        var acceptResult = swapRequest.AcceptByPeer(command.ActingEmployeeId);
        if (acceptResult.IsFailure)
        {
            return Result.Failure<ShiftSwapActionResultDto>(acceptResult.Error);
        }

        _shiftSwapRequestRepository.Update(swapRequest);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ShiftSwapValidationHelper.BuildActionResult(swapRequest, validationResult.Value));
    }
}
