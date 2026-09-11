using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Services;

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
    private IThemeService? _theme;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Phones stay portrait; tablets are free to rotate into the wide board layout.
        RequestedOrientation = IsTablet()
            ? ScreenOrientation.FullUser
            : ScreenOrientation.UserPortrait;

        _theme = IPlatformApplication.Current?.Services.GetService<IThemeService>();
        if (_theme is not null) _theme.Changed += OnThemeChanged;
        UpdateSystemBarAppearance();
    }

    protected override void OnResume()
    {
        base.OnResume();
        UpdateSystemBarAppearance();
    }

    protected override void OnDestroy()
    {
        if (_theme is not null) _theme.Changed -= OnThemeChanged;
        base.OnDestroy();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateSystemBarAppearance();

    private void UpdateSystemBarAppearance()
    {
        if (Window?.DecorView is not { } decorView || _theme is null) return;

        // Transparent system bars sit over the app palette, which can differ from the OS theme.
        // Update their icon contrast on a live theme change, without recreating the activity.
        var controller = WindowCompat.GetInsetsController(Window, decorView);
        if (controller is null) return;
        var isLight = _theme.Theme != GameTheme.Dark;
        controller.AppearanceLightStatusBars = isLight;
        controller.AppearanceLightNavigationBars = isLight;
    }

    private bool IsTablet()
    {
        var config = Resources?.Configuration;
        return config is not null && config.SmallestScreenWidthDp >= 600;
    }
}
