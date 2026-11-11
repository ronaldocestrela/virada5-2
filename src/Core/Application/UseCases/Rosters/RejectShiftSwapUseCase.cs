using Application.Common.Interfaces;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso para recusa pelo gestor ou cancelamento pelo colaborador solicitante de uma troca pendente.
/// </summary>
public sealed class RejectShiftSwapUseCase
{
    private readonly IShiftSwapRequestRepository _shiftSwapRequestRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;

    public RejectShiftSwapUseCase(
        IShiftSwapRequestRepository shiftSwapRequestRepository,
        IRosterRepository rosterRepository,
        IEmployeeRepository employeeRepository,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(shiftSwapRequestRepository);
        ArgumentNullException.ThrowIfNull(rosterRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _shiftSwapRequestRepository = shiftSwapRequestRepository;
        _rosterRepository = rosterRepository;
        _employeeRepository = employeeRepository;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ShiftSwapRequestDto>> ExecuteAsync(
        RejectShiftSwapCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
        {
            return Result.Failure<ShiftSwapRequestDto>(
                Error.Validation("ShiftSwap.InvalidCommand", "Comando de recusa ou cancelamento inválido."));
        }

        if (_currentUserService.TenantId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapRequestDto>(
                Error.Validation("ShiftSwap.EmptyTenant", "O identificador do tenant autenticado é obrigatório."));
        }

        if (command.ShiftSwapRequestId == Guid.Empty)
        {
            return Result.Failure<ShiftSwapRequestDto>(
                Error.Validation("ShiftSwap.InvalidRequestId", "O identificador da solicitação de troca é obrigatório."));
        }

        var swapRequest = await _shiftSwapRequestRepository.GetByIdAsync(command.ShiftSwapRequestId, ct);
        if (swapRequest is null)
        {
            return Result.Failure<ShiftSwapRequestDto>(
                Error.NotFound("ShiftSwap.NotFound", "Solicitação de troca não encontrada para o tenant atual."));
        }

        var transitionResult = command.IsCancellationByRequester
            ? swapRequest.Cancel(command.ActingEmployeeId ?? Guid.Empty, command.Notes)
            : swapRequest.RejectByManager(_currentUserService.UserId, command.Notes);

        if (transitionResult.IsFailure)
        {
            return Result.Failure<ShiftSwapRequestDto>(transitionResult.Error);
        }

        _shiftSwapRequestRepository.Update(swapRequest);
        await _unitOfWork.SaveChangesAsync(ct);

        var dto = await ShiftSwapValidationHelper.LoadAndMapDtoAsync(
            swapRequest,
            _rosterRepository,
            _employeeRepository,
            ct);

        return Result.Success(dto);
    }
}
