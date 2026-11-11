using Domain.Common;
using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Representa um colaborador do estabelecimento, sua função principal, matriz de polivalência e disponibilidade.
/// </summary>
public sealed class Employee : AggregateRoot, ITenantEntity
{
    public const int MaxNameLength = 150;

    private readonly List<SecondarySkill> _secondarySkills = [];
    private readonly List<DayOfWeek> _preferredDaysOff = [];
    private readonly List<DayOfWeek> _unavailableDays = [];

    private Employee()
    {
    }

    private Employee(
        Guid tenantId,
        Guid companyId,
        string name,
        StationRole primaryRole,
        List<SecondarySkill> secondarySkills,
        List<DayOfWeek> preferredDaysOff,
        List<DayOfWeek> unavailableDays)
    {
        TenantId = tenantId;
        CompanyId = companyId;
        Name = name;
        PrimaryRole = primaryRole;
        IsActive = true;
        _secondarySkills = secondarySkills;
        _preferredDaysOff = preferredDaysOff;
        _unavailableDays = unavailableDays;
    }

    public Guid TenantId { get; set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public StationRole PrimaryRole { get; private set; }
    public bool IsActive { get; private set; }
    public IReadOnlyCollection<SecondarySkill> SecondarySkills => _secondarySkills.AsReadOnly();
    public IReadOnlyCollection<DayOfWeek> PreferredDaysOff => _preferredDaysOff.AsReadOnly();
    public IReadOnlyCollection<DayOfWeek> UnavailableDays => _unavailableDays.AsReadOnly();

    /// <summary>
    /// Cria uma nova instância válida de <see cref="Employee"/>.
    /// </summary>
    public static Result<Employee> Create(
        Guid tenantId,
        Guid companyId,
        string name,
        StationRole primaryRole,
        IEnumerable<SecondarySkill>? secondarySkills = null,
        IEnumerable<DayOfWeek>? preferredDaysOff = null,
        IEnumerable<DayOfWeek>? unavailableDays = null)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<Employee>(
                Error.Validation("Employee.EmptyTenantId", "O identificador do tenant é obrigatório."));
        }

        if (companyId == Guid.Empty)
        {
            return Result.Failure<Employee>(
                Error.Validation("Employee.EmptyCompanyId", "O identificador do estabelecimento é obrigatório."));
        }

        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
        {
            return Result.Failure<Employee>(nameResult.Error);
        }

        if (!Enum.IsDefined(primaryRole))
        {
            return Result.Failure<Employee>(
                Error.Validation("Employee.InvalidStationRole", "Função principal inválida."));
        }

        var skillsResult = ValidateSecondarySkills(primaryRole, secondarySkills);
        if (skillsResult.IsFailure)
        {
            return Result.Failure<Employee>(skillsResult.Error);
        }

        var availabilityResult = ValidateAvailability(preferredDaysOff, unavailableDays);
        if (availabilityResult.IsFailure)
        {
            return Result.Failure<Employee>(availabilityResult.Error);
        }

