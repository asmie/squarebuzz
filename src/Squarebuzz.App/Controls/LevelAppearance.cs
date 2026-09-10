using Squarebuzz.Core.Progression;

namespace Squarebuzz.App.Controls;

/// <summary>
/// Keeps a level node, caption or trail bound to its state's theme resource keys. Changing a
/// recycled card's state replaces those keys; palette changes update the same controls directly.
/// </summary>
public static class LevelAppearance
{
    // Null distinguishes an unbound control from every real state, so even the first Locked
    // binding applies its resources instead of being skipped as an unchanged default value.
    public static readonly BindableProperty StateProperty = BindableProperty.CreateAttached(
        "State",
        typeof(LevelNodeState?),
        typeof(LevelAppearance),
        defaultValue: null,
        propertyChanged: OnStateChanged);

    public static LevelNodeState? GetState(BindableObject view) =>
        (LevelNodeState?)view.GetValue(StateProperty);

    public static void SetState(BindableObject view, LevelNodeState? value) =>
        view.SetValue(StateProperty, value);

    private static void OnStateChanged(BindableObject view, object oldValue, object newValue)
    {
        var (fill, stroke, ink, trail) = newValue switch
        {
            LevelNodeState.Done => ("Accent", "AccentDeep", "OnAccent", "Accent"),
            LevelNodeState.Current => ("Primary", "PrimaryDeep", "OnPrimary", "Line"),
            _ => ("Surface", "Line", "Ink2", "Line"),
        };

        switch (view)
        {
            case Border node:
                node.SetDynamicResource(VisualElement.BackgroundColorProperty, fill);
                node.SetDynamicResource(Border.StrokeProperty, stroke);
                break;
            case Label caption:
                caption.SetDynamicResource(Label.TextColorProperty, ink);
                break;
            case BoxView dot:
                dot.SetDynamicResource(BoxView.ColorProperty, trail);
                break;
        }
    }
}
