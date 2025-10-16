using System;
using System.Diagnostics;

var script = @"C:\Users\kevin\trading-bot-c-\trading-bot-c--1\src\UnifiedOrchestrator\python\sdk_bridge.py";
var json = "{\"symbol\":\"ES\",\"days\":1,\"timeframe_minutes\":5}";

Console.WriteLine($"Script: {script}");
Console.WriteLine($"JSON: {json}");

var startInfo = new ProcessStartInfo
{
    FileName = "python",
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true
};
startInfo.ArgumentList.Add(script);
startInfo.ArgumentList.Add(json);

using var process = Process.Start(startInfo);
var output = await process.StandardOutput.ReadToEndAsync();
var error = await process.StandardError.ReadToEndAsync();
await process.WaitForExitAsync();

Console.WriteLine($"Exit code: {process.ExitCode}");
Console.WriteLine($"Output length: {output.Length}");
if (output.Length > 200)
    Console.WriteLine($"Output preview: {output.Substring(0, 200)}...");
else
    Console.WriteLine($"Output: {output}");

if (!string.IsNullOrEmpty(error))
    Console.WriteLine($"Error: {error}");
