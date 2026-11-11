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
    Roster PreviousWeekRoster,
    DateOnly TargetWeekStartDate,
    IReadOnlyList<ShiftTemplateInputDto> Templates,
    IReadOnlyList<StationStaffingRuleDto> StaffingRules);

internal static class RosterEngineStressProfileBuilder
{
    public static RosterStressScenario BuildComplexScenario(
        int employeeCount,
        ScheduleModel scheduleModel,
        DateOnly targetWeekStartDate)
    {
        var tenantId = Guid.NewGuid();
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

        var roles = new[]
        {
            StationRole.Kitchen,
            StationRole.KitchenAssistant,
            StationRole.Waiter,
            StationRole.Bar,
            StationRole.Cashier
        };

        var preferredDaysPool = new[]
        {
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday
        };

        var employees = new List<Employee>(employeeCount);
        for (var i = 0; i < employeeCount; i++)
        {
            var primary = roles[i % roles.Length];
            var secondary1 = roles[(i + 1) % roles.Length];
            var secondary2 = roles[(i + 2) % roles.Length];

            var skills = new List<SecondarySkill>
            {
                SecondarySkill.Create(secondary1, SkillProficiency.Autonomous).Value
            };

            if (i % 2 == 0)
            {
                skills.Add(SecondarySkill.Create(secondary2, SkillProficiency.Support).Value);
            }

            var preferredOff = new[] { preferredDaysPool[i % preferredDaysPool.Length] };
            var employee = Employee.Create(
                tenantId,
                company.Id,
                $"Colaborador-{i + 1:D3}",
                primary,
                skills,
                preferredOff).Value;

            employees.Add(employee);
        }

        var demandSlots = BuildWeeklyDemandCurve(tenantId, company.Id, closedDays);
        var previousWeekStart = targetWeekStartDate.AddDays(-7);
        var previousWeekRoster = BuildPreviousWeekBoundaryRoster(
            tenantId,
            company.Id,
            previousWeekStart,
            employees,
            closedDays);

        var templates = new List<ShiftTemplateInputDto>
        {
            new("Abertura", new TimeOnly(11, 0), new TimeOnly(19, 0)),
            new("Noturno", new TimeOnly(17, 0), new TimeOnly(1, 0))
        };

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
            previousWeekRoster,
            targetWeekStartDate,
            templates,
            staffingRules);
    }

    private static List<DemandSlot> BuildWeeklyDemandCurve(
        Guid tenantId,
        Guid companyId,
        IReadOnlyCollection<DayOfWeek> closedDays)
    {
        var slots = new List<DemandSlot>(7 * 14);
        const decimal peakVolume = 120m;

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            if (closedDays.Contains(day))
            {
                continue;
            }

            for (var hourOffset = 0; hourOffset < 14; hourOffset++)
            {
                var hour = (11 + hourOffset) % 24;
                var isWeekend = day is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday;
                var isPrimeDinner = hour is >= 19 and <= 23;
                var isLunch = hour is >= 12 and <= 14;

                var volume = (isWeekend, isPrimeDinner, isLunch) switch
                {
                    (true, true, _) => 120m,
                    (true, false, true) => 95m,
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

    private static Roster BuildPreviousWeekBoundaryRoster(
        Guid tenantId,
        Guid companyId,
        DateOnly previousWeekStart,
        IReadOnlyList<Employee> employees,
        IReadOnlyCollection<DayOfWeek> closedDays)
    {
        var roster = Roster.Create(tenantId, companyId, previousWeekStart).Value;

        for (var idx = 0; idx < employees.Count; idx++)
        {
            var employee = employees[idx];
            var off1 = idx % 7;
            var off2 = (idx + 1) % 7;

            for (var dayOffset = 0; dayOffset < 7; dayOffset++)
            {
                if (dayOffset == off1 || dayOffset == off2)
                {
                    continue;
                }

                var date = previousWeekStart.AddDays(dayOffset);
                if (closedDays.Contains(date.DayOfWeek))
                {
                    continue;
                }

                // Metade trabalha fechamento noturno (17:00-01:00) no fim de semana, forçando 11h na segunda
                var isLateShift = (idx + dayOffset) % 2 == 0;
                var start = isLateShift ? new TimeOnly(17, 0) : new TimeOnly(11, 0);
                var end = isLateShift ? new TimeOnly(1, 0) : new TimeOnly(19, 0);

                roster.AssignShift(employee, date, start, end, employee.PrimaryRole);
            }
        }

        return roster;
    }
}
