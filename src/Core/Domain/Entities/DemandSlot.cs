using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// Representa um slot horário de demanda na matriz semanal (dia da semana × hora) de um estabelecimento.
/// </summary>
public sealed class DemandSlot : AggregateRoot, ITenantEntity
{
    public const int MinHour = 0;
    public const int MaxHour = 23;

    private const decimal IdleThreshold = 0.25m;
    private const decimal LowThreshold = 0.50m;
    private const decimal PeakThreshold = 0.75m;

    private DemandSlot()
    {
    }

    private DemandSlot(
        Guid tenantId,
        Guid companyId,
        DayOfWeek dayOfWeek,
        int hour,
        decimal expectedVolume)
    {
        TenantId = tenantId;
        CompanyId = companyId;
        DayOfWeek = dayOfWeek;
        Hour = hour;
        ExpectedVolume = expectedVolume;
        RelativeVolume = 0m;
        Classification = DemandClassification.Idle;
    }

    public Guid TenantId { get; set; }
    public Guid CompanyId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public int Hour { get; private set; }
    public decimal ExpectedVolume { get; private set; }
    public decimal RelativeVolume { get; private set; }
    public DemandClassification Classification { get; private set; }

    /// <summary>
    /// Cria uma nova instância válida de <see cref="DemandSlot"/>.
    /// </summary>
    public static Result<DemandSlot> Create(
        Guid tenantId,
        Guid companyId,
        DayOfWeek dayOfWeek,
        int hour,
        decimal expectedVolume)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<DemandSlot>(
                Error.Validation("DemandSlot.EmptyTenantId", "O identificador do tenant é obrigatório."));
        }

        if (companyId == Guid.Empty)
        {
            return Result.Failure<DemandSlot>(
                Error.Validation("DemandSlot.EmptyCompanyId", "O identificador do estabelecimento é obrigatório."));
        }

        if (!Enum.IsDefined(dayOfWeek))
        {
            return Result.Failure<DemandSlot>(
                Error.Validation("DemandSlot.InvalidDayOfWeek", "Dia da semana inválido."));
        }

        if (hour < MinHour || hour > MaxHour)
        {
            return Result.Failure<DemandSlot>(
                Error.Validation("DemandSlot.InvalidHour", $"A hora do slot deve estar entre {MinHour} e {MaxHour}."));
        }

        var volumeValidation = ValidateVolume(expectedVolume);
        if (volumeValidation.IsFailure)
        {
            return Result.Failure<DemandSlot>(volumeValidation.Error);
        }

        return Result.Success(new DemandSlot(tenantId, companyId, dayOfWeek, hour, expectedVolume));
    }

    /// <summary>
    /// Atualiza o volume esperado do slot e reinicia a avaliação relativa pendente de recálculo pelo pico.
    /// </summary>
    public Result UpdateVolume(decimal expectedVolume)
    {
        var volumeValidation = ValidateVolume(expectedVolume);
        if (volumeValidation.IsFailure)
        {
            return volumeValidation;
        }

        ExpectedVolume = expectedVolume;
        RelativeVolume = 0m;
        Classification = DemandClassification.Idle;
        return Result.Success();
    }

    /// <summary>
    /// Calcula o volume relativo do slot em relação ao pico semanal e classifica seu nível de ociosidade.
    /// </summary>
    public Result EvaluateRelativeDemand(decimal peakVolume)
    {
        if (peakVolume < 0m)
        {
            return Result.Failure(
                Error.Validation("DemandSlot.InvalidPeakVolume", "O volume de pico não pode ser negativo."));
        }

        if (peakVolume < ExpectedVolume)
        {
            return Result.Failure(
                Error.Validation("DemandSlot.PeakLessThanSlotVolume", "O volume de pico não pode ser menor que o volume do slot."));
        }

        if (peakVolume == 0m)
        {
            RelativeVolume = 0m;
            Classification = DemandClassification.Idle;
            return Result.Success();
        }

        RelativeVolume = Math.Round(ExpectedVolume / peakVolume, 4, MidpointRounding.AwayFromZero);
        Classification = ClassifyRelativeVolume(RelativeVolume);
        return Result.Success();
    }

    private static Result ValidateVolume(decimal expectedVolume)
    {
        if (expectedVolume < 0m)
        {
            return Result.Failure(
                Error.Validation("DemandSlot.NegativeVolume", "O volume esperado de demanda não pode ser negativo."));
        }

        return Result.Success();
    }

    private static DemandClassification ClassifyRelativeVolume(decimal relativeVolume) => relativeVolume switch
    {
        <= IdleThreshold => DemandClassification.Idle,
        <= LowThreshold => DemandClassification.Low,
        < PeakThreshold => DemandClassification.Moderate,
        _ => DemandClassification.Peak
    };
}
