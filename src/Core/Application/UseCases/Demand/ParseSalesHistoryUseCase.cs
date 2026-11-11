using Application.Common.Interfaces;
using Application.DTOs.Demand;
using Application.UseCases.Demand.Parsing;
using Domain.Entities;
using Domain.Repositories;

namespace Application.UseCases.Demand;

public sealed class ParseSalesHistoryUseCase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IDemandSlotRepository _demandSlotRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CsvSalesHistoryParser _csvParser = new();
    private readonly XlsxSalesHistoryParser _xlsxParser = new();

    public ParseSalesHistoryUseCase(
        ICompanyRepository companyRepository,
        IDemandSlotRepository demandSlotRepository,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(companyRepository);
        ArgumentNullException.ThrowIfNull(demandSlotRepository);
        ArgumentNullException.ThrowIfNull(currentUserService);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _companyRepository = companyRepository;
        _demandSlotRepository = demandSlotRepository;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ParseSalesHistoryResultDto>> ExecuteAsync(
        ParseSalesHistoryCommand command,
        CancellationToken ct = default)
    {
        if (command is null)
        {
            return Result.Failure<ParseSalesHistoryResultDto>(
                Error.Validation("DemandImport.InvalidCommand", "Comando de importação inválido."));
        }

        if (!Enum.IsDefined(command.Source))
        {
            return Result.Failure<ParseSalesHistoryResultDto>(
                Error.Validation("DemandImport.InvalidSource", "Origem de importação de demanda inválida."));
        }

        var contextResult = await DemandUseCaseHelpers.ResolveCompanyAsync(
            command.CompanyId,
            _companyRepository,
            _currentUserService,
            ct);

        if (contextResult.IsFailure)
        {
            return Result.Failure<ParseSalesHistoryResultDto>(contextResult.Error);
        }

        var (tenantId, company) = contextResult.Value;

        var parseResult = ParseSource(command);
        if (parseResult.IsFailure)
        {
            return Result.Failure<ParseSalesHistoryResultDto>(parseResult.Error);
        }

        var parsed = parseResult.Value;
        var warnings = parsed.Warnings.ToList();

        var targetEntries = BuildTargetMatrix(company, parsed.Slots, command.FillMissingOperatingHoursWithZero, warnings);

        var syncResult = await SynchronizeSlotsAsync(tenantId, company.Id, targetEntries, ct);
        if (syncResult.IsFailure)
        {
            return Result.Failure<ParseSalesHistoryResultDto>(syncResult.Error);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        var snapshots = DemandUseCaseHelpers.MapToOrderedSnapshots(syncResult.Value, company);

        var totalWeeklyVolume = snapshots.Sum(s => s.ExpectedVolume);

        return Result.Success(new ParseSalesHistoryResultDto(
            company.Id,
            command.Source,
            parsed.RawRowsProcessed,
            snapshots.Count,
            totalWeeklyVolume,
            snapshots,
            warnings));
    }

    private Result<ParsedDemandMatrixResult> ParseSource(ParseSalesHistoryCommand command) =>
        command.Source switch
        {
            DemandImportSource.Csv => ParseFile(command, ".csv", _csvParser.Parse),
            DemandImportSource.Xlsx => ParseFile(command, ".xlsx", _xlsxParser.Parse),
            DemandImportSource.ManualMatrix => ParseManualEntries(command.ManualEntries),
            _ => Result.Failure<ParsedDemandMatrixResult>(
                Error.Validation("DemandImport.InvalidSource", "Origem de importação inválida."))
        };

    private static Result<ParsedDemandMatrixResult> ParseFile(
        ParseSalesHistoryCommand command,
        string expectedExtension,
        Func<Stream, Result<ParsedDemandMatrixResult>> parser)
    {
        if (command.FileStream is null)
        {
            return Result.Failure<ParsedDemandMatrixResult>(
                Error.Validation("DemandImport.EmptyFile", "O stream do arquivo de vendas é obrigatório."));
        }

        if (!string.IsNullOrWhiteSpace(command.FileName))
        {
            var extension = Path.GetExtension(command.FileName.Trim());
            if (!string.Equals(extension, expectedExtension, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<ParsedDemandMatrixResult>(
                    Error.Validation(
                        "DemandImport.UnsupportedFileExtension",
                        $"Extensão '{extension}' incompatível. Era esperado um arquivo '{expectedExtension}'."));
            }
        }

        return parser(command.FileStream);
    }

    private static Result<ParsedDemandMatrixResult> ParseManualEntries(
        IReadOnlyList<ManualDemandEntryDto>? manualEntries)
    {
        if (manualEntries is null || manualEntries.Count == 0)
        {
            return Result.Failure<ParsedDemandMatrixResult>(
                Error.Validation("DemandImport.EmptyManualEntries", "Informe ao menos um slot horário na matriz manual."));
        }

        var seenKeys = new HashSet<(DayOfWeek, int)>();
        foreach (var entry in manualEntries)
        {
            if (!Enum.IsDefined(entry.DayOfWeek))
            {
                return Result.Failure<ParsedDemandMatrixResult>(
                    Error.Validation("DemandImport.InvalidDayOfWeek", "Dia da semana inválido na matriz manual."));
            }

            if (entry.Hour < DemandSlot.MinHour || entry.Hour > DemandSlot.MaxHour)
            {
                return Result.Failure<ParsedDemandMatrixResult>(
                    Error.Validation("DemandImport.InvalidHour", $"Hora inválida ({entry.Hour}) na matriz manual."));
            }

            if (entry.ExpectedVolume < 0m)
            {
                return Result.Failure<ParsedDemandMatrixResult>(
                    Error.Validation("DemandImport.NegativeVolume", "O volume esperado não pode ser negativo na matriz manual."));
            }

            if (!seenKeys.Add((entry.DayOfWeek, entry.Hour)))
            {
                return Result.Failure<ParsedDemandMatrixResult>(
                    Error.Validation(
                        "DemandImport.DuplicateManualSlot",
                        $"Slot duplicado informado para {entry.DayOfWeek} às {entry.Hour}h."));
            }
        }

        return Result.Success(new ParsedDemandMatrixResult(manualEntries, manualEntries.Count, []));
    }

    private static Dictionary<(DayOfWeek DayOfWeek, int Hour), decimal> BuildTargetMatrix(
        Company company,
        IReadOnlyList<ManualDemandEntryDto> parsedSlots,
        bool fillMissingOperatingHoursWithZero,
        List<string> warnings)
    {
        var matrix = new Dictionary<(DayOfWeek DayOfWeek, int Hour), decimal>();
        var outOfWindowCount = 0;

        foreach (var entry in parsedSlots)
        {
            if (!company.IsOpenAt(entry.DayOfWeek, entry.Hour))
            {
                outOfWindowCount++;
            }

            matrix[(entry.DayOfWeek, entry.Hour)] = entry.ExpectedVolume;
        }

        if (outOfWindowCount > 0)
        {
            warnings.Add($"{outOfWindowCount} slot(s) possuem registros fora da janela de funcionamento cadastrada para o estabelecimento.");
        }

        if (fillMissingOperatingHoursWithZero)
        {
            foreach (var day in Enum.GetValues<DayOfWeek>())
            {
                for (var hour = DemandSlot.MinHour; hour <= DemandSlot.MaxHour; hour++)
                {
                    if (company.IsOpenAt(day, hour))
                    {
                        matrix.TryAdd((day, hour), 0m);
                    }
                }
            }
        }

        return matrix;
    }

    private async Task<Result<List<DemandSlot>>> SynchronizeSlotsAsync(
        Guid tenantId,
        Guid companyId,
        Dictionary<(DayOfWeek DayOfWeek, int Hour), decimal> targetMatrix,
        CancellationToken ct)
    {
        var existingSlots = await _demandSlotRepository.GetByCompanyAsync(companyId, ct);
        var existingByKey = existingSlots.ToDictionary(s => (s.DayOfWeek, s.Hour));

        var resultingSlots = new List<DemandSlot>(targetMatrix.Count);
        var slotsToAdd = new List<DemandSlot>();

        foreach (var ((dayOfWeek, hour), expectedVolume) in targetMatrix)
        {
            if (existingByKey.TryGetValue((dayOfWeek, hour), out var existingSlot))
            {
                var updateResult = existingSlot.UpdateVolume(expectedVolume);
                if (updateResult.IsFailure)
                {
                    return Result.Failure<List<DemandSlot>>(updateResult.Error);
                }

                _demandSlotRepository.Update(existingSlot);
                resultingSlots.Add(existingSlot);
            }
            else
            {
                var createResult = DemandSlot.Create(tenantId, companyId, dayOfWeek, hour, expectedVolume);
                if (createResult.IsFailure)
                {
                    return Result.Failure<List<DemandSlot>>(createResult.Error);
                }

                slotsToAdd.Add(createResult.Value);
                resultingSlots.Add(createResult.Value);
            }
        }

        if (slotsToAdd.Count > 0)
        {
            await _demandSlotRepository.AddRangeAsync(slotsToAdd, ct);
        }

        var obsoleteSlots = existingSlots
            .Where(s => !targetMatrix.ContainsKey((s.DayOfWeek, s.Hour)))
            .ToList();

        if (obsoleteSlots.Count > 0)
        {
            _demandSlotRepository.RemoveRange(obsoleteSlots);
        }

        return Result.Success(resultingSlots);
    }
}
