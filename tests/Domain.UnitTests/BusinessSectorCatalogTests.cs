using Domain.Common;
using Domain.Enums;
using Xunit;

namespace Domain.UnitTests;

public sealed class BusinessSectorCatalogTests
{
    [Fact]
    public void GetAllSectors_ShouldReturnAllFourDefinedSectors()
    {
        var sectors = BusinessSectorCatalog.GetAllSectors();

        Assert.Equal(4, sectors.Count);
        Assert.Contains(BusinessSector.FoodService, sectors);
        Assert.Contains(BusinessSector.Hospitality, sectors);
        Assert.Contains(BusinessSector.Retail, sectors);
        Assert.Contains(BusinessSector.ServicesAndOther, sectors);
    }

    [Theory]
    [InlineData(BusinessSector.FoodService, BusinessType.Restaurant)]
    [InlineData(BusinessSector.FoodService, BusinessType.Bar)]
    [InlineData(BusinessSector.Hospitality, BusinessType.Hotel)]
    [InlineData(BusinessSector.Hospitality, BusinessType.Hostel)]
    [InlineData(BusinessSector.Retail, BusinessType.Supermarket)]
    [InlineData(BusinessSector.Retail, BusinessType.RetailStore)]
    [InlineData(BusinessSector.ServicesAndOther, BusinessType.Other)]
    public void GetBusinessTypesForSector_ShouldContainExpectedTypes(BusinessSector sector, BusinessType expectedType)
    {
        var types = BusinessSectorCatalog.GetBusinessTypesForSector(sector);

        Assert.NotEmpty(types);
        Assert.Contains(expectedType, types);
    }

    [Theory]
    [InlineData(BusinessType.Restaurant, BusinessSector.FoodService)]
    [InlineData(BusinessType.Hotel, BusinessSector.Hospitality)]
    [InlineData(BusinessType.Supermarket, BusinessSector.Retail)]
    [InlineData(BusinessType.ClinicHealth, BusinessSector.ServicesAndOther)]
    public void GetSectorForBusinessType_ShouldMapCorrectly(BusinessType type, BusinessSector expectedSector)
    {
        var sector = BusinessSectorCatalog.GetSectorForBusinessType(type);

        Assert.Equal(expectedSector, sector);
    }

    [Theory]
    [InlineData("Retail", BusinessSector.Retail)]
    [InlineData("varejo", BusinessSector.Retail)]
    [InlineData("Hotelaria", BusinessSector.Hospitality)]
    [InlineData("FoodService", BusinessSector.FoodService)]
    [InlineData("Restaurante / Bar", BusinessSector.FoodService)]
    [InlineData("Outro", BusinessSector.ServicesAndOther)]
    [InlineData(null, BusinessSector.FoodService)]
    public void ParseSector_ShouldParseStringOrFallback(string? input, BusinessSector expectedSector)
    {
        var sector = BusinessSectorCatalog.ParseSector(input);

        Assert.Equal(expectedSector, sector);
    }

    [Fact]
    public void EveryBusinessType_ShouldHaveMappedSector()
    {
        foreach (var type in Enum.GetValues<BusinessType>())
        {
            var sector = BusinessSectorCatalog.GetSectorForBusinessType(type);
            var typesInSector = BusinessSectorCatalog.GetBusinessTypesForSector(sector);
            Assert.Contains(type, typesInSector);
        }
    }
}
