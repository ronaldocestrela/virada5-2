using Domain.Common;

namespace Domain.ValueObjects;

/// <summary>
/// Política periódica de revezamento dominical conforme regras da CLT.
/// </summary>
public readonly record struct SundayRotationPolicy
{
    public const int MinConsecutiveSundays = 1;
    public const int MaxConsecutiveSundaysLimit = 3;

    public static readonly SundayRotationPolicy BiweeklyCycle = new(1);
    public static readonly SundayRotationPolicy ThreeWeekCycle = new(2);

    private SundayRotationPolicy(int maxConsecutiveSundaysWorked)
    {
        MaxConsecutiveSundaysWorked = maxConsecutiveSundaysWorked;
    }

    public int MaxConsecutiveSundaysWorked { get; }

    /// <summary>
    /// Cria uma política válida de revezamento dominical.
    /// </summary>
    public static Result<SundayRotationPolicy> Create(int maxConsecutiveSundaysWorked)
    {
        if (maxConsecutiveSundaysWorked < MinConsecutiveSundays ||
            maxConsecutiveSundaysWorked > MaxConsecutiveSundaysLimit)
        {
            return Result.Failure<SundayRotationPolicy>(
                Error.Validation(
                    "SundayRotationPolicy.InvalidMaxConsecutiveSundays",
                    $"O limite de domingos consecutivos deve estar entre {MinConsecutiveSundays} e {MaxConsecutiveSundaysLimit}."));
        }

        return Result.Success(new SundayRotationPolicy(maxConsecutiveSundaysWorked));
    }

    /// <summary>
    /// Indica se o colaborador pode trabalhar em um novo domingo dado o histórico consecutivo anterior.
    /// </summary>
    public bool CanWorkSunday(int consecutiveSundaysWorkedBeforeWeek) =>
        consecutiveSundaysWorkedBeforeWeek >= 0 &&
        consecutiveSundaysWorkedBeforeWeek < MaxConsecutiveSundaysWorked;
}

