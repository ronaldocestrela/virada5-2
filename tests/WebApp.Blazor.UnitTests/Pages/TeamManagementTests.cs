using Application.Common.Interfaces;
using Application.UseCases.Employees;
using Bunit;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Blazor.Components.Pages.Employees;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Pages;

public class TeamManagementTests : BunitContext
{
    private readonly InMemoryCompanyRepository _companyRepository = new();
    private readonly InMemoryEmployeeRepository _employeeRepository = new();
    private readonly StubCurrentUserService _currentUserService = new();
    private readonly StubUnitOfWork _unitOfWork = new();
    private readonly ToastService _toastService = new();

    public TeamManagementTests()
    {
        Services.AddSingleton<ICompanyRepository>(_companyRepository);
        Services.AddSingleton<IEmployeeRepository>(_employeeRepository);
        Services.AddSingleton<ICurrentUserService>(_currentUserService);
        Services.AddSingleton<IUnitOfWork>(_unitOfWork);
        Services.AddScoped<GetCurrentEmployeesWorkspaceUseCase>();
        Services.AddScoped<CreateEmployeeUseCase>();
        Services.AddScoped<UpdateEmployeeUseCase>();
        Services.AddScoped<SetEmployeeActiveStatusUseCase>();
        Services.AddSingleton<IToastService>(_toastService);
    }

    [Fact]
    public void Render_WhenCompanyNotConfigured_ShouldDisplayOnboardingRequiredCallToAction()
    {
        // Act
        var cut = Render<TeamManagement>();

        // Assert
        cut.Find("h1").TextContent.Should().Contain("Gestão da Equipe");
        cut.Find("[data-testid=\"employees-onboarding-required\"]").TextContent.Should().Contain("Onboarding");
        cut.Markup.Should().Contain("/onboarding");
    }

    [Fact]
    public void Render_WhenCompanyHasNoEmployees_ShouldDisplayEmptyStateAndQuickAddForm()
    {
        // Arrange
        SeedCompany();

        // Act
        var cut = Render<TeamManagement>();

        // Assert
        cut.Find("[data-testid=\"employee-quick-add-form\"]").Should().NotBeNull();
        cut.Find("[data-testid=\"employees-empty-state\"]").TextContent.Should().Contain("Nenhum colaborador");
        cut.Find("[data-testid=\"employees-active-badge\"]").TextContent.Should().Contain("0");
    }

