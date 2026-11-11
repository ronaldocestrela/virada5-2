using Application.Common.Interfaces;
using Application.UseCases.Financial;
using Application.UseCases.Rosters;
using Bunit;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Blazor.Components.Pages.Financial;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Pages;

public class FinancialSimulatorTests : BunitContext
{
    private readonly InMemoryCompanyRepository _companyRepository = new();
    private readonly InMemoryEmployeeRepository _employeeRepository = new();
    private readonly InMemoryDemandSlotRepository _demandSlotRepository = new();
    private readonly InMemoryRosterRepository _rosterRepository = new();
    private readonly StubCurrentUserService _currentUserService = new();
    private readonly StubUnitOfWork _unitOfWork = new();
    private readonly ToastService _toastService = new();

    public FinancialSimulatorTests()
    {
        Services.AddSingleton<ICompanyRepository>(_companyRepository);
        Services.AddSingleton<IEmployeeRepository>(_employeeRepository);
        Services.AddSingleton<IDemandSlotRepository>(_demandSlotRepository);
        Services.AddSingleton<IRosterRepository>(_rosterRepository);
        Services.AddSingleton<ICurrentUserService>(_currentUserService);
        Services.AddSingleton<IUnitOfWork>(_unitOfWork);
        Services.AddScoped<GetCurrentFinancialSimulationContextUseCase>();
        Services.AddScoped<SimulateFinancialImpactUseCase>();
        Services.AddScoped<GenerateRoster5x2UseCase>();
        Services.AddSingleton<IToastService>(_toastService);
    }

    [Fact]
    public void Render_WhenCompanyNotConfigured_ShouldDisplayOnboardingRequiredCallToAction()
    {
        // Act
        var cut = Render<FinancialSimulator>();

        // Assert
        cut.Find("h1").TextContent.Should().Contain("Simulador de Impacto Financeiro");
        cut.Find("[data-testid=\"financial-onboarding-required\"]").TextContent.Should().Contain("Onboarding");
        cut.Markup.Should().Contain("/onboarding");
    }

    [Fact]
    public void Render_WhenCompanyHasNoEmployeesAndNoDemand_ShouldRequireExplicitHeadcountAndShowDemandHint()
    {
        // Arrange
        SeedCompany();

        // Act
        var cut = Render<FinancialSimulator>();

        // Assert
        cut.Find("[data-testid=\"financial-demand-missing-hint\"]").TextContent.Should().Contain("demanda");
        cut.Find("[data-testid=\"financial-headcount-input\"]").Should().NotBeNull();
        cut.Find(".financial-card__empty").TextContent.Should().Contain("Nenhuma simulação financeira");
    }

    [Fact]
    public void Render_WhenCompanyHasActiveEmployees_ShouldDefaultToActiveRosterHeadcountMode()
    {
        // Arrange
        var company = SeedCompany();
        SeedEmployee(company.Id, "Ana", StationRole.Waiter);
        SeedEmployee(company.Id, "Bruno", StationRole.Kitchen);

        // Act
        var cut = Render<FinancialSimulator>();

        // Assert
        var rosterRadio = cut.Find("[data-testid=\"headcount-mode-roster\"]");
        rosterRadio.GetAttribute("checked").Should().NotBeNull();
        cut.Find("[data-testid=\"financial-roster-badge\"]").TextContent.Should().Contain("2");
    }

