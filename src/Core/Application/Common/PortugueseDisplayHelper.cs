using Domain.Enums;

namespace Application.Common;

/// <summary>
/// Formata enums de domínio em português brasileiro para mensagens de erro e alertas.
/// </summary>
internal static class PortugueseDisplayHelper
{
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

