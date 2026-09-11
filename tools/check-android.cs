#:property PublishAot=false

// Native Release smoke test and evidence capture. See tests/Native/README.md.
// Requires an already running, dedicated test device; never clears application data.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

return await AndroidCheck.RunAsync(args);

internal static class AndroidCheck
{
    private const string Package = "fun.asmie.squarebuzz";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 0 || arguments.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine("dotnet run tools/check-android.cs -- --serial SERIAL --adb PATH [--apk PATH | --capture NAME] [--expect TEXT]...");
            return arguments.Length == 0 ? 1 : 0;
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var expected = new List<string>();
        try
        {
            for (var i = 0; i < arguments.Length; i += 2)
            {
                var key = arguments[i];
                if (i + 1 == arguments.Length || key is not ("--serial" or "--adb" or "--apk" or "--capture" or "--expect"))
                    throw new ArgumentException($"Unknown option or missing value: {key}");
                if (key == "--expect") expected.Add(arguments[i + 1]);
                else options.Add(key, arguments[i + 1]);
            }
            if (!options.ContainsKey("--serial") || !options.ContainsKey("--adb"))
                throw new ArgumentException("Specify --serial and --adb explicitly; never select an arbitrary connected device.");
            if (options.ContainsKey("--apk") == options.ContainsKey("--capture"))
                throw new ArgumentException("Specify exactly one of --apk (install/smoke) or --capture (current screen).");
            if (options.TryGetValue("--capture", out var name) && !Regex.IsMatch(name, "^[a-zA-Z0-9-]+$"))
                throw new ArgumentException("Capture names may contain only letters, digits and hyphens.");
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }

        var label = options.GetValueOrDefault("--capture", "smoke");
        var directory = Path.GetFullPath(Path.Combine("artifacts", "native",
            $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}-{label}-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(directory);
        var report = new Dictionary<string, object?>
        {
            ["startedUtc"] = DateTimeOffset.UtcNow,
            ["serial"] = options["--serial"],
            ["package"] = Package,
            ["mode"] = label,
            ["status"] = "failed",
        };
        var log = new List<object>();

        async Task<string> Adb(params string[] command)
        {
            var start = new ProcessStartInfo(options["--adb"])
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-s");
            start.ArgumentList.Add(options["--serial"]);
            foreach (var argument in command) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Could not start ADB.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException($"ADB timed out: {string.Join(' ', command)}");
            }
            var output = await stdout;
            var error = await stderr;
            log.Add(new { command, process.ExitCode, output, error });
            if (process.ExitCode != 0 || Regex.IsMatch(output + "\n" + error, @"(?im)^\s*(error:|failure \[|error type \d)"))
                throw new IOException($"ADB failed: {string.Join(' ', command)}\n{output}\n{error}");
            return output.Trim();
        }

        async Task AssertForeground()
        {
            var activity = await Adb("shell", "dumpsys", "activity", "activities");
            var resumed = Regex.Matches(activity, @"(?m)^.*(?:topResumedActivity|mResumedActivity).*$");
            if (!resumed.Any(match => match.Value.Contains(Package, StringComparison.Ordinal)))
                throw new InvalidOperationException("squarebuzz is not the resumed activity.");
            var pid = await Adb("shell", "pidof", Package);
            if (string.IsNullOrWhiteSpace(pid)) throw new InvalidOperationException("squarebuzz has no live process.");
            report["lastPid"] = pid;
        }

        async Task Screenshot(string name)
        {
            var remote = $"/sdcard/squarebuzz-{Guid.NewGuid():N}.png";
            try
            {
                await Adb("shell", "screencap", "-p", remote);
                await Adb("pull", remote, Path.Combine(directory, name + ".png"));
            }
            finally
            {
                // Exact temporary path generated above; never clear the shared storage directory.
                await Adb("shell", "rm", "-f", remote);
            }
        }

