namespace Domain.Enums;

/// <summary>
/// Tipo específico de operação do estabelecimento (subsetor).
/// </summary>
public enum BusinessType
{
    // Alimentação & Gastronomia (1 - 19)
    Restaurant = 1,
    Bar = 2,
    CafeBakery = 3,
    FastFood = 4,
    DarkKitchen = 5,

    // Hotelaria & Hospedagem (20 - 39)
    Hotel = 20,
    InnResort = 21,
    Hostel = 22,

    // Varejo & Comércio Geral (40 - 59)
    Supermarket = 40,
    RetailStore = 41,
    Pharmacy = 42,
    ConvenienceStore = 43,

    // Serviços & Outros (60+)
    ClinicHealth = 60,
    GymWellness = 61,
    Other = 99
}
