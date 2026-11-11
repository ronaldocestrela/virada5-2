using Domain.Common;
using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Raiz de agregação que representa a escala semanal (ciclo 5x2 de 7 dias) e garante a conformidade CLT dos turnos.
/// </summary>
public sealed class Roster : AggregateRoot, ITenantEntity
{
    public const int CycleLengthDays = 7;
    public const int MaxWorkDaysPerWeek = 5;

    private readonly List<Shift> _shifts = [];

    private Roster()
    {
    }

    private Roster(
        Guid tenantId,
        Guid companyId,
        DateOnly weekStartDate,
        SundayRotationPolicy sundayPolicy)
    {
        TenantId = tenantId;
        CompanyId = companyId;
        WeekStartDate = weekStartDate;
        WeekEndDate = weekStartDate.AddDays(CycleLengthDays - 1);
        Status = RosterStatus.Draft;
        SundayPolicy = sundayPolicy;
    }

    public Guid TenantId { get; set; }
    public Guid CompanyId { get; private set; }
    public DateOnly WeekStartDate { get; private set; }
    public DateOnly WeekEndDate { get; private set; }
    public RosterStatus Status { get; private set; }
    public SundayRotationPolicy SundayPolicy { get; private set; }
    public IReadOnlyCollection<Shift> Shifts => _shifts.AsReadOnly();

