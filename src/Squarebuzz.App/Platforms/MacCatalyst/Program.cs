using ObjCRuntime;
using UIKit;

namespace Squarebuzz.App;

public static class Program
{
    // This is the main entry point of the application.
    private static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
