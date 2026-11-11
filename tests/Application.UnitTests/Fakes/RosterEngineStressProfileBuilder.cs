using Application.DTOs.Rosters;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Application.UnitTests.Fakes;

internal sealed record RosterStressScenario(
    Guid TenantId,
    Company Company,
    IReadOnlyList<Employee> Employees,
    IReadOnlyList<DemandSlot> DemandSlots,
    IReadOnlyList<Roster> AdjacentRosters,
    DateOnly TargetWeekStartDate,
    IReadOnlyList<ShiftTemplateInputDto> Templates,
    IReadOnlyList<StationStaffingRuleDto> StaffingRules,
    IReadOnlySet<Guid> SundayCappedEmployeeIds);

internal static class RosterEngineStressProfileBuilder
{
    private static readonly StationRole[] Roles =
    [
        StationRole.Kitchen,
        StationRole.KitchenAssistant,
        StationRole.Waiter,
        StationRole.Bar,
        StationRole.Cashier
    ];

    private static readonly DayOfWeek[] DaysPool =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

    /// <summary>
    /// Constrói cenário complexo factível (10 a 50 colaboradores) com folgas preferenciais,
    /// indisponibilidades, polivalência autônoma/apoio, operação noturna após meia-noite e fronteiras semanais.
    /// </summary>
    public static RosterStressScenario BuildSolvableComplexScenario(
        int employeeCount,
        ScheduleModel scheduleModel,
        DateOnly targetWeekStartDate)
    {
        var tenantId = CreateDeterministicGuid(1, employeeCount, (int)scheduleModel);
        var closedDays = scheduleModel == ScheduleModel.FixedClosure
            ? new[] { DayOfWeek.Monday }
            : Array.Empty<DayOfWeek>();

        var company = Company.Create(
            tenantId,
            $"Operacao Factivel {employeeCount}",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(1, 0),
            scheduleModel,
            closedDays).Value;

        var employees = BuildEmployees(tenantId, company.Id, employeeCount, includeUnavailableDays: true);
        var demandSlots = BuildWeeklyDemandCurve(tenantId, company.Id, closedDays, new TimeOnly(11, 0), 14);
        var templates = new List<ShiftTemplateInputDto>
        {
            new("Abertura", new TimeOnly(11, 0), new TimeOnly(19, 0)),
            new("Noturno", new TimeOnly(17, 0), new TimeOnly(1, 0))
        };

        var adjacent = BuildHistoryAndLookaheadRosters(
            tenantId,
            company.Id,
            targetWeekStartDate,
            employees,
            closedDays,
            templates,
            previousWeeksCount: 2,
            lookaheadStep: 7);

        // Capacidade máxima semanal = 5 * employeeCount turnos; picos concentrados no serviço noturno de Sex/Sáb/Dom
        var unit = Math.Max(1, employeeCount / 10);
        var staffingRules = new List<StationStaffingRuleDto>
        {
            new(StationRole.Kitchen, unit, unit, Math.Max(1, unit / 2)),
            new(StationRole.KitchenAssistant, unit, Math.Max(1, unit / 2), 1),
            new(StationRole.Waiter, unit, unit, Math.Max(1, unit / 2)),
            new(StationRole.Bar, unit, Math.Max(1, unit / 2), 1),
            new(StationRole.Cashier, Math.Max(1, unit / 2), Math.Max(1, unit / 2), 1)
        };

        return new RosterStressScenario(
            tenantId,
            company,
            employees,
            demandSlots,
            adjacent.Rosters,
            targetWeekStartDate,
            templates,
            staffingRules,
            adjacent.SundayCappedEmployeeIds);
    }

    /// <summary>
    /// Constrói cenário estendido (06:00 às 02:00) com 3 turnos sobrepostos, streak de 2 domingos
    /// e fronteira futura (NextWeekFirstShiftStart) para testar interjornada de 11h nos dois extremos.
    /// </summary>
    public static RosterStressScenario BuildThreeShiftExtendedScenario(
        int employeeCount,
        DateOnly targetWeekStartDate)
    {
        var tenantId = CreateDeterministicGuid(2, employeeCount, 3);
        var company = Company.Create(
            tenantId,
            $"Operacao 3 Turnos {employeeCount}",
            BusinessType.Bar,
            new TimeOnly(6, 0),
            new TimeOnly(2, 0),
            ScheduleModel.RotatingDaysOff,
            Array.Empty<DayOfWeek>()).Value;

        var employees = BuildEmployees(tenantId, company.Id, employeeCount, includeUnavailableDays: true);
        var demandSlots = BuildWeeklyDemandCurve(tenantId, company.Id, Array.Empty<DayOfWeek>(), new TimeOnly(6, 0), 20);
        var templates = new List<ShiftTemplateInputDto>
        {
            new("Manha", new TimeOnly(6, 0), new TimeOnly(14, 0)),
            new("Tarde", new TimeOnly(13, 0), new TimeOnly(21, 0)),
            new("Madrugada", new TimeOnly(18, 0), new TimeOnly(2, 0))
        };

        var adjacent = BuildHistoryAndLookaheadRosters(
            tenantId,
            company.Id,
            targetWeekStartDate,
            employees,
            Array.Empty<DayOfWeek>(),
            templates,
            previousWeeksCount: 2,
            lookaheadStep: 2);

        var unit = Math.Max(1, employeeCount / 10);
        var staffingRules = new List<StationStaffingRuleDto>
        {
            new(StationRole.Kitchen, unit, unit, 1),
            new(StationRole.KitchenAssistant, unit, 1, 1),
            new(StationRole.Waiter, unit, unit, 1),
            new(StationRole.Bar, unit, unit, 1),
            new(StationRole.Cashier, Math.Max(1, unit / 2), 1, 1)
        };

        return new RosterStressScenario(
            tenantId,
            company,
            employees,
            demandSlots,
            adjacent.Rosters,
            targetWeekStartDate,
            templates,
            staffingRules,
            adjacent.SundayCappedEmployeeIds);
    }

