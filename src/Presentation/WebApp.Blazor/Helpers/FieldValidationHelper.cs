using System.Linq.Expressions;
using Microsoft.AspNetCore.Components.Forms;

namespace WebApp.Blazor.Helpers;

internal static class FieldValidationHelper
{
    public static FieldIdentifier? ResolveFieldIdentifier<TValue>(
        EditContext? editContext,
        Expression<Func<TValue>>? valueExpression)
    {
        if (editContext is null || valueExpression is null)
        {
            return null;
        }

        return FieldIdentifier.Create(valueExpression);
    }

    public static string? ResolveErrorMessage(
        string? explicitErrorMessage,
        EditContext? editContext,
        FieldIdentifier? fieldIdentifier)
    {
        if (!string.IsNullOrWhiteSpace(explicitErrorMessage))
        {
            return explicitErrorMessage;
        }

        if (editContext is null || fieldIdentifier is not { } field)
        {
            return null;
        }

        return editContext.GetValidationMessages(field).FirstOrDefault();
    }

    public static void NotifyFieldChanged(EditContext? editContext, FieldIdentifier? fieldIdentifier)
    {
        if (editContext is not null && fieldIdentifier is { } field)
        {
            editContext.NotifyFieldChanged(field);
        }
    }

    public static void SyncValidationSubscription(
        ref EditContext? subscribedEditContext,
        EditContext? currentEditContext,
        EventHandler<ValidationStateChangedEventArgs> handler)
    {
        if (ReferenceEquals(subscribedEditContext, currentEditContext))
        {
            return;
        }

        if (subscribedEditContext is not null)
        {
            subscribedEditContext.OnValidationStateChanged -= handler;
        }

        subscribedEditContext = currentEditContext;
        if (subscribedEditContext is not null)
        {
            subscribedEditContext.OnValidationStateChanged += handler;
        }
    }
}