    /// <summary>
    /// Cria uma nova escala semanal em rascunho para um estabelecimento.
    /// </summary>
    public static Result<Roster> Create(
        Guid tenantId,
        Guid companyId,
        DateOnly weekStartDate,
        SundayRotationPolicy? sundayPolicy = null)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<Roster>(
                Error.Validation("Roster.EmptyTenantId", "O identificador do tenant é obrigatório."));
        }

        if (companyId == Guid.Empty)
        {
            return Result.Failure<Roster>(
                Error.Validation("Roster.EmptyCompanyId", "O identificador do estabelecimento é obrigatório."));
        }

        if (weekStartDate == default)
        {
            return Result.Failure<Roster>(
                Error.Validation("Roster.InvalidWeekStartDate", "A data inicial da semana da escala é obrigatória."));
        }

        var resolvedPolicy = sundayPolicy ?? SundayRotationPolicy.ThreeWeekCycle;
        if (resolvedPolicy.MaxConsecutiveSundaysWorked < SundayRotationPolicy.MinConsecutiveSundays ||
            resolvedPolicy.MaxConsecutiveSundaysWorked > SundayRotationPolicy.MaxConsecutiveSundaysLimit)
        {
            return Result.Failure<Roster>(
                Error.Validation("Roster.InvalidSundayPolicy", "Política de revezamento dominical inválida."));
        }

        return Result.Success(new Roster(tenantId, companyId, weekStartDate, resolvedPolicy));
    }

    /// <summary>
    /// Indica se a data informada pertence ao ciclo de 7 dias desta escala.
    /// </summary>
    public bool ContainsDate(DateOnly date) =>
        date >= WeekStartDate && date <= WeekEndDate;

    /// <summary>
    /// Retorna a quantidade de dias distintos trabalhados pelo colaborador nesta escala.
    /// </summary>
    public int GetEmployeeWorkedDaysCount(Guid employeeId) =>
        _shifts
            .Where(shift => shift.EmployeeId == employeeId)
            .Select(shift => shift.Date)
            .Distinct()
            .Count();

    /// <summary>
    /// Retorna os dias de folga do colaborador dentro do ciclo semanal desta escala.
    /// </summary>
    public IReadOnlyList<DateOnly> GetEmployeeDaysOff(Guid employeeId)
    {
        var workedDates = _shifts
            .Where(shift => shift.EmployeeId == employeeId)
            .Select(shift => shift.Date)
            .ToHashSet();

        var daysOff = new List<DateOnly>(CycleLengthDays);
        for (var offset = 0; offset < CycleLengthDays; offset++)
        {
            var currentDate = WeekStartDate.AddDays(offset);
            if (!workedDates.Contains(currentDate))
            {
                daysOff.Add(currentDate);
            }
        }

        return daysOff.AsReadOnly();
    }

    /// <summary>
    /// Aloca um novo turno na escala em rascunho validando elegibilidade, polivalência e regras CLT.
    /// </summary>
    public Result<Shift> AssignShift(
        Employee employee,
        DateOnly date,
        TimeOnly startTime,
        TimeOnly endTime,
        StationRole assignedRole,
        DemandClassification demandLevel = DemandClassification.Moderate,
        EmployeeWeekBoundaryContext? boundaryContext = null)
    {
        if (Status != RosterStatus.Draft)
        {
            return Result.Failure<Shift>(
                Error.Conflict("Roster.NotEditable", "Somente escalas em rascunho podem receber novos turnos."));
        }

        if (!ContainsDate(date))
        {
            return Result.Failure<Shift>(
                Error.Validation("Roster.DateOutOfWeekRange", "A data do turno está fora do ciclo semanal desta escala."));
        }

        var eligibilityResult = ValidateEmployeeForShift(
            employee,
            date,
            assignedRole,
            demandLevel,
            boundaryContext);

        if (eligibilityResult.IsFailure)
        {
            return Result.Failure<Shift>(eligibilityResult.Error);
        }

        var shiftResult = Shift.Create(
            TenantId,
            Id,
            employee.Id,
            date,
            startTime,
            endTime,
            assignedRole,
            eligibilityResult.Value);

        if (shiftResult.IsFailure)
        {
            return shiftResult;
        }

        var employeeExistingShifts = _shifts
            .Where(shift => shift.EmployeeId == employee.Id)
            .ToList();

        var cltResult = ValidateShiftCltPlacement(
            shiftResult.Value,
            employeeExistingShifts,
            boundaryContext);

        if (cltResult.IsFailure)
        {
            return Result.Failure<Shift>(cltResult.Error);
        }

        _shifts.Add(shiftResult.Value);
        return shiftResult;
    }

    /// <summary>
    /// Remove um turno de uma escala em rascunho.
    /// </summary>
    public Result RemoveShift(Guid shiftId)
    {
        if (Status != RosterStatus.Draft)
        {
            return Result.Failure(
                Error.Conflict("Roster.NotEditable", "Somente escalas em rascunho podem remover turnos."));
        }

        var shift = _shifts.FirstOrDefault(s => s.Id == shiftId);
        if (shift is null)
        {
            return Result.Failure(
                Error.NotFound("Roster.ShiftNotFound", "Turno não encontrado na escala."));
        }

        _shifts.Remove(shift);
        return Result.Success();
    }

    /// <summary>
    /// Realiza a troca atômica de colaboradores entre dois turnos da escala validando elegibilidade e regras CLT.
    /// </summary>
    public Result SwapShiftEmployees(
        Guid firstShiftId,
        Employee firstShiftReplacement,
        Guid secondShiftId,
        Employee secondShiftReplacement,
        DemandClassification demandLevel = DemandClassification.Moderate,
        EmployeeWeekBoundaryContext? firstReplacementContext = null,
        EmployeeWeekBoundaryContext? secondReplacementContext = null)
    {
        if (Status == RosterStatus.Archived)
        {
            return Result.Failure(
                Error.Conflict("Roster.ArchivedNotModifiable", "Escalas arquivadas não permitem troca de turnos."));
        }

        if (firstShiftId == secondShiftId)
        {
            return Result.Failure(
                Error.Validation("Roster.SameShiftSwap", "Informe dois turnos distintos para realizar a troca."));
        }

        var firstShift = _shifts.FirstOrDefault(s => s.Id == firstShiftId);
        var secondShift = _shifts.FirstOrDefault(s => s.Id == secondShiftId);
        if (firstShift is null || secondShift is null)
        {
            return Result.Failure(
                Error.NotFound("Roster.ShiftNotFound", "Um ou mais turnos informados para troca não foram encontrados."));
        }

        var firstEligibility = ValidateEmployeeForShift(
            firstShiftReplacement,
            firstShift.Date,
            firstShift.AssignedRole,
            demandLevel,
            firstReplacementContext);

        if (firstEligibility.IsFailure)
        {
            return Result.Failure(firstEligibility.Error);
        }

        var secondEligibility = ValidateEmployeeForShift(
            secondShiftReplacement,
            secondShift.Date,
            secondShift.AssignedRole,
            demandLevel,
            secondReplacementContext);

        if (secondEligibility.IsFailure)
        {
            return Result.Failure(secondEligibility.Error);
        }

        var firstCandidateResult = Shift.Create(
            TenantId,
            Id,
            firstShiftReplacement.Id,
            firstShift.Date,
            firstShift.StartTime,
            firstShift.EndTime,
            firstShift.AssignedRole,
            firstEligibility.Value);

        if (firstCandidateResult.IsFailure)
        {
            return Result.Failure(firstCandidateResult.Error);
        }

        var secondCandidateResult = Shift.Create(
            TenantId,
            Id,
            secondShiftReplacement.Id,
            secondShift.Date,
            secondShift.StartTime,
            secondShift.EndTime,
            secondShift.AssignedRole,
            secondEligibility.Value);

        if (secondCandidateResult.IsFailure)
        {
            return Result.Failure(secondCandidateResult.Error);
        }

        var firstRemaining = _shifts
            .Where(s => s.Id != firstShift.Id && s.Id != secondShift.Id && s.EmployeeId == firstShiftReplacement.Id)
            .ToList();

        var firstPlacementResult = ValidateShiftCltPlacement(
            firstCandidateResult.Value,
            firstRemaining,
            firstReplacementContext);

        if (firstPlacementResult.IsFailure)
        {
            return firstPlacementResult;
        }

        var secondRemaining = _shifts
            .Where(s => s.Id != firstShift.Id && s.Id != secondShift.Id && s.EmployeeId == secondShiftReplacement.Id)
            .ToList();

        if (firstShiftReplacement.Id == secondShiftReplacement.Id)
        {
            secondRemaining.Add(firstCandidateResult.Value);
        }

        var secondPlacementResult = ValidateShiftCltPlacement(
            secondCandidateResult.Value,
            secondRemaining,
            secondReplacementContext);

        if (secondPlacementResult.IsFailure)
        {
            return secondPlacementResult;
        }

        firstShift.ReassignEmployee(firstShiftReplacement.Id, firstEligibility.Value);
        secondShift.ReassignEmployee(secondShiftReplacement.Id, secondEligibility.Value);
        return Result.Success();
    }

    /// <summary>
    /// Valida a conformidade integral da escala (incluindo fronteiras semanais opcionais) e a publica.
    /// </summary>
    public Result Publish(IEnumerable<EmployeeWeekBoundaryContext>? boundaryContexts = null)
    {
        if (Status != RosterStatus.Draft)
        {
            return Result.Failure(
                Error.Conflict("Roster.NotDraft", "Apenas escalas em rascunho podem ser publicadas."));
        }

        if (_shifts.Count == 0)
        {
            return Result.Failure(
                Error.Validation("Roster.EmptyShifts", "Não é possível publicar uma escala sem turnos alocados."));
        }

        var contextMapResult = BuildBoundaryContextMap(boundaryContexts);
        if (contextMapResult.IsFailure)
        {
            return Result.Failure(contextMapResult.Error);
        }

        var contextMap = contextMapResult.Value;

        foreach (var employeeGroup in _shifts.GroupBy(shift => shift.EmployeeId))
        {
            var employeeShifts = employeeGroup.ToList();
            EmployeeWeekBoundaryContext? context = contextMap.TryGetValue(employeeGroup.Key, out var found)
                ? found
                : null;

            var complianceResult = ValidateEmployeeWeeklyShifts(employeeShifts, context);
            if (complianceResult.IsFailure)
            {
                return complianceResult;
            }
        }

        Status = RosterStatus.Published;
        return Result.Success();
    }

    /// <summary>
    /// Arquiva a escala semanal impedindo alterações futuras.
    /// </summary>
    public Result Archive()
    {
        if (Status == RosterStatus.Archived)
        {
            return Result.Failure(
                Error.Conflict("Roster.AlreadyArchived", "A escala já está arquivada."));
        }

        Status = RosterStatus.Archived;
        return Result.Success();
    }

    /// <summary>
    /// Valida o revezamento dominical periódico de um colaborador através de uma sequência cronológica de escalas.
    /// </summary>
    public static Result ValidateMultiWeekSundayRotation(
        IReadOnlyList<Roster> orderedRosters,
        Guid employeeId,
        int initialConsecutiveSundaysWorked = 0)
    {
        if (orderedRosters is null || orderedRosters.Count == 0)
        {
            return Result.Failure(
                Error.Validation("Roster.EmptyRosterSequence", "Informe ao menos uma escala para validação dominical."));
        }

        if (employeeId == Guid.Empty)
        {
            return Result.Failure(
                Error.Validation("Roster.EmptyEmployeeId", "O identificador do colaborador é obrigatório."));
        }

        if (initialConsecutiveSundaysWorked < 0)
        {
            return Result.Failure(
                Error.Validation("Roster.NegativeSundayStreak", "A contagem inicial de domingos consecutivos não pode ser negativa."));
        }

        var streak = initialConsecutiveSundaysWorked;
        Roster? previousRoster = null;

        foreach (var roster in orderedRosters)
        {
            if (previousRoster is not null)
            {
                if (roster.TenantId != previousRoster.TenantId || roster.CompanyId != previousRoster.CompanyId)
                {
                    return Result.Failure(
                        Error.Validation("Roster.SequenceCompanyMismatch", "Todas as escalas da sequência devem pertencer ao mesmo estabelecimento."));
                }

                if (roster.WeekStartDate <= previousRoster.WeekEndDate)
                {
                    return Result.Failure(
                        Error.Validation("Roster.InvalidSequenceOrder", "As escalas devem estar ordenadas cronologicamente sem sobreposição."));
                }

                if (roster.WeekStartDate > previousRoster.WeekEndDate.AddDays(1))
                {
                    streak = 0;
                }
            }

            var workedSunday = roster.Shifts.Any(s =>
                s.EmployeeId == employeeId &&
                s.Date.DayOfWeek == DayOfWeek.Sunday);

            if (workedSunday)
            {
                if (!roster.SundayPolicy.CanWorkSunday(streak))
                {
                    return Result.Failure(
                        Error.Conflict(
                            "Roster.SundayRotationViolated",
                            "O colaborador excedeu o limite periódico de domingos consecutivos trabalhados."));
                }

                streak++;
            }
            else
            {
                streak = 0;
            }

            previousRoster = roster;
        }

        return Result.Success();
    }

    private Result<CoverageOrigin> ValidateEmployeeForShift(
        Employee employee,
        DateOnly date,
        StationRole assignedRole,
        DemandClassification demandLevel,
        EmployeeWeekBoundaryContext? boundaryContext)
    {
        if (employee is null)
        {
            return Result.Failure<CoverageOrigin>(
                Error.Validation("Roster.NullEmployee", "O colaborador é obrigatório."));
        }

        if (employee.TenantId != TenantId || employee.CompanyId != CompanyId)
        {
            return Result.Failure<CoverageOrigin>(
                Error.Conflict("Roster.EmployeeCompanyMismatch", "O colaborador não pertence ao estabelecimento desta escala."));
        }

        if (boundaryContext.HasValue && boundaryContext.Value.EmployeeId != employee.Id)
        {
            return Result.Failure<CoverageOrigin>(
                Error.Validation(
                    "Roster.BoundaryContextMismatch",
                    "O contexto semanal informado pertence a outro colaborador."));
        }

        return employee.EvaluateCoverageEligibility(assignedRole, demandLevel, date.DayOfWeek);
    }

    private Result ValidateShiftCltPlacement(
        Shift candidateShift,
        IReadOnlyCollection<Shift> existingEmployeeShifts,
        EmployeeWeekBoundaryContext? boundaryContext)
    {
        foreach (var existingShift in existingEmployeeShifts)
        {
            var pairResult = ValidateShiftPairRest(candidateShift, existingShift);
            if (pairResult.IsFailure)
            {
                return pairResult;
            }
        }

        var singleShiftBoundaryResult = ValidateSingleShiftBoundaries(candidateShift, boundaryContext);
        if (singleShiftBoundaryResult.IsFailure)
        {
            return singleShiftBoundaryResult;
        }

        return ValidateMaxWorkedDays(
            existingEmployeeShifts.Select(shift => shift.Date).Append(candidateShift.Date));
    }

    private Result ValidateEmployeeWeeklyShifts(
        IReadOnlyList<Shift> employeeShifts,
        EmployeeWeekBoundaryContext? boundaryContext)
    {
        var daysResult = ValidateMaxWorkedDays(employeeShifts.Select(s => s.Date));
        if (daysResult.IsFailure)
        {
            return daysResult;
        }

        var ordered = employeeShifts.OrderBy(s => s.StartDateTime).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var current = ordered[i];
            var boundaryResult = ValidateSingleShiftBoundaries(current, boundaryContext);
            if (boundaryResult.IsFailure)
            {
                return boundaryResult;
            }

            if (i > 0)
            {
                var pairResult = ValidateShiftPairRest(ordered[i - 1], current);
                if (pairResult.IsFailure)
                {
                    return pairResult;
                }
            }
        }

        return Result.Success();
    }

    private Result ValidateSingleShiftBoundaries(Shift shift, EmployeeWeekBoundaryContext? boundaryContext)
    {
        var boundaryRestResult = ValidateBoundaryRest(shift, boundaryContext);
        if (boundaryRestResult.IsFailure)
        {
            return boundaryRestResult;
        }

        if (shift.Date.DayOfWeek == DayOfWeek.Sunday &&
            boundaryContext.HasValue &&
            !SundayPolicy.CanWorkSunday(boundaryContext.Value.ConsecutiveSundaysWorkedBeforeWeek))
        {
            return Result.Failure(
                Error.Conflict(
                    "Roster.SundayRotationViolated",
                    "O colaborador excedeu o limite periódico de domingos consecutivos trabalhados."));
        }

        return Result.Success();
    }

    private static Result ValidateShiftPairRest(Shift first, Shift second)
    {
        if (first.OverlapsWith(second))
        {
            return Result.Failure(
                Error.Conflict("Roster.ShiftOverlap", "O colaborador possui turnos com horário sobreposto."));
        }

        if (!first.MeetsInterShiftRestWith(second))
        {
            return Result.Failure(
                Error.Conflict(
                    "Roster.InsufficientInterShiftRest",
                    $"O intervalo mínimo interjornada de {Shift.MinInterShiftRestHours} horas consecutivas não foi respeitado."));
        }

        return Result.Success();
    }

    private static Result ValidateMaxWorkedDays(IEnumerable<DateOnly> workedDates)
    {
        if (workedDates.Distinct().Count() > MaxWorkDaysPerWeek)
        {
            return Result.Failure(
                Error.Conflict(
                    "Roster.WeeklyRestDaysViolated",
                    $"Na escala 5x2 o colaborador deve ter ao menos 2 folgas semanais (máximo de {MaxWorkDaysPerWeek} dias trabalhados)."));
        }

        return Result.Success();
    }

    private static Result ValidateBoundaryRest(Shift shift, EmployeeWeekBoundaryContext? boundaryContext)
    {
        if (!boundaryContext.HasValue)
        {
            return Result.Success();
        }

        var context = boundaryContext.Value;

        if (context.PreviousWeekLastShiftEnd.HasValue)
        {
            var gapFromPrevious = shift.StartDateTime - context.PreviousWeekLastShiftEnd.Value;
            if (gapFromPrevious < Shift.MinimumInterShiftRest)
            {
                return Result.Failure(
                    Error.Conflict(
                        "Roster.InsufficientInterShiftRest",
                        $"O intervalo mínimo interjornada de {Shift.MinInterShiftRestHours} horas desde o último turno da semana anterior não foi respeitado."));
            }
        }

        if (context.NextWeekFirstShiftStart.HasValue)
        {
            var gapToNext = context.NextWeekFirstShiftStart.Value - shift.EndDateTime;
            if (gapToNext < Shift.MinimumInterShiftRest)
            {
                return Result.Failure(
                    Error.Conflict(
                        "Roster.InsufficientInterShiftRest",
                        $"O intervalo mínimo interjornada de {Shift.MinInterShiftRestHours} horas até o primeiro turno da próxima semana não foi respeitado."));
            }
        }

        return Result.Success();
    }

    private static Result<Dictionary<Guid, EmployeeWeekBoundaryContext>> BuildBoundaryContextMap(
        IEnumerable<EmployeeWeekBoundaryContext>? boundaryContexts)
    {
        var map = new Dictionary<Guid, EmployeeWeekBoundaryContext>();
        if (boundaryContexts is null)
        {
            return Result.Success(map);
        }

        foreach (var context in boundaryContexts)
        {
            if (context.EmployeeId == Guid.Empty)
            {
                return Result.Failure<Dictionary<Guid, EmployeeWeekBoundaryContext>>(
                    Error.Validation("Roster.EmptyEmployeeId", "O contexto semanal possui identificador de colaborador vazio."));
            }

            if (!map.TryAdd(context.EmployeeId, context))
            {
                return Result.Failure<Dictionary<Guid, EmployeeWeekBoundaryContext>>(
                    Error.Validation("Roster.DuplicateBoundaryContext", "Contexto semanal duplicado para o mesmo colaborador."));
            }
        }

        return Result.Success(map);
    }
}
