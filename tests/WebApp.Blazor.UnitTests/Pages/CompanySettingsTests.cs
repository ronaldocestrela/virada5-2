using Application.Common.Interfaces;
using Application.UseCases.Companies;
using Bunit;
using Domain.Entities;
using Domain.Enums;
using Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Blazor.Components.Pages.Company;
using WebApp.Blazor.Services;

namespace WebApp.Blazor.UnitTests.Pages;

public class CompanySettingsTests : BunitContext
{
    private readonly InMemoryCompanyRepository _companyRepository = new();
    private readonly StubCurrentUserService _currentUserService = new();
    private readonly StubUnitOfWork _unitOfWork = new();
    private readonly ToastService _toastService = new();

    public CompanySettingsTests()
    {
        Services.AddSingleton<ICompanyRepository>(_companyRepository);
        Services.AddSingleton<ICurrentUserService>(_currentUserService);
        Services.AddSingleton<IUnitOfWork>(_unitOfWork);
        Services.AddScoped<GetCurrentCompanySettingsUseCase>();
        Services.AddScoped<SaveCompanySettingsUseCase>();
        Services.AddSingleton<IToastService>(_toastService);
    }

    [Fact]
    public void Render_WhenCompanyNotConfigured_ShouldDisplayOnboardingModeAndPendingBadge()
    {
        // Act
        var cut = Render<CompanySettings>();

        // Assert
        cut.Find("h1").TextContent.Should().Contain("Onboarding da Casa");
        cut.Markup.Should().Contain("Configuração pendente");
        cut.Find("button[type=\"submit\"]").TextContent.Should().Contain("Concluir Onboarding");
        cut.Find("#company-name").GetAttribute("value").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenCompanyExists_ShouldPrepopulateAllFieldsInSettingsMode()
    {
        // Arrange
        var company = Company.Create(
            _currentUserService.TenantId,
            "Cantina Bella",
            BusinessType.Restaurant,
            new TimeOnly(11, 30),
            new TimeOnly(23, 15),
            ScheduleModel.FixedClosure,
            [DayOfWeek.Monday, DayOfWeek.Tuesday]).Value;
        _companyRepository.Seed(company);

        // Act
        var cut = Render<CompanySettings>();

        // Assert
        cut.Find("h1").TextContent.Should().Contain("Configurações da Casa");
        cut.Markup.Should().Contain("Casa configurada");
        cut.Find("button[type=\"submit\"]").TextContent.Should().Contain("Salvar Configurações");
        cut.Find("#company-name").GetAttribute("value").Should().Be("Cantina Bella");
        cut.Find("#company-business-type").GetAttribute("value").Should().Be(BusinessType.Restaurant.ToString());
        cut.Find("#company-opening-time").GetAttribute("value").Should().Be("11:30");
        cut.Find("#company-closing-time").GetAttribute("value").Should().Be("23:15");
        cut.Find("#schedule-model-fixed").HasAttribute("checked").Should().BeTrue();
        cut.Find("#closed-day-Monday").HasAttribute("checked").Should().BeTrue();
        cut.Find("#closed-day-Tuesday").HasAttribute("checked").Should().BeTrue();
        cut.Find("#closed-day-Friday").HasAttribute("checked").Should().BeFalse();
    }

    [Fact]
    public void ScheduleModelSelection_WhenSwitchedToFixedClosure_ShouldRevealClosedDaysSelector()
    {
        // Arrange
        var cut = Render<CompanySettings>();
        cut.FindAll("#closed-day-Monday").Should().BeEmpty();

        // Act
        cut.Find("#schedule-model-fixed").Change(true);

        // Assert
        cut.Find("#closed-day-Monday").Should().NotBeNull();
        cut.Find("#closed-day-Sunday").Should().NotBeNull();
    }

    [Fact]
    public void OperatingHours_WhenClosingTimeIsAfterMidnight_ShouldShowOvernightIndicator()
    {
        // Arrange
        var cut = Render<CompanySettings>();

        // Act
        cut.Find("#company-opening-time").Change("18:00");
        cut.Find("#company-closing-time").Change("02:00");

        // Assert
        cut.Find("[data-testid=\"overnight-indicator\"]").TextContent.Should().Contain("meia-noite");
    }

    [Fact]
    public async Task Submit_WhenOnboardingFormIsValid_ShouldPersistCompanyAndShowSuccessToast()
    {
        // Arrange
        var cut = Render<CompanySettings>();

        cut.Find("#company-name").Change("Bar da Virada");
        cut.Find("#company-business-type").Change(BusinessType.Bar.ToString());
        cut.Find("#company-opening-time").Change("18:00");
        cut.Find("#company-closing-time").Change("02:00");
        cut.Find("#schedule-model-fixed").Change(true);
        cut.Find("#closed-day-Monday").Change(true);

        // Act
        await cut.Find("form").SubmitAsync();

        // Assert
        _companyRepository.CurrentCompany.Should().NotBeNull();
        _companyRepository.CurrentCompany!.Name.Should().Be("Bar da Virada");
        _companyRepository.CurrentCompany.BusinessType.Should().Be(BusinessType.Bar);
        _companyRepository.CurrentCompany.OpeningTime.Should().Be(new TimeOnly(18, 0));
        _companyRepository.CurrentCompany.ClosingTime.Should().Be(new TimeOnly(2, 0));
        _companyRepository.CurrentCompany.ScheduleModel.Should().Be(ScheduleModel.FixedClosure);
        _companyRepository.CurrentCompany.ClosedDays.Should().Equal(DayOfWeek.Monday);
        _unitOfWork.SaveChangesCount.Should().Be(1);

        _toastService.Messages.Should().ContainSingle(m => m.Severity == AppToastSeverity.Success);
        cut.Find("h1").TextContent.Should().Contain("Configurações da Casa");
    }

    [Fact]
    public async Task Submit_WhenOperatingHoursAreEqual_ShouldNotPersistAndShowErrorFeedback()
    {
        // Arrange
        var cut = Render<CompanySettings>();

        cut.Find("#company-name").Change("Restaurante Teste");
        cut.Find("#company-business-type").Change(BusinessType.Restaurant.ToString());
        cut.Find("#company-opening-time").Change("11:00");
        cut.Find("#company-closing-time").Change("11:00");

        // Act
        await cut.Find("form").SubmitAsync();

        // Assert
        _companyRepository.CurrentCompany.Should().BeNull();
        _unitOfWork.SaveChangesCount.Should().Be(0);
        _toastService.Messages.Should().ContainSingle(m => m.Severity == AppToastSeverity.Error);
        cut.Find("[role=\"alert\"]").TextContent.Should().Contain("abertura");
    }

    [Fact]
    public async Task Submit_WhenFixedClosureHasNoClosedDays_ShouldNotPersistAndShowValidationError()
    {
        // Arrange
        var cut = Render<CompanySettings>();

        cut.Find("#company-name").Change("Restaurante Teste");
        cut.Find("#company-business-type").Change(BusinessType.Restaurant.ToString());
        cut.Find("#company-opening-time").Change("11:00");
        cut.Find("#company-closing-time").Change("23:00");
        cut.Find("#schedule-model-fixed").Change(true);

        // Act
        await cut.Find("form").SubmitAsync();

        // Assert
        _companyRepository.CurrentCompany.Should().BeNull();
        _unitOfWork.SaveChangesCount.Should().Be(0);
        cut.Markup.Should().Contain("ao menos um dia de fechamento");
    }

    private sealed class InMemoryCompanyRepository : ICompanyRepository
    {
        public Company? CurrentCompany { get; private set; }

        public void Seed(Company company) => CurrentCompany = company;

        public Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(CurrentCompany?.Id == id ? CurrentCompany : null);

        public Task<Company?> GetCurrentAsync(CancellationToken ct = default) =>
            Task.FromResult(CurrentCompany);

        public Task AddAsync(Company company, CancellationToken ct = default)
        {
            CurrentCompany = company;
            return Task.CompletedTask;
        }

        public void Update(Company company) => CurrentCompany = company;
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

