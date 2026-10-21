namespace Squarebuzz.Presentation.Services;

/// <summary>Offers a public app listing to the system share sheet after the parent gate.</summary>
public interface IAppShare
{
    /// <summary>False when sharing is unavailable. Completion does not confirm delivery.</summary>
    Task<bool> RequestAsync(Uri address, string title);
}