        return Result.Success(new Employee(
            tenantId,
            companyId,
            nameResult.Value,
            primaryRole,
            skillsResult.Value,
            availabilityResult.Value.Preferred,
            availabilityResult.Value.Unavailable));
    }

    /// <summary>
    /// Atualiza o nome do colaborador.
    /// </summary>
    public Result UpdateName(string name)
    {
        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
        {
            return Result.Failure(nameResult.Error);
        }

        Name = nameResult.Value;
        return Result.Success();
    }

    /// <summary>
    /// Atualiza a função principal do colaborador, removendo-a da matriz secundária caso exista.
    /// </summary>
    public Result UpdatePrimaryRole(StationRole primaryRole)
    {
        if (!Enum.IsDefined(primaryRole))
        {
            return Result.Failure(
                Error.Validation("Employee.InvalidStationRole", "Função principal inválida."));
        }

        PrimaryRole = primaryRole;
        _secondarySkills.RemoveAll(skill => skill.Role == primaryRole);
        return Result.Success();
    }

    /// <summary>
    /// Adiciona ou atualiza o nível de proficiência de uma competência secundária (polivalência).
    /// </summary>
    public Result AddOrUpdateSecondarySkill(StationRole role, SkillProficiency proficiency)
    {
        var skillResult = SecondarySkill.Create(role, proficiency);
        if (skillResult.IsFailure)
        {
            return Result.Failure(skillResult.Error);
        }

        if (role == PrimaryRole)
        {
            return Result.Failure(
                Error.Conflict(
                    "Employee.PrimaryRoleCannotBeSecondary",
                    "A função principal não pode ser cadastrada como competência secundária."));
        }

        var existingIndex = _secondarySkills.FindIndex(skill => skill.Role == role);
        if (existingIndex >= 0)
        {
            _secondarySkills[existingIndex] = skillResult.Value;
        }
        else
        {
            _secondarySkills.Add(skillResult.Value);
        }

        return Result.Success();
    }

    /// <summary>
    /// Remove uma competência secundária da matriz de polivalência do colaborador.
    /// </summary>
    public Result RemoveSecondarySkill(StationRole role)
    {
        if (!Enum.IsDefined(role))
        {
            return Result.Failure(
                Error.Validation("Employee.InvalidStationRole", "Praça de atuação inválida."));
        }

        var removed = _secondarySkills.RemoveAll(skill => skill.Role == role);
        if (removed == 0)
        {
            return Result.Failure(
                Error.NotFound("Employee.SecondarySkillNotFound", "Competência secundária não encontrada."));
        }

        return Result.Success();
    }

    /// <summary>
    /// Atualiza as preferências de folga e restrições de indisponibilidade do colaborador.
    /// </summary>
    public Result UpdateAvailability(
        IEnumerable<DayOfWeek>? preferredDaysOff,
        IEnumerable<DayOfWeek>? unavailableDays)
    {
        var availabilityResult = ValidateAvailability(preferredDaysOff, unavailableDays);
        if (availabilityResult.IsFailure)
        {
            return Result.Failure(availabilityResult.Error);
        }

        _preferredDaysOff.Clear();
        _preferredDaysOff.AddRange(availabilityResult.Value.Preferred);

        _unavailableDays.Clear();
        _unavailableDays.AddRange(availabilityResult.Value.Unavailable);

        return Result.Success();
    }

    /// <summary>
    /// Desativa o colaborador para alocação em escalas e trocas.
    /// </summary>
    public Result Deactivate()
    {
        if (!IsActive)
        {
            return Result.Failure(
                Error.Conflict("Employee.AlreadyInactive", "O colaborador já está inativo."));
        }

        IsActive = false;
        return Result.Success();
    }

    /// <summary>
    /// Reativa o colaborador para alocação em escalas e trocas.
    /// </summary>
    public Result Activate()
    {
        if (IsActive)
        {
            return Result.Failure(
                Error.Conflict("Employee.AlreadyActive", "O colaborador já está ativo."));
        }

        IsActive = true;
        return Result.Success();
    }

    /// <summary>
    /// Indica se o colaborador possui preferência flexível de folga no dia da semana informado.
    /// </summary>
    public bool PrefersDayOff(DayOfWeek dayOfWeek) =>
        Enum.IsDefined(dayOfWeek) && _preferredDaysOff.Contains(dayOfWeek);

    /// <summary>
    /// Indica se o colaborador está disponível para trabalhar no dia da semana informado.
    /// </summary>
    public bool IsAvailableOn(DayOfWeek dayOfWeek) =>
        Enum.IsDefined(dayOfWeek) && !_unavailableDays.Contains(dayOfWeek);

    /// <summary>
    /// Avalia a elegibilidade do colaborador para cobrir uma praça em determinado nível de demanda e dia opcional.
    /// </summary>
    public Result<CoverageOrigin> EvaluateCoverageEligibility(
        StationRole targetRole,
        DemandClassification demandLevel,
        DayOfWeek? dayOfWeek = null)
    {
        if (!Enum.IsDefined(targetRole))
        {
            return Result.Failure<CoverageOrigin>(
                Error.Validation("Employee.InvalidStationRole", "Praça de atuação inválida."));
        }

        if (!Enum.IsDefined(demandLevel))
        {
            return Result.Failure<CoverageOrigin>(
                Error.Validation("Employee.InvalidDemandClassification", "Classificação de demanda inválida."));
        }

        if (dayOfWeek.HasValue && !Enum.IsDefined(dayOfWeek.Value))
        {
            return Result.Failure<CoverageOrigin>(
                Error.Validation("Employee.InvalidDayOfWeek", "Dia da semana inválido."));
        }

        if (!IsActive)
        {
            return Result.Failure<CoverageOrigin>(
                Error.Conflict("Employee.Inactive", "Colaborador inativo não é elegível para cobertura de praça."));
        }

        if (dayOfWeek.HasValue && _unavailableDays.Contains(dayOfWeek.Value))
        {
            return Result.Failure<CoverageOrigin>(
                Error.Conflict("Employee.UnavailableOnDay", "Colaborador possui restrição de indisponibilidade no dia informado."));
        }

        if (targetRole == PrimaryRole)
        {
            return Result.Success(CoverageOrigin.PrimaryRole);
        }

        var matchingSkill = _secondarySkills.FirstOrDefault(skill => skill.Role == targetRole);
        if (!Enum.IsDefined(matchingSkill.Role))
        {
            return Result.Failure<CoverageOrigin>(
                Error.Conflict(
                    "Employee.RoleNotCovered",
                    "O colaborador não possui competência principal ou secundária para a praça informada."));
        }

        if (!matchingSkill.CanCover(demandLevel))
        {
            return Result.Failure<CoverageOrigin>(
                Error.Conflict(
                    "Employee.InsufficientProficiencyForPeak",
                    "Colaborador com nível de apoio não é elegível para cobertura da praça em horário de pico."));
        }

        return Result.Success(CoverageOrigin.SecondarySkill);
    }

    private static Result<string> ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<string>(
                Error.Validation("Employee.EmptyName", "O nome do colaborador é obrigatório."));
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
        {
            return Result.Failure<string>(
                Error.Validation("Employee.NameTooLong", $"O nome do colaborador não pode exceder {MaxNameLength} caracteres."));
        }

        return Result.Success(trimmedName);
    }

    private static Result<List<SecondarySkill>> ValidateSecondarySkills(
        StationRole primaryRole,
        IEnumerable<SecondarySkill>? secondarySkills)
    {
        if (secondarySkills is null)
        {
            return Result.Success(new List<SecondarySkill>());
        }

        var result = new List<SecondarySkill>();
        var seenRoles = new HashSet<StationRole>();

        foreach (var skill in secondarySkills)
        {
            if (!Enum.IsDefined(skill.Role) || !Enum.IsDefined(skill.Proficiency))
            {
                return Result.Failure<List<SecondarySkill>>(
                    Error.Validation("Employee.InvalidSecondarySkill", "Competência secundária inválida."));
            }

            if (skill.Role == primaryRole)
            {
                return Result.Failure<List<SecondarySkill>>(
                    Error.Validation(
                        "Employee.PrimaryRoleCannotBeSecondary",
                        "A função principal não pode ser cadastrada como competência secundária."));
            }

            if (!seenRoles.Add(skill.Role))
            {
                return Result.Failure<List<SecondarySkill>>(
                    Error.Validation(
                        "Employee.DuplicateSecondarySkill",
                        "Não é permitido duplicar a mesma praça na matriz de competências secundárias."));
            }

            result.Add(skill);
        }

        return Result.Success(result);
    }

    private static Result<(List<DayOfWeek> Preferred, List<DayOfWeek> Unavailable)> ValidateAvailability(
        IEnumerable<DayOfWeek>? preferredDaysOff,
        IEnumerable<DayOfWeek>? unavailableDays)
    {
        var preferredResult = NormalizeDistinctDays(
            preferredDaysOff,
            "Employee.InvalidPreferredDayOff",
            "Dia da semana inválido informado para preferência de folga.");

        if (preferredResult.IsFailure)
        {
            return Result.Failure<(List<DayOfWeek>, List<DayOfWeek>)>(preferredResult.Error);
        }

        var unavailableResult = NormalizeDistinctDays(
            unavailableDays,
            "Employee.InvalidUnavailableDay",
            "Dia da semana inválido informado para indisponibilidade.");

        if (unavailableResult.IsFailure)
        {
            return Result.Failure<(List<DayOfWeek>, List<DayOfWeek>)>(unavailableResult.Error);
        }

        if (unavailableResult.Value.Count >= 7)
        {
            return Result.Failure<(List<DayOfWeek>, List<DayOfWeek>)>(
                Error.Validation(
                    "Employee.AllDaysUnavailable",
                    "O colaborador não pode ficar indisponível em todos os dias da semana."));
        }

        if (preferredResult.Value.Any(day => unavailableResult.Value.Contains(day)))
        {
            return Result.Failure<(List<DayOfWeek>, List<DayOfWeek>)>(
                Error.Validation(
                    "Employee.ConflictingDayConstraint",
                    "Um dia marcado como indisponível não pode ser informado como preferência de folga."));
        }

        return Result.Success((preferredResult.Value, unavailableResult.Value));
    }

    private static Result<List<DayOfWeek>> NormalizeDistinctDays(
        IEnumerable<DayOfWeek>? days,
        string errorCode,
        string errorMessage)
    {
        var normalizedDays = days?.Distinct().ToList() ?? [];

        if (normalizedDays.Any(day => !Enum.IsDefined(day)))
        {
            return Result.Failure<List<DayOfWeek>>(Error.Validation(errorCode, errorMessage));
        }

        return Result.Success(normalizedDays);
    }
}

