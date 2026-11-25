using Domain.Enums;

namespace Domain.Common;

/// <summary>
/// Catálogo que organiza a relação entre os macrosetores de atividade econômica
/// e os tipos específicos de operação comercial.
/// </summary>
public static class BusinessSectorCatalog
{
    private static readonly Dictionary<BusinessSector, BusinessType[]> SectorToTypes = new()
    {
        [BusinessSector.FoodService] =
        [
            BusinessType.Restaurant,
            BusinessType.Bar,
            BusinessType.CafeBakery,
            BusinessType.FastFood,
            BusinessType.DarkKitchen
        ],
        [BusinessSector.Hospitality] =
        [
            BusinessType.Hotel,
            BusinessType.InnResort,
            BusinessType.Hostel
        ],
        [BusinessSector.Retail] =
        [
            BusinessType.Supermarket,
            BusinessType.RetailStore,
            BusinessType.Pharmacy,
            BusinessType.ConvenienceStore
        ],
        [BusinessSector.ServicesAndOther] =
        [
            BusinessType.ClinicHealth,
            BusinessType.GymWellness,
            BusinessType.Other
        ]
    };

    private static readonly Dictionary<BusinessType, BusinessSector> TypeToSector =
        SectorToTypes
            .SelectMany(pair => pair.Value.Select(type => (Type: type, Sector: pair.Key)))
            .ToDictionary(x => x.Type, x => x.Sector);

    public static IReadOnlyList<BusinessSector> GetAllSectors() =>
        [
            BusinessSector.FoodService,
            BusinessSector.Hospitality,
            BusinessSector.Retail,
            BusinessSector.ServicesAndOther
        ];

    public static IReadOnlyList<BusinessType> GetBusinessTypesForSector(BusinessSector sector)
    {
        return SectorToTypes.TryGetValue(sector, out var types)
            ? types
            : [BusinessType.Other];
    }

    public static BusinessSector GetSectorForBusinessType(BusinessType type)
    {
        return TypeToSector.TryGetValue(type, out var sector)
            ? sector
            : BusinessSector.ServicesAndOther;
    }

    public static BusinessType GetDefaultBusinessType(BusinessSector sector) => sector switch
    {
        BusinessSector.FoodService => BusinessType.Restaurant,
        BusinessSector.Hospitality => BusinessType.Hotel,
        BusinessSector.Retail => BusinessType.RetailStore,
        BusinessSector.ServicesAndOther => BusinessType.Other,
        _ => BusinessType.Restaurant
    };

    public static BusinessSector ParseSector(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return BusinessSector.FoodService;
        }

        if (Enum.TryParse<BusinessSector>(value, true, out var sector))
        {
            return sector;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("hotel") || normalized.Contains("hosped") || normalized.Contains("pousada"))
        {
            return BusinessSector.Hospitality;
        }
        if (normalized.Contains("varejo") || normalized.Contains("loja") || normalized.Contains("mercado") || normalized.Contains("comercio") || normalized.Contains("comércio"))
        {
            return BusinessSector.Retail;
        }
        if (normalized.Contains("outro") || normalized.Contains("serviço") || normalized.Contains("servico") || normalized.Contains("saude") || normalized.Contains("saúde"))
        {
            return BusinessSector.ServicesAndOther;
        }

        return BusinessSector.FoodService;
    }
}
