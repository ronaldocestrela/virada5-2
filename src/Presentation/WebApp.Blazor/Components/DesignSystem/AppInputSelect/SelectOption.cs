namespace WebApp.Blazor.Components.DesignSystem.AppInputSelect;

public sealed record SelectOption<TValue>(
    TValue Value,
    string Label,
    bool Disabled = false);

