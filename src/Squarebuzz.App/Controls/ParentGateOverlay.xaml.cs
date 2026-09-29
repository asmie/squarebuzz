namespace Squarebuzz.App.Controls;

/// <summary>
/// The parent gate's sum, as one overlay for every page that asks it. Shows itself while the
/// bound <see cref="Squarebuzz.Presentation.ViewModels.ParentGate"/> is open.
/// </summary>
public partial class ParentGateOverlay : ContentView
{
    public ParentGateOverlay()
    {
        InitializeComponent();
    }
}
