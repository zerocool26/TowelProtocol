using System.Diagnostics;
using System.IO;

Console.WriteLine("PrivacyHardening Integration Runner\n");

// Determine path to built elevated helper
var solutionRoot = Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.Parent!.Parent!.FullName; // approximate back to repo root
var elevatedExeRel = Path.Combine(solutionRoot, "src", "PrivacyHardeningElevated", "bin", "Release", "net8.0-windows10.0.22621.0", "PrivacyHardeningElevated.exe");

if (!File.Exists(elevatedExeRel))
{
    Console.WriteLine($"Cannot find elevated helper at: {elevatedExeRel}");
    Console.WriteLine("Build the solution in Release configuration first (dotnet build PrivacyHardeningFramework.sln -c Release)");
    return 2;
}

Console.WriteLine($"Found elevated helper: {elevatedExeRel}");
Console.WriteLine("This runner will launch the elevated helper using UAC (you will see a prompt).\nEnsure the service is running (PrivacyHardeningService) before continuing.");

var psi = new ProcessStartInfo(elevatedExeRel)
{
    UseShellExecute = true,
    Verb = "runas",
    Arguments = "--apply-all",
};

try
{
    using var proc = Process.Start(psi)!;
    Console.WriteLine("Launched elevated helper, waiting for exit...");
    proc.WaitForExit();
    Console.WriteLine($"Elevated helper exited with code: {proc.ExitCode}");
    return proc.ExitCode;
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to launch elevated helper: {ex.Message}");
    return 1;
}