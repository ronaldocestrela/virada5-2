using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// Representa o estabelecimento cliente (tenant) e suas regras de operação e escala.
/// </summary>
public sealed class Company : AggregateRoot, ITenantEntity
{
    public const int MaxNameLength = 150;

    private readonly List<DayOfWeek> _closedDays = [];

    private Company()
    {
    }

    private Company(
        Guid tenantId,
        string name,
        BusinessType businessType,
        TimeOnly openingTime,
        TimeOnly closingTime,
        ScheduleModel scheduleModel,
        List<DayOfWeek> closedDays)
    {
        TenantId = tenantId;
        Name = name;
        BusinessType = businessType;
        OpeningTime = openingTime;
        ClosingTime = closingTime;
        ScheduleModel = scheduleModel;
        _closedDays = closedDays;
    }

    public Guid TenantId { get; set; }
    public string Name { get; private set; } = string.Empty;
    public BusinessType BusinessType { get; private set; }
    public TimeOnly OpeningTime { get; private set; }
    public TimeOnly ClosingTime { get; private set; }
    public ScheduleModel ScheduleModel { get; private set; }
    public IReadOnlyCollection<DayOfWeek> ClosedDays => _closedDays.AsReadOnly();

    /// <summary>
    /// Cria uma nova instância válida de <see cref="Company"/>.
    /// </summary>
    public static Result<Company> Create(
        Guid tenantId,
        string name,
        BusinessType businessType,
        TimeOnly openingTime,
        TimeOnly closingTime,
        ScheduleModel scheduleModel,
        IEnumerable<DayOfWeek>? closedDays = null)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<Company>(
                Error.Validation("Company.EmptyTenantId", "O identificador do tenant é obrigatório."));
        }

        var profileResult = ValidateProfile(name, businessType);
        if (profileResult.IsFailure)
        {
            return Result.Failure<Company>(profileResult.Error);
        }

        var hoursResult = ValidateOperatingHours(openingTime, closingTime);
        if (hoursResult.IsFailure)
        {
            return Result.Failure<Company>(hoursResult.Error);
        }

        var scheduleResult = ValidateScheduleModel(scheduleModel, closedDays);
        if (scheduleResult.IsFailure)
        {
            return Result.Failure<Company>(scheduleResult.Error);
        }

        return Result.Success(new Company(
            tenantId,
            profileResult.Value,
            businessType,
            openingTime,
            closingTime,
            scheduleModel,
            scheduleResult.Value));
    }

    /// <summary>
    /// Atualiza o nome e o tipo de negócio do estabelecimento.
    /// </summary>
    public Result UpdateProfile(string name, BusinessType businessType)
    {
        var profileResult = ValidateProfile(name, businessType);
        if (profileResult.IsFailure)
        {
            return Result.Failure(profileResult.Error);
        }

        Name = profileResult.Value;
        BusinessType = businessType;
        return Result.Success();
    }

    /// <summary>
    /// Atualiza a janela padrão de funcionamento do estabelecimento.
    /// </summary>
    public Result UpdateOperatingHours(TimeOnly openingTime, TimeOnly closingTime)
    {
        var hoursResult = ValidateOperatingHours(openingTime, closingTime);
        if (hoursResult.IsFailure)
        {
            return Result.Failure(hoursResult.Error);
        }

        OpeningTime = openingTime;
        ClosingTime = closingTime;
        return Result.Success();
    }

    /// <summary>
    /// Atualiza o modelo de folga e os dias de fechamento fixo do estabelecimento.
    /// </summary>
    public Result UpdateScheduleModel(ScheduleModel scheduleModel, IEnumerable<DayOfWeek>? closedDays = null)
    {
        var scheduleResult = ValidateScheduleModel(scheduleModel, closedDays);
        if (scheduleResult.IsFailure)
        {
            return Result.Failure(scheduleResult.Error);
        }

        ScheduleModel = scheduleModel;
        _closedDays.Clear();
        _closedDays.AddRange(scheduleResult.Value);
        return Result.Success();
    }

    /// <summary>
    /// Indica se o estabelecimento opera no dia da semana informado.
    /// </summary>
    public bool IsOpenOn(DayOfWeek dayOfWeek) =>
        Enum.IsDefined(dayOfWeek) && !_closedDays.Contains(dayOfWeek);

    private static Result<string> ValidateProfile(string name, BusinessType businessType)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<string>(
                Error.Validation("Company.EmptyName", "O nome do estabelecimento é obrigatório."));
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
        {
            return Result.Failure<string>(
                Error.Validation("Company.NameTooLong", $"O nome do estabelecimento não pode exceder {MaxNameLength} caracteres."));
        }

        if (!Enum.IsDefined(businessType))
        {
            return Result.Failure<string>(
                Error.Validation("Company.InvalidBusinessType", "Tipo de estabelecimento inválido."));
        }

        return Result.Success(trimmedName);
    }

    private static Result ValidateOperatingHours(TimeOnly openingTime, TimeOnly closingTime)
    {
        if (openingTime == closingTime)
        {
            return Result.Failure(
                Error.Validation("Company.InvalidOperatingHours", "O horário de abertura não pode ser igual ao de fechamento."));
        }

        return Result.Success();
    }

    private static Result<List<DayOfWeek>> ValidateScheduleModel(
        ScheduleModel scheduleModel,
        IEnumerable<DayOfWeek>? closedDays)
    {
        if (!Enum.IsDefined(scheduleModel))
        {
            return Result.Failure<List<DayOfWeek>>(
                Error.Validation("Company.InvalidScheduleModel", "Modelo de escala inválido."));
        }

        if (scheduleModel == ScheduleModel.RotatingDaysOff)
        {
            return Result.Success(new List<DayOfWeek>());
        }

        var normalizedDays = closedDays?.Distinct().ToList() ?? [];

        if (normalizedDays.Any(day => !Enum.IsDefined(day)))
        {
            return Result.Failure<List<DayOfWeek>>(
                Error.Validation("Company.InvalidClosedDay", "Dia da semana inválido informado para fechamento."));
        }

        if (normalizedDays.Count == 0)
        {
            return Result.Failure<List<DayOfWeek>>(
                Error.Validation("Company.ClosedDaysRequired", "É obrigatório informar ao menos um dia de fechamento no modelo fixo."));
        }

        if (normalizedDays.Count >= 7)
        {
            return Result.Failure<List<DayOfWeek>>(
                Error.Validation("Company.AllDaysClosed", "O estabelecimento não pode permanecer fechado todos os dias da semana."));
        }

        return Result.Success(normalizedDays);
    }
}