    [Fact]
    public async Task Submit_WhenValidInputsAndNoEmployees_ShouldRunSimulationAndRenderFinancialComparisonCard()
    {
        // Arrange
        SeedCompany();
        var cut = Render<FinancialSimulator>();

        cut.Find("[data-testid=\"financial-salary-input\"]").Change("2400");
        cut.Find("[data-testid=\"financial-overtime-input\"]").Change("18");
        cut.Find("[data-testid=\"financial-headcount-input\"]").Change("10");

        // Act
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"financial-simulation-form\"]").Submit());

        // Assert
        cut.Find(".financial-card__savings-monthly").TextContent.Should().Contain("R$");
        cut.Find(".financial-card__savings-annual").TextContent.Should().Contain("R$");
        cut.Find(".financial-card__warnings").TextContent.Should().Contain("Nenhuma matriz de demanda");
        _toastService.Messages.Should().ContainSingle(m => m.Severity == AppToastSeverity.Success);
    }

    [Fact]
    public async Task Submit_WhenUsingActiveEmployeesAndCustomAssumptions_ShouldApplyAssumptionsAndUpdateSavings()
    {
        // Arrange
        var company = SeedCompany();
        SeedEmployee(company.Id, "Ana", StationRole.Waiter);
        SeedEmployee(company.Id, "Bruno", StationRole.Kitchen);
        SeedOpenDemand(company.Id);

        var cut = Render<FinancialSimulator>();

        cut.Find("[data-testid=\"financial-salary-input\"]").Change("2500");
        cut.Find("[data-testid=\"financial-overtime-input\"]").Change("20");
        cut.Find("[data-testid=\"toggle-advanced-assumptions\"]").Click();
        cut.Find("[data-testid=\"assumption-overtime-reduction\"]").Change("85");

        // Act
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"financial-simulation-form\"]").Submit());

        // Assert
        cut.Find(".financial-card__idle-exposure").TextContent.Should().Contain("50,0%");
        cut.Markup.Should().Contain("2 colaboradores");
        cut.Find(".financial-card__savings-monthly").TextContent.Should().Contain("R$");
    }

    [Fact]
    public void Click_WhenResetAssumptionsClicked_ShouldRestoreDefaultAssumptions()
    {
        // Arrange
        SeedCompany();
        var cut = Render<FinancialSimulator>();
        cut.Find("[data-testid=\"toggle-advanced-assumptions\"]").Click();
        cut.Find("[data-testid=\"assumption-employer-charges\"]").Change("95");

        // Act
        cut.Find("[data-testid=\"reset-assumptions-btn\"]").Click();

        // Assert
        cut.Find("[data-testid=\"assumption-employer-charges\"]").GetAttribute("value").Should().Be("68");
        cut.Find("[data-testid=\"assumption-work-hours-divisor\"]").GetAttribute("value").Should().Be("220");
    }

    [Fact]
    public async Task Submit_WhenInvalidSalary_ShouldDisplayValidationAlertAndNotThrow()
    {
        // Arrange
        SeedCompany();
        var cut = Render<FinancialSimulator>();

        cut.Find("[data-testid=\"financial-salary-input\"]").Change("0");
        cut.Find("[data-testid=\"financial-overtime-input\"]").Change("10");
        cut.Find("[data-testid=\"financial-headcount-input\"]").Change("8");

        // Act
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"financial-simulation-form\"]").Submit());

        // Assert
        cut.Find("[data-testid=\"financial-error-alert\"]").TextContent.Should().Contain("salário");
        cut.FindAll(".financial-card__savings-monthly").Should().BeEmpty();
    }

    [Fact]
    public async Task PrimaryCta_WhenNoActiveEmployees_ShouldOpenInformativeModalWithoutCallingRosterGenerator()
    {
        // Arrange
        SeedCompany();
        var cut = Render<FinancialSimulator>();

        cut.Find("[data-testid=\"financial-salary-input\"]").Change("2200");
        cut.Find("[data-testid=\"financial-overtime-input\"]").Change("16");
        cut.Find("[data-testid=\"financial-headcount-input\"]").Change("6");
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"financial-simulation-form\"]").Submit());

        // Act
        cut.Find(".app-card__footer button").Click();

        // Assert
        var modal = cut.Find("[data-testid=\"employees-required-modal\"]");
        modal.TextContent.Should().Contain("colaboradores");
        modal.InnerHtml.Should().Contain("/employees");
        _rosterRepository.Rosters.Should().BeEmpty();
        _unitOfWork.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task PrimaryCta_WhenActiveEmployeesExist_ShouldGenerateRosterAndRenderSummary()
    {
        // Arrange
        var company = SeedCompany();
        SeedEmployee(company.Id, "Ana", StationRole.Waiter);
        SeedEmployee(company.Id, "Bruno", StationRole.Kitchen);
        SeedOpenDemand(company.Id);

        var cut = Render<FinancialSimulator>();
        cut.Find("[data-testid=\"financial-salary-input\"]").Change("2200");
        cut.Find("[data-testid=\"financial-overtime-input\"]").Change("16");
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"financial-simulation-form\"]").Submit());

        // Act
        await cut.InvokeAsync(() => cut.Find(".app-card__footer button").Click());

        // Assert
        _rosterRepository.Rosters.Should().ContainSingle();
        _unitOfWork.SaveChangesCount.Should().Be(1);
        cut.Find("[data-testid=\"generated-roster-summary\"]").TextContent.Should().Contain("Escala 5x2 gerada");
    }

    [Fact]
    public async Task PrimaryCta_WhenDraftAlreadyExists_ShouldAskConfirmationAndReplaceDraft()
    {
        // Arrange
        var company = SeedCompany();
        SeedEmployee(company.Id, "Ana", StationRole.Waiter);
        SeedEmployee(company.Id, "Bruno", StationRole.Kitchen);
        SeedOpenDemand(company.Id);

        var nextMonday = ResolveNextMonday();
        var existingDraft = Roster.Create(
            _currentUserService.TenantId,
            company.Id,
            nextMonday).Value;
        _rosterRepository.Seed(existingDraft);

        var cut = Render<FinancialSimulator>();
        cut.Find("[data-testid=\"financial-salary-input\"]").Change("2200");
        cut.Find("[data-testid=\"financial-overtime-input\"]").Change("16");
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"financial-simulation-form\"]").Submit());

        // Act 1: solicita geração e detecta rascunho existente
        await cut.InvokeAsync(() => cut.Find(".app-card__footer button").Click());

        // Assert 1
        cut.Find("[data-testid=\"replace-draft-modal\"]").TextContent.Should().Contain("rascunho");

        // Act 2: confirma substituição
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"confirm-replace-draft-btn\"]").Click());

        // Assert 2
        _unitOfWork.SaveChangesCount.Should().Be(1);
        cut.Find("[data-testid=\"generated-roster-summary\"]").TextContent.Should().Contain("Escala 5x2 gerada");
    }

    private static DateOnly ResolveNextMonday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return daysUntilMonday == 0 ? today.AddDays(7) : today.AddDays(daysUntilMonday);
    }

    private Company SeedCompany()
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Bistrô Central",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(15, 0),
            ScheduleModel.RotatingDaysOff).Value;

        _companyRepository.Seed(company);
        return company;
    }

    private Employee SeedEmployee(Guid companyId, string name, StationRole primaryRole)
    {
        var employee = Employee.Create(
            _currentUserService.TenantId,
            companyId,
            name,
            primaryRole).Value;

        _employeeRepository.Seed(employee);
        return employee;
    }

    private void SeedOpenDemand(Guid companyId)
    {
        _demandSlotRepository.Seed(
            DemandSlot.Create(_currentUserService.TenantId, companyId, DayOfWeek.Tuesday, 12, 10m).Value,
            DemandSlot.Create(_currentUserService.TenantId, companyId, DayOfWeek.Friday, 12, 100m).Value);
    }

    private sealed class InMemoryCompanyRepository : ICompanyRepository
    {
        private readonly List<Company> _companies = [];

        public void Seed(Company company) => _companies.Add(company);

        public Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_companies.FirstOrDefault(c => c.Id == id));

        public Task<Company?> GetCurrentAsync(CancellationToken ct = default) =>
            Task.FromResult(_companies.FirstOrDefault());

        public Task AddAsync(Company company, CancellationToken ct = default)
        {
            _companies.Add(company);
            return Task.CompletedTask;
        }

        public void Update(Company company)
        {
        }
    }

    private sealed class InMemoryEmployeeRepository : IEmployeeRepository
    {
        private readonly List<Employee> _employees = [];

        public void Seed(Employee employee) => _employees.Add(employee);

        public Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_employees.FirstOrDefault(e => e.Id == id));

        public Task<IReadOnlyList<Employee>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Employee>>(_employees.Where(e => e.CompanyId == companyId).ToList());

        public Task<IReadOnlyList<Employee>> GetActiveByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Employee>>(_employees.Where(e => e.CompanyId == companyId && e.IsActive).ToList());

        public Task<IReadOnlyList<Employee>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            var idSet = ids.ToHashSet();
            return Task.FromResult<IReadOnlyList<Employee>>(_employees.Where(e => idSet.Contains(e.Id)).ToList());
        }

        public Task AddAsync(Employee employee, CancellationToken ct = default)
        {
            _employees.Add(employee);
            return Task.CompletedTask;
        }

        public void Update(Employee employee)
        {
        }
    }

    private sealed class InMemoryDemandSlotRepository : IDemandSlotRepository
    {
        private readonly List<DemandSlot> _slots = [];

        public void Seed(params DemandSlot[] slots) => _slots.AddRange(slots);

        public Task<DemandSlot?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_slots.FirstOrDefault(s => s.Id == id));

        public Task<DemandSlot?> GetByCompanyDayAndHourAsync(
            Guid companyId,
            DayOfWeek dayOfWeek,
            int hour,
            CancellationToken ct = default) =>
            Task.FromResult(_slots.FirstOrDefault(s =>
                s.CompanyId == companyId &&
                s.DayOfWeek == dayOfWeek &&
                s.Hour == hour));

        public Task<IReadOnlyList<DemandSlot>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DemandSlot>>(_slots.Where(s => s.CompanyId == companyId).ToList());

        public Task AddAsync(DemandSlot demandSlot, CancellationToken ct = default)
        {
            _slots.Add(demandSlot);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<DemandSlot> demandSlots, CancellationToken ct = default)
        {
            _slots.AddRange(demandSlots);
            return Task.CompletedTask;
        }

        public void Update(DemandSlot demandSlot)
        {
        }

        public void RemoveRange(IEnumerable<DemandSlot> demandSlots)
        {
            foreach (var slot in demandSlots.ToList())
            {
                _slots.Remove(slot);
            }
        }
    }

    private sealed class InMemoryRosterRepository : IRosterRepository
    {
        public List<Roster> Rosters { get; } = [];

        public void Seed(Roster roster) => Rosters.Add(roster);

        public Task<Roster?> GetByIdWithShiftsAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Rosters.FirstOrDefault(r => r.Id == id));

        public Task<Roster?> GetByCompanyAndWeekStartAsync(
            Guid companyId,
            DateOnly weekStartDate,
            CancellationToken ct = default) =>
            Task.FromResult(Rosters.FirstOrDefault(r =>
                r.CompanyId == companyId &&
                r.WeekStartDate == weekStartDate &&
                r.Status != RosterStatus.Archived));

        public Task<IReadOnlyList<Roster>> GetByWeekRangeAsync(
            Guid companyId,
            DateOnly startDate,
            DateOnly endDate,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Roster>>(Rosters.Where(r =>
                r.CompanyId == companyId &&
                r.WeekStartDate >= startDate &&
                r.WeekStartDate <= endDate &&
                r.Status != RosterStatus.Archived).ToList());

        public Task AddAsync(Roster roster, CancellationToken ct = default)
        {
            Rosters.Add(roster);
            return Task.CompletedTask;
        }

        public void Update(Roster roster)
        {
        }
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid TenantId { get; } = Guid.NewGuid();
        public bool IsAuthenticated => true;
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }
    }
}
