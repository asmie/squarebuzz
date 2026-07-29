using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Squarebuzz.App;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ResizeableActivity = true,
    // Handle these ourselves so rotating a tablet re-lays-out the board instead of
    // recreating the activity and losing the in-progress puzzle.
    ConfigurationChanges = ConfigChanges.ScreenSize
                           | ConfigChanges.Orientation
                           | ConfigChanges.UiMode
                           | ConfigChanges.ScreenLayout
                           | ConfigChanges.SmallestScreenSize
                           | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Phones stay portrait; tablets are free to rotate into the wide board layout.
        RequestedOrientation = IsTablet()
            ? ScreenOrientation.FullUser
            : ScreenOrientation.UserPortrait;
    }

    private bool IsTablet()
    {
        var config = Resources?.Configuration;
        return config is not null && config.SmallestScreenWidthDp >= 600;
    }
}
