using System.Globalization;
using Domain.Enums;

namespace WebApp.Blazor.Helpers;

internal static class DesignSystemDisplayHelper
{
    private static readonly CultureInfo PtBrCulture = CultureInfo.GetCultureInfo("pt-BR");

    public static readonly DayOfWeek[] OperationalWeekOrder =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

    public static string FormatCurrency(decimal value) =>
        value.ToString("C2", PtBrCulture);

    public static string FormatDecimal(decimal value, int decimals = 1) =>
        value.ToString($"N{decimals}", PtBrCulture);

    public static string FormatDayShort(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "Seg",
        DayOfWeek.Tuesday => "Ter",
        DayOfWeek.Wednesday => "Qua",
        DayOfWeek.Thursday => "Qui",
        DayOfWeek.Friday => "Sex",
        DayOfWeek.Saturday => "Sáb",
        DayOfWeek.Sunday => "Dom",
        _ => dayOfWeek.ToString()
    };

    public static string FormatStationRole(StationRole role) => role switch
    {
        StationRole.Kitchen => "Cozinha",
        StationRole.KitchenAssistant => "Aux. Cozinha",
        StationRole.Waiter => "Salão / Garçom",
        StationRole.Bar => "Bar",
        StationRole.Cashier => "Caixa",
        StationRole.Cleaning => "Limpeza",
        StationRole.Expedition => "Expedição",
        StationRole.Management => "Gerência",
        _ => role.ToString()
    };

    public static string FormatSkillProficiency(SkillProficiency proficiency) => proficiency switch
    {
        SkillProficiency.Support => "Apoio",
        SkillProficiency.Autonomous => "Autônomo",
        _ => proficiency.ToString()
    };

    public static string FormatDemandClassification(DemandClassification classification) => classification switch
    {
        DemandClassification.Idle => "Ocioso",
        DemandClassification.Low => "Baixo",
        DemandClassification.Moderate => "Moderado",
        DemandClassification.Peak => "Pico",
        _ => classification.ToString()
    };

    public static string FormatRosterStatus(RosterStatus status) => status switch
    {
        RosterStatus.Draft => "Rascunho",
        RosterStatus.Published => "Publicada",
        RosterStatus.Archived => "Arquivada",
        _ => status.ToString()
    };

    public static string FormatBusinessType(BusinessType businessType) => businessType switch
    {
        BusinessType.Restaurant => "Restaurante",
        BusinessType.Bar => "Bar",
        BusinessType.CafeBakery => "Café / Padaria",
        BusinessType.FastFood => "Fast Food",
        BusinessType.DarkKitchen => "Dark Kitchen",
        BusinessType.Other => "Outro",
        _ => businessType.ToString()
    };

    public static string FormatScheduleModel(ScheduleModel scheduleModel) => scheduleModel switch
    {
        ScheduleModel.FixedClosure => "Fechamento fixo",
        ScheduleModel.RotatingDaysOff => "Folga rotativa",
        _ => scheduleModel.ToString()
    };

    public static string FormatDayFull(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "Segunda-feira",
        DayOfWeek.Tuesday => "Terça-feira",
        DayOfWeek.Wednesday => "Quarta-feira",
        DayOfWeek.Thursday => "Quinta-feira",
        DayOfWeek.Friday => "Sexta-feira",
        DayOfWeek.Saturday => "Sábado",
        DayOfWeek.Sunday => "Domingo",
        _ => dayOfWeek.ToString()
    };
}