        try
        {
            if (await Adb("shell", "getprop", "sys.boot_completed") != "1")
                throw new InvalidOperationException("Wait for the test device to finish booting.");
            report["fingerprint"] = await Adb("shell", "getprop", "ro.build.fingerprint");
            report["api"] = await Adb("shell", "getprop", "ro.build.version.sdk");
            report["screen"] = await Adb("shell", "wm", "size");
            report["density"] = await Adb("shell", "wm", "density");
            report["fontScale"] = await Adb("shell", "settings", "get", "system", "font_scale");
            report["animatorScale"] = await Adb("shell", "settings", "get", "global", "animator_duration_scale");
            report["accessibilityServices"] = await Adb("shell", "settings", "get", "secure", "enabled_accessibility_services");

            if (options.TryGetValue("--apk", out var apk))
            {
                apk = Path.GetFullPath(apk);
                await using (var file = File.OpenRead(apk))
                    report["apkSha256"] = Convert.ToHexString(await SHA256.HashDataAsync(file));
                report["apk"] = apk;
                await Adb("install", "-r", apk);
                // Prove that the package we exercise is this APK, not an older squarebuzz
                // installation left behind when an unrelated APK was supplied by mistake.
                var installedPaths = (await Adb("shell", "pm", "path", Package))
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (installedPaths.Length != 1 || !installedPaths[0].StartsWith("package:", StringComparison.Ordinal))
                    throw new InvalidOperationException("Expected one installed squarebuzz APK; split APKs are not supported by this smoke test.");
                var installedApk = Path.Combine(directory, "installed.apk");
                try
                {
                    await Adb("pull", installedPaths[0]["package:".Length..], installedApk);
                    await using var installedFile = File.OpenRead(installedApk);
                    var installedHash = Convert.ToHexString(await SHA256.HashDataAsync(installedFile));
                    report["installedApkSha256"] = installedHash;
                    if (installedHash != (string?)report["apkSha256"])
                        throw new InvalidOperationException("The installed squarebuzz APK does not match the supplied APK.");
                }
                finally
                {
                    File.Delete(installedApk);
                }
                string? component = null;
                for (var attempt = 0; attempt < 10 && component is null; attempt++)
                {
                    var resolution = await Adb("shell", "cmd", "package", "resolve-activity", "--brief",
                        "-a", "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER", Package);
                    component = resolution.Split('\n').Select(line => line.Trim())
                        .FirstOrDefault(line => line.StartsWith(Package + "/", StringComparison.Ordinal));
                    if (component is null) await Task.Delay(500);
                }
                if (component is null) throw new InvalidOperationException("The installed APK has no resolved launcher activity.");
                report["activity"] = component;
                await Adb("shell", "am", "force-stop", Package);
                await Adb("shell", "am", "start", "-W", "-n", component);
                await Task.Delay(2000);
                await AssertForeground();
                await Screenshot("cold-start");
                await Adb("shell", "input", "keyevent", "KEYCODE_HOME");
                await Task.Delay(2000);
                await Adb("shell", "am", "start", "-W", "-n", component);
                await AssertForeground();
                await Screenshot("resumed");
                report["scope"] = "Install, cold startup and Home/resume. Gameplay, persistence contents, audio and screen-reader usability require the acceptance cases.";
            }
            else
            {
                await AssertForeground();
                await Screenshot(label);
            }

            if (expected.Count > 0)
            {
                var remote = $"/sdcard/squarebuzz-{Guid.NewGuid():N}.xml";
                try
                {
                    await Adb("shell", "uiautomator", "dump", remote);
                    var path = Path.Combine(directory, "hierarchy.xml");
                    await Adb("pull", remote, path);
                    var document = XDocument.Load(path);
                    var descriptions = document.Descendants("node")
                        .Where(node => (string?)node.Attribute("package") == Package)
                        .SelectMany(node => new[] { (string?)node.Attribute("text"), (string?)node.Attribute("content-desc") })
                        .ToHashSet(StringComparer.Ordinal);
                    foreach (var text in expected)
                        if (!descriptions.Contains(text)) throw new InvalidOperationException($"Missing exact UI text/description: {text}");
                }
                finally
                {
                    await Adb("shell", "rm", "-f", remote);
                }
            }
            var pidArgument = ((string)report["lastPid"]!).Split(' ')[0];
            await File.WriteAllTextAsync(Path.Combine(directory, "app-logcat.txt"),
                await Adb("logcat", "-d", "--pid", pidArgument, "-v", "threadtime"));
            report["status"] = "passed";
            return 0;
        }
        catch (Exception exception)
        {
            report["error"] = exception.Message;
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally
        {
            report["finishedUtc"] = DateTimeOffset.UtcNow;
            report["commands"] = log;
            await File.WriteAllTextAsync(Path.Combine(directory, "result.json"),
                JsonSerializer.Serialize(report, JsonOptions));
            Console.WriteLine($"Native check {report["status"]}: {directory}");
        }
    }
}