    [Fact]
    public async Task QuickAdd_WhenValidNameRolePolyvalenceAndConstraints_ShouldPersistEmployeeRefreshGridAndClearForm()
    {
        // Arrange
        SeedCompany();
        var cut = Render<TeamManagement>();

        cut.Find("[data-testid=\"quick-add-name\"]").Change("Mariana Silva");
        cut.Find("[data-testid=\"quick-add-primary-role\"]").Change(StationRole.Waiter.ToString());

        // 1 clique = Apoio, 2 cliques = Autônomo
        cut.Find("[data-testid=\"quick-add-skill-Bar\"]").Click();
        cut.Find("[data-testid=\"quick-add-skill-Cashier\"]").Click();
        cut.Find("[data-testid=\"quick-add-skill-Cashier\"]").Click();

        cut.Find("[data-testid=\"quick-add-unavailable-Sunday\"]").Click();
        cut.Find("[data-testid=\"quick-add-preferred-Monday\"]").Click();

        // Act
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"employee-quick-add-form\"]").Submit());

        // Assert
        _employeeRepository.Employees.Should().ContainSingle();
        var saved = _employeeRepository.Employees.Single();
        saved.Name.Should().Be("Mariana Silva");
        saved.PrimaryRole.Should().Be(StationRole.Waiter);
        saved.SecondarySkills.Should().Contain(s => s.Role == StationRole.Bar && s.Proficiency == SkillProficiency.Support);
        saved.SecondarySkills.Should().Contain(s => s.Role == StationRole.Cashier && s.Proficiency == SkillProficiency.Autonomous);
        saved.UnavailableDays.Should().Equal(DayOfWeek.Sunday);
        saved.PreferredDaysOff.Should().Equal(DayOfWeek.Monday);
        _unitOfWork.SaveChangesCount.Should().Be(1);

        var row = cut.Find($"[data-testid=\"employee-row-{saved.Id}\"]");
        row.TextContent.Should().Contain("Mariana Silva");
        row.TextContent.Should().Contain("Bar");
        row.TextContent.Should().Contain("Apoio");
        row.TextContent.Should().Contain("Caixa");
        row.TextContent.Should().Contain("Autônomo");
        row.TextContent.Should().Contain("Dom");
        row.TextContent.Should().Contain("Seg");

        cut.Find("[data-testid=\"quick-add-name\"]").GetAttribute("value").Should().BeEmpty();
        _toastService.Messages.Should().ContainSingle(m => m.Severity == AppToastSeverity.Success);
    }

    [Fact]
    public async Task QuickAdd_WhenNameIsEmpty_ShouldDisplayValidationAlertAndNotPersist()
    {
        // Arrange
        SeedCompany();
        var cut = Render<TeamManagement>();

        // Act
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"employee-quick-add-form\"]").Submit());

        // Assert
        cut.Find("[data-testid=\"employees-error-alert\"]").TextContent.Should().Contain("nome");
        _employeeRepository.Employees.Should().BeEmpty();
        _unitOfWork.SaveChangesCount.Should().Be(0);
        _toastService.Messages.Should().ContainSingle(m => m.Severity == AppToastSeverity.Error);
    }

    [Fact]
    public void Filters_WhenFilteringBySearchRoleAndStatus_ShouldFilterVisibleRows()
    {
        // Arrange
        var company = SeedCompany();
        var ana = SeedEmployee(
            company.Id,
            "Ana Paula",
            StationRole.Waiter,
            [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value]);
        var bruno = SeedEmployee(company.Id, "Bruno Chef", StationRole.Kitchen);
        var carlos = SeedEmployee(company.Id, "Carlos Inativo", StationRole.Bar);
        carlos.Deactivate();

        var cut = Render<TeamManagement>();

        // Assert default shows active only (2 rows)
        cut.FindAll("tbody tr").Should().HaveCount(2);

        // Filter by role Bar (Ana has Bar as secondary skill!)
        cut.Find("[data-testid=\"employees-role-filter\"]").Change(StationRole.Bar.ToString());
        cut.FindAll($"[data-testid=\"employee-row-{ana.Id}\"]").Should().ContainSingle();
        cut.FindAll($"[data-testid=\"employee-row-{bruno.Id}\"]").Should().BeEmpty();

        // Reset role filter & show all statuses
        cut.Find("[data-testid=\"employees-role-filter\"]").Change(string.Empty);
        cut.Find("[data-testid=\"employees-status-filter\"]").Change("all");
        cut.FindAll("tbody tr").Should().HaveCount(3);

        // Search by name
        cut.Find("[data-testid=\"employees-search-input\"]").Input("Carlos");
        cut.FindAll($"[data-testid=\"employee-row-{carlos.Id}\"]").Should().ContainSingle();
        cut.FindAll("tbody tr").Should().HaveCount(1);
    }

    [Fact]
    public async Task EditModal_WhenUpdatingEmployeeSkillsAndConstraints_ShouldPersistAndRefreshRow()
    {
        // Arrange
        var company = SeedCompany();
        var employee = SeedEmployee(company.Id, "Lucas Ferreira", StationRole.KitchenAssistant);
        var cut = Render<TeamManagement>();

        // Act 1: abrir modal de edição
        cut.Find($"[data-testid=\"edit-employee-{employee.Id}\"]").Click();
        cut.Find("[data-testid=\"edit-employee-modal\"]").Should().NotBeNull();

        cut.Find("[data-testid=\"edit-employee-name\"]").Change("Lucas Ferreira Jr.");
        cut.Find("[data-testid=\"edit-employee-primary-role\"]").Change(StationRole.Kitchen.ToString());
        cut.Find("[data-testid=\"edit-employee-skill-Expedition\"]").Click();
        cut.Find("[data-testid=\"edit-employee-skill-Expedition\"]").Click();
        cut.Find("[data-testid=\"edit-employee-unavailable-Saturday\"]").Click();
        cut.Find("[data-testid=\"edit-employee-preferred-Tuesday\"]").Click();

        // Act 2: salvar
        await cut.InvokeAsync(() => cut.Find("[data-testid=\"edit-employee-form\"]").Submit());

        // Assert
        employee.Name.Should().Be("Lucas Ferreira Jr.");
        employee.PrimaryRole.Should().Be(StationRole.Kitchen);
        employee.SecondarySkills.Should().ContainSingle(s =>
            s.Role == StationRole.Expedition && s.Proficiency == SkillProficiency.Autonomous);
        employee.UnavailableDays.Should().Equal(DayOfWeek.Saturday);
        employee.PreferredDaysOff.Should().Equal(DayOfWeek.Tuesday);
        _unitOfWork.SaveChangesCount.Should().Be(1);

        cut.FindAll("[data-testid=\"edit-employee-modal\"]").Should().BeEmpty();
        var row = cut.Find($"[data-testid=\"employee-row-{employee.Id}\"]");
        row.TextContent.Should().Contain("Lucas Ferreira Jr.");
        row.TextContent.Should().Contain("Expedição");
        row.TextContent.Should().Contain("Sáb");
        row.TextContent.Should().Contain("Ter");
    }

    [Fact]
    public async Task ToggleStatus_WhenDeactivatingAndReactivatingEmployee_ShouldUpdateStatusAndBadge()
    {
        // Arrange
        var company = SeedCompany();
        var employee = SeedEmployee(company.Id, "Patrícia Gomes", StationRole.Cashier);
        var cut = Render<TeamManagement>();

        // Act 1: desativar
        await cut.InvokeAsync(() => cut.Find($"[data-testid=\"toggle-status-{employee.Id}\"]").Click());

        // Assert 1
        employee.IsActive.Should().BeFalse();
        _unitOfWork.SaveChangesCount.Should().Be(1);
        cut.Find("[data-testid=\"employees-active-badge\"]").TextContent.Should().Contain("0");

        // Act 2: mostrar inativos e reativar
        cut.Find("[data-testid=\"employees-status-filter\"]").Change("inactive");
        await cut.InvokeAsync(() => cut.Find($"[data-testid=\"toggle-status-{employee.Id}\"]").Click());

        // Assert 2
        employee.IsActive.Should().BeTrue();
        _unitOfWork.SaveChangesCount.Should().Be(2);
        cut.Find("[data-testid=\"employees-active-badge\"]").TextContent.Should().Contain("1");
    }

    private Company SeedCompany()
    {
        var company = Company.Create(
            _currentUserService.TenantId,
            "Bistrô Central",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.FixedClosure,
            [DayOfWeek.Monday]).Value;

        _companyRepository.Seed(company);
        return company;
    }

    private Employee SeedEmployee(
        Guid companyId,
        string name,
        StationRole primaryRole,
        IEnumerable<SecondarySkill>? secondarySkills = null)
    {
        var employee = Employee.Create(
            _currentUserService.TenantId,
            companyId,
            name,
            primaryRole,
            secondarySkills).Value;

        _employeeRepository.Seed(employee);
        return employee;
    }

    private sealed class InMemoryCompanyRepository : ICompanyRepository
    {
        private readonly List<Company> _companies = [];

        public void Seed(Company company) => _companies.Add(company);

        public Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_companies.FirstOrDefault(c => c.Id == id));

        public Task<Company?> GetCurrentAsync(CancellationToken ct = default) =>
            Task.FromResult(_companies.SingleOrDefault());

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
        public List<Employee> Employees { get; } = [];

        public void Seed(Employee employee) => Employees.Add(employee);

        public Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Employees.FirstOrDefault(e => e.Id == id));

        public Task<IReadOnlyList<Employee>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Employee>>(Employees
                .Where(e => e.CompanyId == companyId)
                .OrderBy(e => e.Name)
                .ToList());

        public Task<IReadOnlyList<Employee>> GetActiveByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Employee>>(Employees
                .Where(e => e.CompanyId == companyId && e.IsActive)
                .OrderBy(e => e.Name)
                .ToList());

        public Task<IReadOnlyList<Employee>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            var idSet = ids.ToHashSet();
            return Task.FromResult<IReadOnlyList<Employee>>(Employees.Where(e => idSet.Contains(e.Id)).ToList());
        }

        public Task AddAsync(Employee employee, CancellationToken ct = default)
        {
            Employees.Add(employee);
            return Task.CompletedTask;
        }

        public void Update(Employee employee)
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

