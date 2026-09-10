using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Squarebuzz.App.Services;

/// <summary>Registers pushed pages whose disposable view models are owned by the page.</summary>
internal static class PageRegistration
{
    public static IServiceCollection AddPageWithViewModel<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPage,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TViewModel>(
        this IServiceCollection services)
        where TPage : class
        where TViewModel : class, IDisposable
    {
        return services.AddTransient<TPage>(provider =>
        {
            // Resolving a registered disposable transient would retain it in the root container
            // even after Dispose. Only its app-lifetime dependencies come from the container.
            var viewModel = ActivatorUtilities.CreateInstance<TViewModel>(provider);
            try
            {
                return ActivatorUtilities.CreateInstance<TPage>(provider, viewModel);
            }
            catch
            {
                // A failed page constructor never reaches the pop hook that normally owns this.
                viewModel.Dispose();
                throw;
            }
        });
    }
}
