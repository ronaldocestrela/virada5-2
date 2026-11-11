using Domain.Common;

namespace Domain.ValueObjects;

/// <summary>
/// Contexto histórico e limítrofe opcional de um colaborador para validação de regras CLT entre semanas adjacentes.
/// </summary>
public readonly record struct EmployeeWeekBoundaryContext
{
    private EmployeeWeekBoundaryContext(
        Guid employeeId,
        int consecutiveSundaysWorkedBeforeWeek,
        DateTime? previousWeekLastShiftEnd,
        DateTime? nextWeekFirstShiftStart)
    {
        EmployeeId = employeeId;
        ConsecutiveSundaysWorkedBeforeWeek = consecutiveSundaysWorkedBeforeWeek;
        PreviousWeekLastShiftEnd = previousWeekLastShiftEnd;
        NextWeekFirstShiftStart = nextWeekFirstShiftStart;
    }

    public Guid EmployeeId { get; }
    public int ConsecutiveSundaysWorkedBeforeWeek { get; }
    public DateTime? PreviousWeekLastShiftEnd { get; }
    public DateTime? NextWeekFirstShiftStart { get; }

    /// <summary>
    /// Cria uma instância válida de <see cref="EmployeeWeekBoundaryContext"/>.
    /// </summary>
    public static Result<EmployeeWeekBoundaryContext> Create(
        Guid employeeId,
        int consecutiveSundaysWorkedBeforeWeek = 0,
        DateTime? previousWeekLastShiftEnd = null,
        DateTime? nextWeekFirstShiftStart = null)
    {
        if (employeeId == Guid.Empty)
        {
            return Result.Failure<EmployeeWeekBoundaryContext>(
                Error.Validation("EmployeeWeekBoundaryContext.EmptyEmployeeId", "O identificador do colaborador é obrigatório."));
        }

        if (consecutiveSundaysWorkedBeforeWeek < 0)
        {
            return Result.Failure<EmployeeWeekBoundaryContext>(
                Error.Validation(
                    "EmployeeWeekBoundaryContext.NegativeSundayStreak",
                    "A contagem de domingos consecutivos anteriores não pode ser negativa."));
        }

        if (previousWeekLastShiftEnd.HasValue &&
            nextWeekFirstShiftStart.HasValue &&
            previousWeekLastShiftEnd.Value >= nextWeekFirstShiftStart.Value)
        {
            return Result.Failure<EmployeeWeekBoundaryContext>(
                Error.Validation(
                    "EmployeeWeekBoundaryContext.InvalidBoundaryWindow",
                    "O término do turno anterior deve preceder o início do próximo turno."));
        }

        return Result.Success(new EmployeeWeekBoundaryContext(
            employeeId,
            consecutiveSundaysWorkedBeforeWeek,
            previousWeekLastShiftEnd,
            nextWeekFirstShiftStart));
    }
}

