using Squarebuzz.App.ViewModels;

namespace Squarebuzz.App.Views;

public partial class OnboardingPage : ContentPage
{
    private readonly OnboardingViewModel _viewModel;

    public OnboardingPage(OnboardingViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;

        // Rotation and desktop window resizes reach this through MiddleRow. The caption is watched
        // as well: its height is known only after it has been measured, and it re-wraps whenever the
        // step text changes.
        MiddleRow.SizeChanged += (_, _) => ApplyLayout();
        Caption.SizeChanged += (_, _) => ApplyLayout();
    }

    /// <summary>The card's design size, used whenever there is room for it.</summary>
    private const double PreferredCardSize = 300;

    /// <summary>
    /// Below this the demo art says nothing, so the card stops shrinking and the page clips instead.
    /// </summary>
    private const double MinimumCardSize = 90;

    /// <summary>
    /// Shrinks the demo card to whatever vertical room is left, so the Next button stays on screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Onboarding is the one screen a child cannot navigate past - every other screen is reachable
    /// from the menu, but this one offers only Next and Skip. At its design size the card plus its
    /// caption overflows a short viewport: the dots ride up over the caption, and further down the
    /// Next button leaves the screen entirely, which strands a child who has not yet learned the
    /// rules. A small tablet held sideways is only about 600 units tall, so this is an ordinary
    /// case rather than an exotic one. Phones cannot reach it - MainActivity and Info.plist both
    /// pin handsets to portrait - but tablets rotate freely.
    /// </para>
    /// <para>
    /// The room available is read from the row the content sits in, not totalled up from the page
    /// padding, the two pinned rows and the gaps between them. That arithmetic was the first
    /// attempt and it under-counted, which does not fail loudly - it just quietly overlaps.
    /// </para>
    /// <para>
    /// This settles in one pass rather than oscillating: the row is a star row, so its height comes
    /// from what is left over and not from the card inside it, and the caption's height depends on
    /// its width, which the frame cap fixes.
    /// </para>
    /// </remarks>
    private void ApplyLayout()
    {
        // Both are zero until the first measure pass; the SizeChanged that follows it does the work.
        if (MiddleRow.Height <= 0 || Caption.Height <= 0)
        {
            return;
        }

        var room = MiddleRow.Height - Caption.Height - CardAndCaption.Spacing;
        var size = Math.Clamp(room, MinimumCardSize, PreferredCardSize);

        // Assigning unconditionally would invalidate layout on every pass and never settle.
        if (Math.Abs(DemoCard.HeightRequest - size) > 0.5)
        {
            DemoCard.HeightRequest = DemoCard.WidthRequest = size;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
