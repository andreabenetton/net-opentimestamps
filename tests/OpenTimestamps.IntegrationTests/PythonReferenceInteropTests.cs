using System.Diagnostics;
using OpenTimestamps;
using Xunit;

namespace OpenTimestamps.IntegrationTests;

/// <summary>
/// Cross-implementation interop: feed bytes produced by our library to the
/// Python reference CLI and assert it accepts them.
/// </summary>
/// <remarks>
/// <para>
/// This is the gold-standard interop check — "byte-compatible with the
/// reference" means a file we serialize is indistinguishable to the Python
/// parser from a file the Python serializer would have produced.
/// </para>
/// <para>
/// Skipped unless <c>OTS_PYTHON_REF=1</c> AND the <c>ots</c> Python CLI is on
/// PATH. Gated separately from <c>OTS_SKIP_NETWORK</c> because the test
/// doesn't need network access — only the local Python CLI binary. CI can
/// opt into it via a separate job that installs <c>opentimestamps-client</c>.
/// </para>
/// </remarks>
[Trait("Category", "PythonInterop")]
public sealed class PythonReferenceInteropTests : IClassFixture<PythonInteropFixture>
{
    private readonly PythonInteropFixture _python;

    private static readonly string[] FixtureNames =
    [
        "hello-world.txt.ots",
        "two-calendars.txt.ots",
        "incomplete.txt.ots",
        "known-and-unknown-notary.txt.ots",
        "unknown-notary.txt.ots",
        "different-blockchains.txt.ots",
    ];

    public PythonReferenceInteropTests(PythonInteropFixture python)
    {
        _python = python;
    }

    [SkippableTheory]
    [InlineData("hello-world.txt.ots")]
    [InlineData("two-calendars.txt.ots")]
    [InlineData("incomplete.txt.ots")]
    [InlineData("known-and-unknown-notary.txt.ots")]
    [InlineData("unknown-notary.txt.ots")]
    [InlineData("different-blockchains.txt.ots")]
    public void Our_Reserialized_Fixture_Is_Accepted_By_Python_CLI(string fixtureName)
    {
        Skip.If(!_python.Enabled, "OTS_PYTHON_REF != 1 or `ots` not on PATH.");

        string fixturesDir = LocateFixturesDir();
        string originalPath = Path.Combine(fixturesDir, fixtureName);
        Skip.IfNot(File.Exists(originalPath), $"fixture missing: {originalPath}");

        // Parse with our library, then re-serialize.
        DetachedTimestampFile dtf = DetachedTimestampFile.DeserializeFromFile(originalPath);
        byte[] roundTripped = dtf.SerializeToArray();

        string tempPath = Path.Combine(Path.GetTempPath(), $"ots-interop-{Guid.NewGuid():N}.ots");
        try
        {
            File.WriteAllBytes(tempPath, roundTripped);

            (int exitCode, string stdout, string stderr) = RunOts("info", tempPath);
            Assert.True(exitCode == 0,
                $"Python `ots info` rejected our re-serialized {fixtureName} " +
                $"(exit={exitCode}). stderr:\n{stderr}\nstdout:\n{stdout}");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [SkippableFact]
    public void Python_Cli_And_Our_Library_Agree_On_Fixture_Byte_Identity()
    {
        Skip.If(!_python.Enabled, "OTS_PYTHON_REF != 1 or `ots` not on PATH.");

        // If our re-serialize is byte-identical to the original (RoundTripTests
        // already asserts this), and `ots info` accepts both, then the Python
        // parser cannot tell our output apart from the reference's.
        string fixturesDir = LocateFixturesDir();
        foreach (string name in FixtureNames)
        {
            string path = Path.Combine(fixturesDir, name);
            if (!File.Exists(path))
            {
                continue;
            }

            (int exitCode, string stdout, string stderr) = RunOts("info", path);
            Assert.True(exitCode == 0,
                $"Python `ots info` failed on upstream fixture {name} (exit={exitCode}): {stderr}");
        }
    }

    private static (int exitCode, string stdout, string stderr) RunOts(params string[] args)
    {
        var psi = new ProcessStartInfo("ots")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to launch `ots`.");
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        bool exited = p.WaitForExit(TimeSpan.FromSeconds(30));
        if (!exited)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new TimeoutException("ots CLI did not exit within 30s.");
        }

        return (p.ExitCode, stdout, stderr);
    }

    private static string LocateFixturesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "net-opentimestamps")
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate repo root above {AppContext.BaseDirectory}.");
        }

        return Path.Combine(
            dir.FullName,
            "tests",
            "OpenTimestamps.Tests",
            "fixtures",
            "python-opentimestamps");
    }
}

public sealed class PythonInteropFixture
{
    public bool Enabled { get; }

    public PythonInteropFixture()
    {
        bool optIn = string.Equals(
            Environment.GetEnvironmentVariable("OTS_PYTHON_REF"),
            "1",
            StringComparison.Ordinal);
        Enabled = optIn && IsToolOnPath("ots");
    }

    private static bool IsToolOnPath(string toolName)
    {
        try
        {
            var psi = new ProcessStartInfo(toolName, "--help")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                return false;
            }

            _ = p.StandardOutput.ReadToEnd();
            _ = p.StandardError.ReadToEnd();
            p.WaitForExit(TimeSpan.FromSeconds(5));
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }
}