    /// <summary>
    /// Constrói cenário supersaturado (demanda maior que a capacidade 5x2) para validar degradação graciosa sem violações CLT.
    /// </summary>
    public static RosterStressScenario BuildOversubscribedConflictScenario(
        int employeeCount,
        ScheduleModel scheduleModel,
        DateOnly targetWeekStartDate)
    {
        var tenantId = CreateDeterministicGuid(3, employeeCount, (int)scheduleModel);
        var closedDays = scheduleModel == ScheduleModel.FixedClosure
            ? new[] { DayOfWeek.Monday }
            : Array.Empty<DayOfWeek>();

        var company = Company.Create(
            tenantId,
            $"Operacao Stress {employeeCount}",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(1, 0),
            scheduleModel,
            closedDays).Value;

        var employees = BuildEmployees(tenantId, company.Id, employeeCount, includeUnavailableDays: true);
        var demandSlots = BuildWeeklyDemandCurve(tenantId, company.Id, closedDays, new TimeOnly(11, 0), 14);
        var templates = new List<ShiftTemplateInputDto>
        {
            new("Abertura", new TimeOnly(11, 0), new TimeOnly(19, 0)),
            new("Noturno", new TimeOnly(17, 0), new TimeOnly(1, 0))
        };

        var adjacent = BuildHistoryAndLookaheadRosters(
            tenantId,
            company.Id,
            targetWeekStartDate,
            employees,
            closedDays,
            templates,
            previousWeeksCount: 1,
            lookaheadStep: 0);

        var scaleFactor = Math.Max(1, employeeCount / 10);
        var staffingRules = new List<StationStaffingRuleDto>
        {
            new(StationRole.Kitchen, 2 * scaleFactor, scaleFactor, Math.Max(1, scaleFactor / 2)),
            new(StationRole.KitchenAssistant, 2 * scaleFactor, scaleFactor, Math.Max(1, scaleFactor / 2)),
            new(StationRole.Waiter, 3 * scaleFactor, 2 * scaleFactor, scaleFactor),
            new(StationRole.Bar, Math.Max(1, scaleFactor), Math.Max(1, scaleFactor), 1),
            new(StationRole.Cashier, Math.Max(1, scaleFactor), 1, 1)
        };

        return new RosterStressScenario(
            tenantId,
            company,
            employees,
            demandSlots,
            adjacent.Rosters,
            targetWeekStartDate,
            templates,
            staffingRules,
            adjacent.SundayCappedEmployeeIds);
    }

    private static List<Employee> BuildEmployees(
        Guid tenantId,
        Guid companyId,
        int employeeCount,
        bool includeUnavailableDays)
    {
        var employees = new List<Employee>(employeeCount);
        for (var i = 0; i < employeeCount; i++)
        {
            var primary = Roles[i % Roles.Length];
            var secondary1 = Roles[(i + 1) % Roles.Length];
            var secondary2 = Roles[(i + 2) % Roles.Length];

            var skills = new List<SecondarySkill>
            {
                SecondarySkill.Create(secondary1, SkillProficiency.Autonomous).Value
            };

            if (i % 2 == 0)
            {
                skills.Add(SecondarySkill.Create(secondary2, SkillProficiency.Support).Value);
            }

            var preferredOffDay = DaysPool[i % DaysPool.Length];
            var unavailableDays = includeUnavailableDays && i % 4 == 0
                ? new[] { DaysPool[(i + 3) % DaysPool.Length] }
                : Array.Empty<DayOfWeek>();

            // Garante ausência de sobreposição redundante se coincidir
            var effectivePreferredOff = unavailableDays.Contains(preferredOffDay)
                ? new[] { DaysPool[(i + 1) % DaysPool.Length] }
                : new[] { preferredOffDay };

            var employee = Employee.Create(
                tenantId,
                companyId,
                $"Colaborador-{i + 1:D3}",
                primary,
                skills,
                effectivePreferredOff,
                unavailableDays).Value;

            employees.Add(employee);
        }

        return employees;
    }

