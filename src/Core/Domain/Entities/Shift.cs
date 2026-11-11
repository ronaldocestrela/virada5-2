using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// Representa um turno de trabalho alocado a um colaborador dentro de uma escala semanal (<see cref="Roster"/>).
/// </summary>
public sealed class Shift : BaseEntity, ITenantEntity
{
    public const int MinInterShiftRestHours = 11;
    public const int MaxShiftDurationHours = 12;

    public static readonly TimeSpan MinimumInterShiftRest = TimeSpan.FromHours(MinInterShiftRestHours);
    public static readonly TimeSpan MaxShiftDuration = TimeSpan.FromHours(MaxShiftDurationHours);

    private Shift()
    {
    }

    private Shift(
        Guid tenantId,
        Guid rosterId,
        Guid employeeId,
        DateOnly date,
        TimeOnly startTime,
        TimeOnly endTime,
        StationRole assignedRole,
        CoverageOrigin coverageOrigin)
    {
        TenantId = tenantId;
        RosterId = rosterId;
        EmployeeId = employeeId;
        Date = date;
        StartTime = startTime;
        EndTime = endTime;
        AssignedRole = assignedRole;
        CoverageOrigin = coverageOrigin;
    }

    public Guid TenantId { get; set; }
    public Guid RosterId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly Date { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public StationRole AssignedRole { get; private set; }
    public CoverageOrigin CoverageOrigin { get; private set; }

    public bool CrossesMidnight => EndTime < StartTime;
    public DateTime StartDateTime => Date.ToDateTime(StartTime);
    public DateTime EndDateTime => (CrossesMidnight ? Date.AddDays(1) : Date).ToDateTime(EndTime);
    public TimeSpan Duration => EndDateTime - StartDateTime;

    /// <summary>
    /// Cria uma nova instância válida de <see cref="Shift"/>.
    /// </summary>
    public static Result<Shift> Create(
        Guid tenantId,
        Guid rosterId,
        Guid employeeId,
        DateOnly date,
        TimeOnly startTime,
        TimeOnly endTime,
        StationRole assignedRole,
        CoverageOrigin coverageOrigin)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<Shift>(
                Error.Validation("Shift.EmptyTenantId", "O identificador do tenant é obrigatório."));
        }

        if (rosterId == Guid.Empty)
        {
            return Result.Failure<Shift>(
                Error.Validation("Shift.EmptyRosterId", "O identificador da escala é obrigatório."));
        }

        if (employeeId == Guid.Empty)
        {
            return Result.Failure<Shift>(
                Error.Validation("Shift.EmptyEmployeeId", "O identificador do colaborador é obrigatório."));
        }

        if (date == default)
        {
            return Result.Failure<Shift>(
                Error.Validation("Shift.InvalidDate", "A data do turno é obrigatória."));
        }

        if (startTime == endTime)
        {
            return Result.Failure<Shift>(
                Error.Validation("Shift.InvalidTimeRange", "O horário de início não pode ser igual ao horário de término."));
        }

        if (!Enum.IsDefined(assignedRole))
        {
            return Result.Failure<Shift>(
                Error.Validation("Shift.InvalidStationRole", "Praça de atuação inválida."));
        }

        if (!Enum.IsDefined(coverageOrigin))
        {
            return Result.Failure<Shift>(
                Error.Validation("Shift.InvalidCoverageOrigin", "Origem de cobertura inválida."));
        }

        var shift = new Shift(
            tenantId,
            rosterId,
            employeeId,
            date,
            startTime,
            endTime,
            assignedRole,
            coverageOrigin);

        if (shift.Duration > MaxShiftDuration)
        {
            return Result.Failure<Shift>(
                Error.Validation(
                    "Shift.DurationExceedsLimit",
                    $"A duração do turno não pode exceder {MaxShiftDurationHours} horas."));
        }

        return Result.Success(shift);
    }

    /// <summary>
    /// Indica se o intervalo deste turno se sobrepõe ao de outro turno.
    /// </summary>
    public bool OverlapsWith(Shift other) =>
        StartDateTime < other.EndDateTime && other.StartDateTime < EndDateTime;

    /// <summary>
    /// Verifica se há ao menos 11 horas consecutivas de descanso interjornada entre dois turnos.
    /// </summary>
    public bool MeetsInterShiftRestWith(Shift other)
    {
        if (OverlapsWith(other))
        {
            return false;
        }

        var restGap = StartDateTime >= other.EndDateTime
            ? StartDateTime - other.EndDateTime
            : other.StartDateTime - EndDateTime;

        return restGap >= MinimumInterShiftRest;
    }

    internal void ReassignEmployee(Guid employeeId, CoverageOrigin coverageOrigin)
    {
        EmployeeId = employeeId;
        CoverageOrigin = coverageOrigin;
    }
}

