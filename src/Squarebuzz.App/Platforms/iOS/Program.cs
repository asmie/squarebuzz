using ObjCRuntime;
using UIKit;

namespace Squarebuzz.App;

public static class Program
{
    // This is the main entry point of the application.
    private static void Main(string[] args)
    {
        // If you want to use a different Application Delegate class from "AppDelegate"
        // you can specify it here.
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
