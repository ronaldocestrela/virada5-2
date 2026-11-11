using System.Reflection;
using Domain.Entities;
using Domain.Repositories;

namespace Domain.UnitTests.Repositories;

public class RepositoryContractsTests
{
    [Fact]
    public void CompanyRepository_WhenInspected_ShouldExposeExpectedSpecificMethods()
    {
        // Arrange
        var contract = typeof(ICompanyRepository);

        // Act & Assert
        AssertRepositoryInterface(contract);
        AssertMethod(contract, nameof(ICompanyRepository.GetByIdAsync), typeof(Task<Company?>),
            ("id", typeof(Guid)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(ICompanyRepository.GetCurrentAsync), typeof(Task<Company?>),
            ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(ICompanyRepository.AddAsync), typeof(Task),
            ("company", typeof(Company)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(ICompanyRepository.Update), typeof(void),
            ("company", typeof(Company)));
        contract.GetMethods().Should().HaveCount(4);
    }

    [Fact]
    public void DemandSlotRepository_WhenInspected_ShouldExposeExpectedSpecificMethods()
    {
        // Arrange
        var contract = typeof(IDemandSlotRepository);

        // Act & Assert
        AssertRepositoryInterface(contract);
        AssertMethod(contract, nameof(IDemandSlotRepository.GetByIdAsync), typeof(Task<DemandSlot?>),
            ("id", typeof(Guid)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IDemandSlotRepository.GetByCompanyDayAndHourAsync), typeof(Task<DemandSlot?>),
            ("companyId", typeof(Guid)), ("dayOfWeek", typeof(DayOfWeek)), ("hour", typeof(int)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IDemandSlotRepository.GetByCompanyAsync), typeof(Task<IReadOnlyList<DemandSlot>>),
            ("companyId", typeof(Guid)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IDemandSlotRepository.AddAsync), typeof(Task),
            ("demandSlot", typeof(DemandSlot)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IDemandSlotRepository.AddRangeAsync), typeof(Task),
            ("demandSlots", typeof(IEnumerable<DemandSlot>)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IDemandSlotRepository.Update), typeof(void),
            ("demandSlot", typeof(DemandSlot)));
        AssertMethod(contract, nameof(IDemandSlotRepository.RemoveRange), typeof(void),
            ("demandSlots", typeof(IEnumerable<DemandSlot>)));
        contract.GetMethods().Should().HaveCount(7);
    }

    [Fact]
    public void EmployeeRepository_WhenInspected_ShouldExposeExpectedSpecificMethods()
    {
        // Arrange
        var contract = typeof(IEmployeeRepository);

        // Act & Assert
        AssertRepositoryInterface(contract);
        AssertMethod(contract, nameof(IEmployeeRepository.GetByIdAsync), typeof(Task<Employee?>),
            ("id", typeof(Guid)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IEmployeeRepository.GetByCompanyAsync), typeof(Task<IReadOnlyList<Employee>>),
            ("companyId", typeof(Guid)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IEmployeeRepository.GetActiveByCompanyAsync), typeof(Task<IReadOnlyList<Employee>>),
            ("companyId", typeof(Guid)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IEmployeeRepository.GetByIdsAsync), typeof(Task<IReadOnlyList<Employee>>),
            ("ids", typeof(IEnumerable<Guid>)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IEmployeeRepository.AddAsync), typeof(Task),
            ("employee", typeof(Employee)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IEmployeeRepository.Update), typeof(void),
            ("employee", typeof(Employee)));
        contract.GetMethods().Should().HaveCount(6);
    }

    [Fact]
    public void RosterRepository_WhenInspected_ShouldExposeExpectedSpecificMethods()
    {
        // Arrange
        var contract = typeof(IRosterRepository);

        // Act & Assert
        AssertRepositoryInterface(contract);
        AssertMethod(contract, nameof(IRosterRepository.GetByIdWithShiftsAsync), typeof(Task<Roster?>),
            ("id", typeof(Guid)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IRosterRepository.GetByCompanyAndWeekStartAsync), typeof(Task<Roster?>),
            ("companyId", typeof(Guid)), ("weekStartDate", typeof(DateOnly)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IRosterRepository.GetByWeekRangeAsync), typeof(Task<IReadOnlyList<Roster>>),
            ("companyId", typeof(Guid)), ("startDate", typeof(DateOnly)), ("endDate", typeof(DateOnly)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IRosterRepository.AddAsync), typeof(Task),
            ("roster", typeof(Roster)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IRosterRepository.Update), typeof(void),
            ("roster", typeof(Roster)));
        contract.GetMethods().Should().HaveCount(5);
    }

    [Fact]
    public void ShiftSwapRequestRepository_WhenInspected_ShouldExposeExpectedSpecificMethods()
    {
        // Arrange
        var contract = typeof(IShiftSwapRequestRepository);

        // Act & Assert
        AssertRepositoryInterface(contract);
        AssertMethod(contract, nameof(IShiftSwapRequestRepository.GetByIdAsync), typeof(Task<ShiftSwapRequest?>),
            ("id", typeof(Guid)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IShiftSwapRequestRepository.GetByRosterAsync), typeof(Task<IReadOnlyList<ShiftSwapRequest>>),
            ("rosterId", typeof(Guid)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IShiftSwapRequestRepository.GetPendingByCompanyAsync), typeof(Task<IReadOnlyList<ShiftSwapRequest>>),
            ("companyId", typeof(Guid)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IShiftSwapRequestRepository.HasActiveRequestForShiftAsync), typeof(Task<bool>),
            ("shiftId", typeof(Guid)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IShiftSwapRequestRepository.AddAsync), typeof(Task),
            ("request", typeof(ShiftSwapRequest)), ("ct", typeof(CancellationToken)));
        AssertMethod(contract, nameof(IShiftSwapRequestRepository.Update), typeof(void),
            ("request", typeof(ShiftSwapRequest)));
        contract.GetMethods().Should().HaveCount(6);
    }

    private static void AssertRepositoryInterface(Type contract)
    {
        contract.IsInterface.Should().BeTrue();
        contract.IsPublic.Should().BeTrue();
        contract.IsGenericType.Should().BeFalse();
        contract.Namespace.Should().Be("Domain.Repositories");
    }

    private static void AssertMethod(
        Type contract,
        string methodName,
        Type expectedReturnType,
        params (string Name, Type Type)[] expectedParameters)
    {
        var method = contract.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull($"o contrato {contract.Name} deve declarar {methodName}");
        method!.ReturnType.Should().Be(expectedReturnType);

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(expectedParameters.Length);

        for (var i = 0; i < expectedParameters.Length; i++)
        {
            parameters[i].Name.Should().Be(expectedParameters[i].Name);
            parameters[i].ParameterType.Should().Be(expectedParameters[i].Type);

            if (expectedParameters[i].Type == typeof(CancellationToken))
            {
                parameters[i].HasDefaultValue.Should().BeTrue();
                parameters[i].DefaultValue.Should().BeNull();
            }
        }
    }
}