    private static List<DemandSlot> BuildWeeklyDemandCurve(
        Guid tenantId,
        Guid companyId,
        IReadOnlyCollection<DayOfWeek> closedDays,
        TimeOnly openingTime,
        int openHoursCount)
    {
        var slots = new List<DemandSlot>(7 * openHoursCount);
        const decimal peakVolume = 120m;

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            if (closedDays.Contains(day))
            {
                continue;
            }

            for (var hourOffset = 0; hourOffset < openHoursCount; hourOffset++)
            {
                var hour = (openingTime.Hour + hourOffset) % 24;
                var isWeekend = day is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday;
                var isPrimeDinner = hour is >= 19 and <= 23;
                var isLunch = hour is >= 12 and <= 14;

                var volume = (isWeekend, isPrimeDinner, isLunch) switch
                {
                    (true, true, _) => 120m,
                    (true, false, true) => 84m,
                    (false, true, _) => 82m,
                    (false, false, true) => 65m,
                    _ when day is DayOfWeek.Monday or DayOfWeek.Tuesday => 24m,
                    _ => 48m
                };

                var slot = DemandSlot.Create(tenantId, companyId, day, hour, volume).Value;
                slot.EvaluateRelativeDemand(peakVolume).IsSuccess.Should().BeTrue();
                slots.Add(slot);
            }
        }

        return slots;
    }

    private static (List<Roster> Rosters, HashSet<Guid> SundayCappedEmployeeIds) BuildHistoryAndLookaheadRosters(
        Guid tenantId,
        Guid companyId,
        DateOnly targetWeekStart,
        IReadOnlyList<Employee> employees,
        IReadOnlyCollection<DayOfWeek> closedDays,
        IReadOnlyList<ShiftTemplateInputDto> templates,
        int previousWeeksCount,
        int lookaheadStep)
    {
        var rosters = new List<Roster>(previousWeeksCount + (lookaheadStep > 0 ? 1 : 0));
        var sundayCapped = new HashSet<Guid>();
        var firstTemplate = templates[0];
        var lastTemplate = templates[^1];

        for (var weekBack = previousWeeksCount; weekBack >= 1; weekBack--)
        {
            var weekStart = targetWeekStart.AddDays(-7 * weekBack);
            var roster = Roster.Create(tenantId, companyId, weekStart).Value;

            for (var idx = 0; idx < employees.Count; idx++)
            {
                var employee = employees[idx];

                // ~17% dos colaboradores (passo 6, coprimo com 5 praças) trabalham os dois domingos anteriores consecutivos
                var forceSundayStreak = idx % 6 == 1;
                var off1 = forceSundayStreak ? 0 : idx % 7;
                var off2 = forceSundayStreak ? 1 : (idx + 1) % 7;

                for (var dayOffset = 0; dayOffset < 7; dayOffset++)
                {
                    if (dayOffset == off1 || dayOffset == off2)
                    {
                        continue;
                    }

                    var date = weekStart.AddDays(dayOffset);
                    if (closedDays.Contains(date.DayOfWeek) || employee.UnavailableDays.Contains(date.DayOfWeek))
                    {
                        continue;
                    }

                    // Cada colaborador mantém padrão coerente de turno na semana histórica para não quebrar 11h ao alternar madrugada->manhã
                    var isLateShift = idx % 2 == 0;
                    var chosen = isLateShift ? lastTemplate : firstTemplate;

                    var assignResult = roster.AssignShift(employee, date, chosen.StartTime, chosen.EndTime, employee.PrimaryRole);
                    assignResult.IsSuccess.Should().BeTrue(
                        $"falha inesperada ao construir histórico CLT para {employee.Name} em {date}: {assignResult.Error.Message}");
                }

                if (forceSundayStreak && weekBack == 1)
                {
                    sundayCapped.Add(employee.Id);
                }
            }

            rosters.Add(roster);
        }

        if (lookaheadStep > 0)
        {
            var nextWeekStart = targetWeekStart.AddDays(7);
            var nextRoster = Roster.Create(tenantId, companyId, nextWeekStart).Value;

            for (var idx = 0; idx < employees.Count; idx++)
            {
                var employee = employees[idx];
                if (closedDays.Contains(nextWeekStart.DayOfWeek) ||
                    employee.UnavailableDays.Contains(nextWeekStart.DayOfWeek))
                {
                    continue;
                }

                // Parte dos colaboradores inicia no primeiro turno da segunda-feira seguinte (bloqueando noturno/madrugada no domingo anterior)
                if (idx % lookaheadStep == 0)
                {
                    var lookaheadResult = nextRoster.AssignShift(
                        employee,
                        nextWeekStart,
                        firstTemplate.StartTime,
                        firstTemplate.EndTime,
                        employee.PrimaryRole);
                    lookaheadResult.IsSuccess.Should().BeTrue();
                }
            }

            rosters.Add(nextRoster);
        }

        return (rosters, sundayCapped);
    }

    private static Guid CreateDeterministicGuid(int scenarioKind, int employeeCount, int variant)
    {
        Span<byte> bytes = stackalloc byte[16];
        bytes.Clear();
        bytes[0] = (byte)scenarioKind;
        bytes[1] = (byte)employeeCount;
        bytes[2] = (byte)variant;
        bytes[15] = 0x52;
        return new Guid(bytes);
    }
}
