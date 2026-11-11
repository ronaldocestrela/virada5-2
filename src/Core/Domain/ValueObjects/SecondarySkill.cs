using Domain.Common;
using Domain.Enums;

namespace Domain.ValueObjects;

/// <summary>
/// Representa uma competência secundária (polivalência) com seu respectivo nível de proficiência.
/// </summary>
public readonly record struct SecondarySkill
{
    private SecondarySkill(StationRole role, SkillProficiency proficiency)
    {
        Role = role;
        Proficiency = proficiency;
    }

    public StationRole Role { get; }
    public SkillProficiency Proficiency { get; }

    /// <summary>
    /// Cria uma instância válida de <see cref="SecondarySkill"/>.
    /// </summary>
    public static Result<SecondarySkill> Create(StationRole role, SkillProficiency proficiency)
    {
        if (!Enum.IsDefined(role))
        {
            return Result.Failure<SecondarySkill>(
                Error.Validation("SecondarySkill.InvalidStationRole", "Praça de atuação inválida."));
        }

        if (!Enum.IsDefined(proficiency))
        {
            return Result.Failure<SecondarySkill>(
                Error.Validation("SecondarySkill.InvalidProficiency", "Nível de proficiência inválido."));
        }

        return Result.Success(new SecondarySkill(role, proficiency));
    }

    /// <summary>
    /// Indica se o nível de proficiência da competência secundária é suficiente para o nível de demanda informado.
    /// </summary>
    public bool CanCover(DemandClassification demandLevel) =>
        Enum.IsDefined(demandLevel) &&
        (Proficiency == SkillProficiency.Autonomous || demandLevel != DemandClassification.Peak);
}

