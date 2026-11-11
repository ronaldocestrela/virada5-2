using System.Globalization;
using System.Text;
using Application.Common.Interfaces;
using Application.Common.Telemetry;
using Application.DTOs.Rosters;
using Application.UseCases.Rosters.Engine;
using Domain.Enums;
using Domain.Repositories;

namespace Application.UseCases.Rosters;

/// <summary>
/// Caso de uso somente-leitura para exportação da escala semanal em formato CSV compatível com Excel pt-BR.
/// </summary>
public sealed class ExportRosterCsvUseCase
{
    private static readonly CultureInfo PtBrCulture = CultureInfo.GetCultureInfo("pt-BR");

    private readonly ICompanyRepository _companyRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IRosterRepository _rosterRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IResultTelemetry? _telemetry;

    public ExportRosterCsvUseCase(
        ICompanyRepository companyRepository,
        IEmployeeRepository employeeRepository,
        IRosterRepository rosterRepository,
        ICurrentUserService currentUserService,
        IResultTelemetry? telemetry = null)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(employeeRepository);
        ArgumentNullException.ThrowIfNull(rosterRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);

        _companyRepository = companyRepository;
        _employeeRepository = employeeRepository;
        _rosterRepository = rosterRepository;
        _currentUserService = currentUserService;
        _telemetry = telemetry;
    }

    public Task<Result<RosterCsvExportDto>> ExecuteAsync(
        ExportRosterCsvCommand command,
        CancellationToken ct = default)
        => ApplicationTelemetry.ObserveAsync(
            "ExportRosterCsv",
            _currentUserService.TenantId,
            _telemetry,
            () => ExecuteCoreAsync(command, ct));

    private async Task<Result<RosterCsvExportDto>> ExecuteCoreAsync(
        ExportRosterCsvCommand command,
        CancellationToken ct)
    {
        if (command is null)
        {
            return Result.Failure<RosterCsvExportDto>(
                Error.Validation("RosterExport.InvalidCommand", "Comando de exportação da escala inválido."));
        }

        if (_currentUserService.TenantId == Guid.Empty)
        {
            return Result.Failure<RosterCsvExportDto>(
                Error.Validation("RosterExport.EmptyTenant", "O identificador do tenant autenticado é obrigatório."));
        }

        if (command.RosterId == Guid.Empty)
        {
            return Result.Failure<RosterCsvExportDto>(
                Error.Validation("RosterExport.InvalidRosterId", "O identificador da escala é obrigatório."));
        }

        var roster = await _rosterRepository.GetByIdWithShiftsAsync(command.RosterId, ct);
        if (roster is null)
        {
            return Result.Failure<RosterCsvExportDto>(
                Error.NotFound("Roster.NotFound", "Escala não encontrada para o tenant atual."));
        }

        var company = await _companyRepository.GetByIdAsync(roster.CompanyId, ct);
        if (company is null)
        {
            return Result.Failure<RosterCsvExportDto>(
                Error.NotFound("Company.NotFound", "Estabelecimento da escala não encontrado."));
        }

        var activeEmployees = await _employeeRepository.GetActiveByCompanyAsync(company.Id, ct);
        var employees = await RosterResultMapper.MergeRelevantEmployeesAsync(
            roster,
            activeEmployees,
            _employeeRepository,
            ct);

        var employeeNames = employees.ToDictionary(e => e.Id, e => e.Name);
        var statusLabel = FormatStatus(roster.Status);

        var sb = new StringBuilder();
        sb.AppendLine("Data;Dia;Colaborador;Praca;Inicio;Fim;DuracaoHoras;OrigemCobertura;Status");

        foreach (var shift in roster.Shifts
            .OrderBy(s => s.Date)
            .ThenBy(s => s.StartTime)
            .ThenBy(s => employeeNames.GetValueOrDefault(s.EmployeeId, string.Empty), StringComparer.Ordinal))
        {
            var employeeName = employeeNames.GetValueOrDefault(shift.EmployeeId, string.Empty);
            var durationHours = shift.Duration.TotalHours.ToString("0.0", PtBrCulture);

            sb.Append(EscapeCsvField(shift.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append(';')
              .Append(EscapeCsvField(FormatDayShort(shift.Date.DayOfWeek))).Append(';')
              .Append(EscapeCsvField(employeeName)).Append(';')
              .Append(EscapeCsvField(FormatStationRole(shift.AssignedRole))).Append(';')
              .Append(EscapeCsvField(shift.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture))).Append(';')
              .Append(EscapeCsvField(shift.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture))).Append(';')
              .Append(EscapeCsvField(durationHours)).Append(';')
              .Append(EscapeCsvField(FormatCoverageOrigin(shift.CoverageOrigin))).Append(';')
              .AppendLine(EscapeCsvField(statusLabel));
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var bodyBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var content = new byte[preamble.Length + bodyBytes.Length];
        Buffer.BlockCopy(preamble, 0, content, 0, preamble.Length);
        Buffer.BlockCopy(bodyBytes, 0, content, preamble.Length, bodyBytes.Length);

        var fileName = $"escala-5x2-{roster.WeekStartDate:yyyy-MM-dd}.csv";
        return Result.Success(new RosterCsvExportDto(fileName, "text/csv; charset=utf-8", content));
    }

    private static string EscapeCsvField(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var sanitized = raw;
        if (sanitized[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            sanitized = $"'{sanitized}";
        }

        if (sanitized.Contains(';') || sanitized.Contains('"') || sanitized.Contains('\n') || sanitized.Contains('\r'))
        {
            return $"\"{sanitized.Replace("\"", "\"\"")}\"";
        }

        return sanitized;
    }

    private static string FormatDayShort(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Segunda",
        DayOfWeek.Tuesday => "Terca",
        DayOfWeek.Wednesday => "Quarta",
        DayOfWeek.Thursday => "Quinta",
        DayOfWeek.Friday => "Sexta",
        DayOfWeek.Saturday => "Sabado",
        DayOfWeek.Sunday => "Domingo",
        _ => day.ToString()
    };

    private static string FormatStationRole(StationRole role) => role switch
    {
        StationRole.Kitchen => "Cozinha",
        StationRole.KitchenAssistant => "Aux. Cozinha",
        StationRole.Waiter => "Salao / Garcom",
        StationRole.Bar => "Bar",
        StationRole.Cashier => "Caixa",
        StationRole.Cleaning => "Limpeza",
        StationRole.Expedition => "Expedicao",
        StationRole.Management => "Gerencia",
        _ => role.ToString()
    };

    private static string FormatCoverageOrigin(CoverageOrigin origin) => origin switch
    {
        CoverageOrigin.PrimaryRole => "Funcao Principal",
        CoverageOrigin.SecondarySkill => "Polivalencia",
        _ => origin.ToString()
    };

    private static string FormatStatus(RosterStatus status) => status switch
    {
        RosterStatus.Draft => "Rascunho",
        RosterStatus.Published => "Publicada",
        RosterStatus.Archived => "Arquivada",
        _ => status.ToString()
    };
}
