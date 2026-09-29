namespace Squarebuzz.App.Controls;

/// <summary>
/// A modal card over a dimmed page: the pause, break, time-up and win overlays, the parent gate
/// and the reset confirmation. Only the card's content, its width cap and the dim differ.
/// </summary>
/// <remarks>
/// Visibility, z-order and accessibility exclusion stay with the page that places it: the card
/// is shown with <see cref="VisualElement.IsVisible"/> like any overlay, and the page still
/// excludes whatever lies underneath.
/// </remarks>
public partial class ModalCard : ContentView
{
    public static readonly BindableProperty CardMaxWidthProperty = BindableProperty.Create(
        nameof(CardMaxWidth), typeof(double), typeof(ModalCard), 300d);

    public static readonly BindableProperty DimColorProperty = BindableProperty.Create(
        nameof(DimColor), typeof(Color), typeof(ModalCard), Color.FromArgb("#CC000000"));

    public ModalCard()
    {
        InitializeComponent();
    }

    /// <summary>The widest the card may grow; it fills narrower viewports.</summary>
    public double CardMaxWidth
    {
        get => (double)GetValue(CardMaxWidthProperty);
        set => SetValue(CardMaxWidthProperty, value);
    }

    /// <summary>The backdrop over the page. Darker for results, which end the game.</summary>
    public Color DimColor
    {
        get => (Color)GetValue(DimColorProperty);
        set => SetValue(DimColorProperty, value);
    }
}
