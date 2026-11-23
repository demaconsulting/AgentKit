using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for <see cref="CopilotCli"/>: discovery of the installed Copilot CLI on every
///     platform, the editor launchers it passes over, the version probe and its failures, the version
///     policy, and the guidance its refusals give.
/// </summary>
/// <remarks>
///     Discovery and the probe run against <see cref="FakeCopilotCliMachine"/>, so every platform's
///     search order is verified on every platform and no CLI is needed. Only the process runner's
///     own tests start real processes — the platform's shell or a sleep — never a Copilot CLI.
/// </remarks>
public class CopilotCliTests
{
    /// <summary>
    ///     A large single-file executable's length, which the launcher check never reads.
    /// </summary>
    private const long RealCliLength = 190L * 1024 * 1024;

    /// <summary>
    ///     A fake data directory for the probe.
    /// </summary>
    private static readonly string DataDirectory = Path.Combine("/fake", "data");

    /// <summary>
    ///     Proves the first <c>PATH</c> entry holding <c>copilot.exe</c> is chosen on Windows.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_OnPath_ReturnsFirstPathMatch()
    {
        // Arrange: copilot.exe in the second and third PATH entries
        var machine = new FakeCopilotCliMachine();
        var second = machine.AddFile(Path.Combine("/fake/two", "copilot.exe"), length: RealCliLength);
        machine.AddFile(Path.Combine("/fake/three", "copilot.exe"), length: RealCliLength);
        var context = machine.Context(CopilotCliPlatform.Windows, "/fake/one;/fake/two;/fake/three");

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Equal(second, result.Path);
        Assert.Equal([Path.Combine("/fake/one", "copilot.exe"), second], result.SearchedLocations);
        Assert.Empty(result.SkippedCandidates);
    }

    /// <summary>
    ///     Proves a CLI on <c>PATH</c> is preferred to one in a well-known location.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_OnPathAndWellKnown_PrefersPath()
    {
        // Arrange: a WinGet link and a CLI on PATH
        var machine = new FakeCopilotCliMachine();
        var local = Path.Combine("/fake", "local");
        machine.AddFile(Path.Combine(local, "Microsoft", "WinGet", "Links", "copilot.exe"), length: RealCliLength);
        var onPath = machine.AddFile(Path.Combine("/fake/tools", "copilot.exe"), length: RealCliLength);
        var context = machine.Context(CopilotCliPlatform.Windows, "/fake/tools", localAppData: local);

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Equal(onPath, result.Path);
    }

    /// <summary>
    ///     Proves the <c>.cmd</c> and <c>.ps1</c> shims npm puts on <c>PATH</c> on Windows are not
    ///     candidates, because they cannot be started without a shell.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_WindowsCmdAndPs1OnPath_AreNotCandidates()
    {
        // Arrange: only npm's script shims on PATH
        var machine = new FakeCopilotCliMachine();
        machine.AddFile(Path.Combine("/fake/npm", "copilot.cmd"));
        machine.AddFile(Path.Combine("/fake/npm", "copilot.ps1"));
        machine.AddFile(Path.Combine("/fake/npm", "copilot"));
        var context = machine.Context(CopilotCliPlatform.Windows, "/fake/npm");

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Null(result.Path);
        Assert.Equal([Path.Combine("/fake/npm", "copilot.exe")], result.SearchedLocations);
    }

    /// <summary>
    ///     Proves the Visual Studio Code launcher in its <c>copilot-shim</c> folder is passed over and
    ///     reported, and the real CLI after it on <c>PATH</c> is chosen.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_VsCodeShimFolder_IsSkippedAndReported()
    {
        // Arrange: the launcher first on PATH, the real CLI second
        var machine = new FakeCopilotCliMachine();
        var shimFolder = Path.Combine("/fake", "Microsoft VS Code", "bin", "copilot-shim");
        var shim = machine.AddFile(Path.Combine(shimFolder, "copilot.exe"), length: 800_000);
        var real = machine.AddFile(Path.Combine("/fake/winget", "copilot.exe"), length: RealCliLength);
        var context = machine.Context(CopilotCliPlatform.Windows, $"{shimFolder};/fake/winget");

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Equal(real, result.Path);
        var skipped = Assert.Single(result.SkippedCandidates);
        Assert.StartsWith(shim, skipped, StringComparison.Ordinal);
        Assert.Contains("Visual Studio Code", skipped, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a small file carrying a launcher marker — the launcher's executable or its POSIX
    ///     script — is passed over wherever it lives.
    /// </summary>
    /// <param name="marker">The marker the launcher carries.</param>
    [Theory]
    [InlineData("VSCODE_COPILOT_RUST_SHIM_V1")]
    [InlineData("vscode-copilot-launcher")]
    public void CopilotCli_FindInstalled_LauncherMarkerInSmallFile_IsSkippedAndReported(string marker)
    {
        // Arrange: a launcher with the marker past its first bytes, then a real CLI
        var machine = new FakeCopilotCliMachine();
        var content = new byte[600_000];
        System.Text.Encoding.ASCII.GetBytes(marker).CopyTo(content, 563_491);
        var launcher = machine.AddFile(Path.Combine("/fake/a", "copilot"), content);
        var real = machine.AddFile(Path.Combine("/fake/b", "copilot"), length: RealCliLength);
        var context = machine.Context(CopilotCliPlatform.Linux, "/fake/a:/fake/b");

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Equal(real, result.Path);
        Assert.StartsWith(launcher, Assert.Single(result.SkippedCandidates), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a CLI installed by WinGet is found in its package folder when it is not on
    ///     <c>PATH</c>, the package folders taken in name order.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_WinGetPackage_IsFound()
    {
        // Arrange: two WinGet Copilot package folders, the CLI in the later one, nothing on PATH
        var machine = new FakeCopilotCliMachine();
        var local = Path.Combine("/fake", "local");
        var packages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
        var later = machine.AddDirectory(packages, "GitHub.Copilot_Z");
        var earlier = machine.AddDirectory(packages, "GitHub.Copilot_A");
        machine.AddDirectory(packages, "Other.Package_A");
        var cli = machine.AddFile(Path.Combine(later, "copilot.exe"), length: RealCliLength);
        var context = machine.Context(CopilotCliPlatform.Windows, string.Empty, localAppData: local);

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Equal(cli, result.Path);
        Assert.Equal(
            [
                Path.Combine(local, "Microsoft", "WinGet", "Links", "copilot.exe"),
                Path.Combine(earlier, "copilot.exe"),
                cli,
            ],
            result.SearchedLocations);
    }

    /// <summary>
    ///     Proves a CLI in the WinGet links folder is found when it is not on <c>PATH</c>.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_WinGetLink_IsFound()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        var local = Path.Combine("/fake", "local");
        var link = machine.AddFile(Path.Combine(local, "Microsoft", "WinGet", "Links", "copilot.exe"), length: RealCliLength);
        var context = machine.Context(CopilotCliPlatform.Windows, null, localAppData: local);

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Equal(link, result.Path);
    }

    /// <summary>
    ///     Proves npm's global Copilot package is found on Windows as its <c>npm-loader.js</c> entry point.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_NpmLoaderOnWindows_IsFound()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        var roaming = Path.Combine("/fake", "roaming");
        var loader = machine.AddFile(Path.Combine(roaming, "npm", "node_modules", "@github", "copilot", "npm-loader.js"));
        var context = machine.Context(CopilotCliPlatform.Windows, null, appData: roaming);

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Equal(loader, result.Path);
    }

    /// <summary>
    ///     Proves a Homebrew installation is found on macOS when it is not on <c>PATH</c>.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_HomebrewOnMacOS_IsFound()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        var homebrew = machine.AddFile(Path.Combine("/", "opt", "homebrew", "bin", "copilot"), length: RealCliLength);
        var context = machine.Context(CopilotCliPlatform.MacOS, "/fake/bin", userProfile: "/fake/home");

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Equal(homebrew, result.Path);
        Assert.Equal([Path.Combine("/fake/bin", "copilot"), homebrew], result.SearchedLocations);
    }

    /// <summary>
    ///     Proves the install script's <c>~/.local/bin</c> installation is found on Linux, ahead of the
    ///     system-wide locations.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_LocalBinOnLinux_IsFound()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        var home = Path.Combine("/fake", "home");
        var localBin = machine.AddFile(Path.Combine(home, ".local", "bin", "copilot"), length: RealCliLength);
        machine.AddFile(Path.Combine("/", "usr", "local", "bin", "copilot"), length: RealCliLength);
        var context = machine.Context(CopilotCliPlatform.Linux, null, userProfile: home);

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Equal(localBin, result.Path);
    }

    /// <summary>
    ///     Proves a file without an execute bit is not a candidate on Unix.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_UnixWithoutExecuteBit_IsNotACandidate()
    {
        // Arrange: a non-executable copilot on PATH, an executable one after it
        var machine = new FakeCopilotCliMachine();
        machine.AddFile(Path.Combine("/fake/a", "copilot"), executable: false, length: RealCliLength);
        var executable = machine.AddFile(Path.Combine("/fake/b", "copilot"), length: RealCliLength);
        var context = machine.Context(CopilotCliPlatform.Linux, "/fake/a:/fake/b");

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Equal(executable, result.Path);
        Assert.Empty(result.SkippedCandidates);
    }

    /// <summary>
    ///     Proves relative and empty <c>PATH</c> entries are never searched, so the current directory
    ///     cannot supply a CLI.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_RelativePathEntries_AreNotSearched()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        machine.AddFile(Path.Combine(".", "copilot"), length: RealCliLength);
        machine.AddFile(Path.Combine("bin", "copilot"), length: RealCliLength);
        var context = machine.Context(CopilotCliPlatform.Linux, ".::bin");

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Null(result.Path);
        Assert.DoesNotContain(result.SearchedLocations, location => !Path.IsPathRooted(location));
    }

    /// <summary>
    ///     Proves drive-relative <c>PATH</c> entries on Windows — <c>C:bin</c>, <c>c:tools</c>,
    ///     <c>C:</c> — are never searched, because each resolves against the drive's current directory,
    ///     while drive roots written with either separator are.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_DriveRelativePathEntries_AreNotSearched()
    {
        // Arrange: copilot.exe under two drive-relative entries and under a fully qualified one
        var machine = new FakeCopilotCliMachine();
        machine.AddFile(Path.Combine("C:bin", "copilot.exe"), length: RealCliLength);
        machine.AddFile(Path.Combine("c:tools", "copilot.exe"), length: RealCliLength);
        var real = machine.AddFile(Path.Combine(@"C:\real", "copilot.exe"), length: RealCliLength);
        var context = machine.Context(CopilotCliPlatform.Windows, @"C:bin;c:tools;C:;C:/first;C:\real");

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Equal(real, result.Path);
        Assert.Equal([Path.Combine("C:/first", "copilot.exe"), real], result.SearchedLocations);
    }

    /// <summary>
    ///     Proves an Intel Homebrew or install-script installation in <c>/usr/local/bin</c> is found on
    ///     macOS when Apple silicon Homebrew holds none.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_UsrLocalBinOnMacOS_IsFound()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        var usrLocal = machine.AddFile(Path.Combine("/", "usr", "local", "bin", "copilot"), length: RealCliLength);
        var context = machine.Context(CopilotCliPlatform.MacOS, null, userProfile: "/fake/home");

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Equal(usrLocal, result.Path);
        Assert.Equal([Path.Combine("/", "opt", "homebrew", "bin", "copilot"), usrLocal], result.SearchedLocations);
    }

    /// <summary>
    ///     Proves a Linuxbrew installation is found on Linux, after <c>~/.local/bin</c> and
    ///     <c>/usr/local/bin</c>.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_LinuxbrewOnLinux_IsFound()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        var home = Path.Combine("/fake", "home");
        var linuxbrew = machine.AddFile(
            Path.Combine("/", "home", "linuxbrew", ".linuxbrew", "bin", "copilot"),
            length: RealCliLength);
        var context = machine.Context(CopilotCliPlatform.Linux, null, userProfile: home);

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Equal(linuxbrew, result.Path);
        Assert.Equal(
            [
                Path.Combine(home, ".local", "bin", "copilot"),
                Path.Combine("/", "usr", "local", "bin", "copilot"),
                linuxbrew,
            ],
            result.SearchedLocations);
    }

    /// <summary>
    ///     Proves npm's global Copilot package is found on Linux and macOS as its <c>npm-loader.js</c>,
    ///     which is run with <c>node</c> and so needs no execute bit.
    /// </summary>
    /// <param name="macOS">Whether the platform is macOS rather than Linux.</param>
    /// <param name="prefix">The npm prefix's folders under the root.</param>
    [Theory]
    [InlineData(false, new[] { "usr", "local" })]
    [InlineData(true, new[] { "opt", "homebrew" })]
    public void CopilotCli_FindInstalled_NpmLoaderOnUnix_IsFound(bool macOS, string[] prefix)
    {
        // Arrange: the loader without an execute bit, nothing else installed
        var machine = new FakeCopilotCliMachine();
        var loader = machine.AddFile(
            Path.Combine(["/", .. prefix, "lib", "node_modules", "@github", "copilot", "npm-loader.js"]),
            executable: false);
        var platform = macOS ? CopilotCliPlatform.MacOS : CopilotCliPlatform.Linux;
        var context = machine.Context(platform, null, userProfile: "/fake/home");

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Equal(loader, result.Path);
    }

    /// <summary>
    ///     Proves a candidate reached twice — a <c>PATH</c> entry that is also a well-known location, or
    ///     two <c>PATH</c> entries differing only in case on Windows — is searched once.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_SameCandidateTwice_IsSearchedOnce()
    {
        // Arrange: /usr/local/bin on PATH on Linux, and one folder twice in different case on Windows
        var machine = new FakeCopilotCliMachine();
        var usrLocalBin = Path.Combine("/", "usr", "local", "bin");
        var linux = machine.Context(CopilotCliPlatform.Linux, usrLocalBin, userProfile: "/fake/home");
        var windows = machine.Context(CopilotCliPlatform.Windows, "/fake/Tools;/fake/tools");

        // Act
        var linuxResult = CopilotCli.FindInstalled(linux);
        var windowsResult = CopilotCli.FindInstalled(windows);

        // Assert
        var usrLocalCli = Path.Combine(usrLocalBin, "copilot");
        Assert.Single(linuxResult.SearchedLocations, location => location == usrLocalCli);
        Assert.Equal([Path.Combine("/fake/Tools", "copilot.exe")], windowsResult.SearchedLocations);
    }

    /// <summary>
    ///     Proves the launcher check reads a file up to 16 MiB for a marker and never reads a larger
    ///     one, which is then chosen.
    /// </summary>
    /// <param name="length">The file's reported length.</param>
    /// <param name="scanned">Whether the file is read, finding the marker and skipping the file.</param>
    [Theory]
    [InlineData(16L * 1024 * 1024, true)]
    [InlineData((16L * 1024 * 1024) + 1, false)]
    public void CopilotCli_FindInstalled_LauncherMarkerScanLimit_LargerFileIsNotScanned(long length, bool scanned)
    {
        // Arrange: a file carrying a launcher marker, with the given length
        var machine = new FakeCopilotCliMachine();
        var content = System.Text.Encoding.ASCII.GetBytes("prefix vscode-copilot-launcher suffix");
        var candidate = machine.AddFile(Path.Combine("/fake/a", "copilot"), content, length: length);
        var context = machine.Context(CopilotCliPlatform.Linux, "/fake/a");

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        if (scanned)
        {
            Assert.Null(result.Path);
            Assert.StartsWith(candidate, Assert.Single(result.SkippedCandidates), StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(candidate, result.Path);
            Assert.Empty(result.SkippedCandidates);
        }
    }

    /// <summary>
    ///     Proves that with nothing installed no CLI is returned and every location searched is
    ///     reported, in order.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_NothingInstalled_ReturnsNullWithSearchedLocations()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        var home = Path.Combine("/fake", "home");
        var context = machine.Context(CopilotCliPlatform.Linux, "/fake/bin", userProfile: home);

        // Act
        var result = CopilotCli.FindInstalled(context);

        // Assert
        Assert.Null(result.Path);
        Assert.Equal(Path.Combine("/fake/bin", "copilot"), result.SearchedLocations[0]);
        Assert.Equal(Path.Combine(home, ".local", "bin", "copilot"), result.SearchedLocations[1]);
        Assert.Contains(Path.Combine("/", "usr", "local", "lib", "node_modules", "@github", "copilot", "npm-loader.js"), result.SearchedLocations);
        Assert.Empty(result.SkippedCandidates);
    }

    /// <summary>
    ///     Proves discovery runs on the current machine and returns a well-formed result, whether or
    ///     not a CLI is installed.
    /// </summary>
    [Fact]
    public void CopilotCli_FindInstalled_CurrentMachine_ReturnsAResult()
    {
        // Act
        var result = CopilotCli.FindInstalled();

        // Assert
        Assert.NotNull(result.SearchedLocations);
        Assert.NotNull(result.SkippedCandidates);
        Assert.True(result.Path is null || result.SearchedLocations.Contains(result.Path));
    }

    /// <summary>
    ///     Proves a search result keeps its own copy of the lists it was built from, so a later change
    ///     to those lists does not change it, and exposes them read-only.
    /// </summary>
    [Fact]
    public void CopilotCliSearchResult_Constructor_CallerListsChanged_ResultUnchanged()
    {
        // Arrange
        List<string> searched = ["/fake/a/copilot"];
        List<string> skipped = ["/fake/shim/copilot (a launcher)"];
        var result = new CopilotCliSearchResult(null, searched, skipped);

        // Act
        searched.Add("/fake/b/copilot");
        searched[0] = "/fake/changed";
        skipped.Clear();

        // Assert
        Assert.Equal(["/fake/a/copilot"], result.SearchedLocations);
        Assert.Equal(["/fake/shim/copilot (a launcher)"], result.SkippedCandidates);
        Assert.IsType<ReadOnlyCollection<string>>(result.SearchedLocations);
        Assert.IsType<ReadOnlyCollection<string>>(result.SkippedCandidates);
    }

    /// <summary>
    ///     Proves the version is read from the CLI's banner, including the update hint it prints.
    /// </summary>
    [Fact]
    public void CopilotCli_ParseVersion_CliBanner_ReturnsVersion()
    {
        // Act
        var version = CopilotCli.ParseVersion("GitHub Copilot CLI 1.0.106.\nRun 'copilot update' to check for updates.\n");

        // Assert
        Assert.Equal(new Version(1, 0, 106), version);
    }

    /// <summary>
    ///     Proves a prerelease suffix is ignored, and a version without the banner is still read.
    /// </summary>
    [Fact]
    public void CopilotCli_ParseVersion_PrereleaseSuffix_ReturnsCoreVersion()
    {
        // Act
        var banner = CopilotCli.ParseVersion("GitHub Copilot CLI v1.0.114-beta.2");
        var bare = CopilotCli.ParseVersion("1.2.3-rc.1\n");

        // Assert
        Assert.Equal(new Version(1, 0, 114), banner);
        Assert.Equal(new Version(1, 2, 3), bare);
    }

    /// <summary>
    ///     Proves output that names no version reads as none.
    /// </summary>
    [Fact]
    public void CopilotCli_ParseVersion_NoVersion_ReturnsNull()
    {
        // Act / Assert
        Assert.Null(CopilotCli.ParseVersion("unknown option --no-auto-update"));
        Assert.Null(CopilotCli.ParseVersion(string.Empty));
        Assert.Null(CopilotCli.ParseVersion(null));
    }

    /// <summary>
    ///     Proves the version policy: the minimum and anything newer is supported, anything older is not.
    /// </summary>
    /// <param name="version">The CLI version.</param>
    /// <param name="supported">Whether it is supported.</param>
    [Theory]
    [InlineData("1.0.106", false)]
    [InlineData("1.0.112", false)]
    [InlineData("1.0.113", true)]
    [InlineData("1.0.114", true)]
    [InlineData("1.1.0", true)]
    [InlineData("2.0", true)]
    public void CopilotCli_IsSupported_ComparesWithMinimumVersion(string version, bool supported)
    {
        // Act / Assert
        Assert.Equal(supported, CopilotCli.IsSupported(Version.Parse(version)));
    }

    /// <summary>
    ///     Proves the minimum version and the supported protocol are the documented pair.
    /// </summary>
    [Fact]
    public void CopilotCli_MinimumVersion_IsTheFirstCliOnTheSupportedProtocol()
    {
        // Act / Assert
        Assert.Equal(new Version(1, 0, 113), CopilotCli.MinimumVersion);
        Assert.Equal(4, CopilotCli.SupportedProtocolVersion);
    }

    /// <summary>
    ///     Proves the probe runs the CLI itself, without a shell, with <c>--no-auto-update --version</c>
    ///     in the data directory, within the probe's time limit, and returns the version it prints.
    /// </summary>
    [Fact]
    public async Task CopilotCli_ReadVersionAsync_RunsTheCliWithNoAutoUpdateAndVersion()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        var cli = machine.AddFile(Path.Combine("/fake/bin", "copilot"), length: RealCliLength);

        // Act
        var version = await CopilotCli.ReadVersionAsync(
            cli,
            DataDirectory,
            new Dictionary<string, string>(),
            machine.Context(CopilotCliPlatform.Linux, "/fake/bin"),
            machine.RunAsync,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new Version(1, 0, 113), version);
        var probe = Assert.Single(machine.Started);
        Assert.Equal(cli, probe.FileName);
        Assert.Equal(["--no-auto-update", "--version"], probe.ArgumentList);
        Assert.False(probe.UseShellExecute);
        Assert.True(probe.RedirectStandardInput);
        Assert.True(probe.RedirectStandardOutput);
        Assert.True(probe.RedirectStandardError);
        Assert.Equal(DataDirectory, probe.WorkingDirectory);
        Assert.Equal(CopilotCli.VersionProbeTimeout, Assert.Single(machine.Timeouts));
        Assert.True(CopilotCli.VersionProbeTimeout >= TimeSpan.FromMinutes(1));
    }

    /// <summary>
    ///     Proves the probe runs under exactly the runtime environment it is given, with its Copilot
    ///     home set to the data directory rather than the given one, and with no other variable.
    /// </summary>
    [Fact]
    public async Task CopilotCli_ReadVersionAsync_RunsWithTheRuntimeEnvironmentAndDataDirectoryHome()
    {
        // Arrange: a runtime environment that names a Copilot home of its own
        var machine = new FakeCopilotCliMachine();
        var cli = machine.AddFile(Path.Combine("/fake/bin", "copilot"), length: RealCliLength);
        var runtimeEnvironment = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["COPILOT_GITHUB_TOKEN"] = "token",
            ["HOME"] = "/home/user",
            ["COPILOT_HOME"] = "/home/user/.copilot",
        };

        // Act
        await CopilotCli.ReadVersionAsync(
            cli,
            DataDirectory,
            runtimeEnvironment,
            machine.Context(CopilotCliPlatform.Linux),
            machine.RunAsync,
            TestContext.Current.CancellationToken);

        // Assert: the given environment, COPILOT_HOME replaced, nothing missing and nothing added
        var expected = new Dictionary<string, string>(runtimeEnvironment) { ["COPILOT_HOME"] = DataDirectory };
        var environment = Assert.Single(machine.Started).Environment;
        Assert.Equal(
            expected.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"),
            environment.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
    }

    /// <summary>
    ///     Proves an npm installation's <c>npm-loader.js</c> is probed under <c>node</c> found on
    ///     <c>PATH</c>, named by its full path.
    /// </summary>
    [Fact]
    public async Task CopilotCli_ReadVersionAsync_JsLoader_RunsUnderNodeFromPath()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        var node = machine.AddFile(Path.Combine("/fake/node", "node"));
        var loader = machine.AddFile(Path.Combine("/fake/npm", "node_modules", "@github", "copilot", "npm-loader.js"));

        // Act
        await CopilotCli.ReadVersionAsync(
            loader,
            DataDirectory,
            new Dictionary<string, string>(),
            machine.Context(CopilotCliPlatform.Linux, "/fake/empty:/fake/node"),
            machine.RunAsync,
            TestContext.Current.CancellationToken);

        // Assert
        var probe = Assert.Single(machine.Started);
        Assert.Equal(node, probe.FileName);
        Assert.Equal([loader, "--no-auto-update", "--version"], probe.ArgumentList);
    }

    /// <summary>
    ///     Proves an npm installation without <c>node</c> on <c>PATH</c> is refused, naming Node.js,
    ///     before any process runs.
    /// </summary>
    [Fact]
    public async Task CopilotCli_ReadVersionAsync_JsLoaderWithoutNode_ThrowsNamingNode()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        var loader = machine.AddFile(Path.Combine("/fake/npm", "npm-loader.js"));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotCli.ReadVersionAsync(
            loader,
            DataDirectory,
            new Dictionary<string, string>(),
            machine.Context(CopilotCliPlatform.Windows, "/fake/empty"),
            machine.RunAsync,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(loader, failure.Message, StringComparison.Ordinal);
        Assert.Contains("Node.js", failure.Message, StringComparison.Ordinal);
        Assert.Contains("node.exe", failure.Message, StringComparison.Ordinal);
        Assert.Empty(machine.Started);
    }

    /// <summary>
    ///     Proves a probe that runs too long is refused, naming the CLI, the command and the time limit.
    /// </summary>
    [Fact]
    public async Task CopilotCli_ReadVersionAsync_TimedOut_ThrowsNamingTheTimeout()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine { Result = new(-1, string.Empty, "Package extraction", TimedOut: true) };
        var cli = machine.AddFile(Path.Combine("/fake/bin", "copilot"), length: RealCliLength);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotCli.ReadVersionAsync(
            cli,
            DataDirectory,
            new Dictionary<string, string>(),
            machine.Context(CopilotCliPlatform.Linux),
            machine.RunAsync,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(cli, failure.Message, StringComparison.Ordinal);
        Assert.Contains("'--no-auto-update' '--version'", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"{CopilotCli.VersionProbeTimeout.TotalSeconds} seconds", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Package extraction", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a probe that exits with an error is refused with its exit code and the end of its
    ///     error output.
    /// </summary>
    [Fact]
    public async Task CopilotCli_ReadVersionAsync_NonZeroExit_ThrowsWithStandardError()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine { Result = new(2, string.Empty, "error: unknown option '--no-auto-update'", TimedOut: false) };
        var cli = machine.AddFile(Path.Combine("/fake/bin", "copilot"), length: RealCliLength);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotCli.ReadVersionAsync(
            cli,
            DataDirectory,
            new Dictionary<string, string>(),
            machine.Context(CopilotCliPlatform.Linux),
            machine.RunAsync,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(cli, failure.Message, StringComparison.Ordinal);
        Assert.Contains("exited with code 2", failure.Message, StringComparison.Ordinal);
        Assert.Contains("unknown option", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a CLI that cannot be started is refused, naming it, with a redacted copy of the
    ///     platform's failure — never the original — kept as the cause.
    /// </summary>
    [Fact]
    public async Task CopilotCli_ReadVersionAsync_CannotStart_ThrowsNamingTheCli()
    {
        // Arrange
        var cause = new Win32Exception(193, "%1 is not a valid Win32 application.");
        var machine = new FakeCopilotCliMachine { Failure = cause };
        var cli = machine.AddFile(Path.Combine("/fake/bin", "copilot.exe"));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotCli.ReadVersionAsync(
            cli,
            DataDirectory,
            new Dictionary<string, string>(),
            machine.Context(CopilotCliPlatform.Windows),
            machine.RunAsync,
            TestContext.Current.CancellationToken));

        // Assert
        var copy = Assert.IsType<CopilotRedactedException>(failure.InnerException);
        Assert.Equal(typeof(Win32Exception).FullName, copy.OriginalTypeName);
        Assert.Equal(cause.Message, copy.Message);
        Assert.Contains(cli, failure.Message, StringComparison.Ordinal);
        Assert.Contains("could not be started", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves every probe refusal — a CLI that cannot be started, one that times out, one that
    ///     exits with an error and an npm installation without <c>node</c> — is redacted of the probe
    ///     environment's secret values and the host's token the caller gives, in the CLI's path, the
    ///     error output and the cause, with the error output redacted before its tail is cut, so a
    ///     secret across the cut is not half quoted.
    /// </summary>
    /// <param name="kind">The failure: <c>start</c>, <c>timeout</c>, <c>exit</c> or <c>node</c>.</param>
    [Theory]
    [InlineData("start")]
    [InlineData("timeout")]
    [InlineData("exit")]
    [InlineData("node")]
    public async Task CopilotCli_ReadVersionAsync_ProbeQuotingASecret_IsRedacted(string kind)
    {
        // Arrange: a secret in the probe's environment, in the CLI's folder with the host's token, and
        // in the error output placed so the tail cut, made on the raw output, would fall inside it
        const string secret = "s3cr3t-value-xyz";
        const string hostToken = "s3cr3t-host-token";
        const string shaped = "gho_PROBE0123456789abcdefABCDEF01234567";
        var folder = $"/opt/{secret}/{hostToken}/bin";
        var suffix = $" failed with {shaped} ";
        var standardError = new string('x', 1000) + secret + suffix + new string('y', 500 - (secret.Length / 2) - suffix.Length);
        var machine = new FakeCopilotCliMachine
        {
            Result = kind == "exit"
                ? new(3, string.Empty, standardError, TimedOut: false)
                : new(-1, string.Empty, standardError, TimedOut: true),
            Failure = kind == "start" ? new Win32Exception(2, $"cannot start {folder}/copilot ({secret})") : null,
        };
        var cli = machine.AddFile(Path.Combine(folder, kind == "node" ? "npm-loader.js" : "copilot"), length: RealCliLength);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["GH_ENTERPRISE_TOKEN"] = secret, ["PATH"] = "/usr/bin" };

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotCli.ReadVersionAsync(
            cli,
            DataDirectory,
            environment,
            machine.Context(CopilotCliPlatform.Linux, "/usr/bin"),
            machine.RunAsync,
            TestContext.Current.CancellationToken,
            hostToken: hostToken));

        // Assert
        Assert.Contains("/opt/<redacted>/<redacted>/bin", failure.Message, StringComparison.Ordinal);
        if (kind is "timeout" or "exit")
        {
            Assert.Contains("redacted> failed with gho_<redacted>", failure.Message, StringComparison.Ordinal);
        }

        if (kind == "start")
        {
            Assert.Equal(typeof(Win32Exception).FullName, Assert.IsType<CopilotRedactedException>(failure.InnerException).OriginalTypeName);
        }

        SecretAssert.NoSecret(failure, secret, hostToken, shaped, secret[..8], secret[^8..]);
    }

    /// <summary>
    ///     Proves output that names no version reads as none, leaving the decision to the SDK's
    ///     handshake.
    /// </summary>
    [Fact]
    public async Task CopilotCli_ReadVersionAsync_NoVersionInOutput_ReturnsNull()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine { Result = new(0, "copilot (development build)", string.Empty, TimedOut: false) };
        var cli = machine.AddFile(Path.Combine("/fake/bin", "copilot"), length: RealCliLength);

        // Act
        var version = await CopilotCli.ReadVersionAsync(
            cli,
            DataDirectory,
            new Dictionary<string, string>(),
            machine.Context(CopilotCliPlatform.Linux),
            machine.RunAsync,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(version);
    }

    /// <summary>
    ///     Proves the refusal of a CLI that is too old names its path, its version, the minimum, the
    ///     version check that matches what AgentKit runs, the upgrade for every installation method,
    ///     and the alternatives.
    /// </summary>
    [Fact]
    public void CopilotCli_ExplainTooOld_NamesVersionsPathAndUpgradeCommands()
    {
        // Act
        var message = CopilotCli.ExplainTooOld("/fake/bin/copilot", new Version(1, 0, 106));

        // Assert
        Assert.Contains("'/fake/bin/copilot'", message, StringComparison.Ordinal);
        Assert.Contains("version 1.0.106", message, StringComparison.Ordinal);
        Assert.Contains("1.0.113 or later", message, StringComparison.Ordinal);
        Assert.Contains("protocol version 4", message, StringComparison.Ordinal);
        Assert.Contains("copilot --no-auto-update --version", message, StringComparison.Ordinal);
        Assert.Contains("copilot update", message, StringComparison.Ordinal);
        Assert.Contains("winget upgrade GitHub.Copilot", message, StringComparison.Ordinal);
        Assert.Contains("brew upgrade --cask copilot-cli", message, StringComparison.Ordinal);
        Assert.Contains("npm install -g @github/copilot@latest", message, StringComparison.Ordinal);
        Assert.Contains("gh.io/copilot-install", message, StringComparison.Ordinal);
        Assert.Contains("cliPath", message, StringComparison.Ordinal);
        Assert.Contains("CopilotRuntimeSource.Bundled", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the explanation for a CLI that was not found lists where AgentKit looked, what it
    ///     passed over, how to install the CLI, and the alternatives.
    /// </summary>
    [Fact]
    public void CopilotCli_ExplainNotFound_ListsSearchedSkippedAndInstallCommands()
    {
        // Arrange
        var result = new CopilotCliSearchResult(null, ["/fake/a/copilot", "/fake/b/copilot"], ["/fake/shim/copilot (a launcher)"]);

        // Act
        var message = CopilotCli.ExplainNotFound(result);

        // Assert
        Assert.Contains("/fake/a/copilot; /fake/b/copilot", message, StringComparison.Ordinal);
        Assert.Contains("/fake/shim/copilot (a launcher)", message, StringComparison.Ordinal);
        Assert.Contains("COPILOT_CLI_PATH is not consulted", message, StringComparison.Ordinal);
        Assert.Contains("1.0.113 or later", message, StringComparison.Ordinal);
        Assert.Contains("winget install GitHub.Copilot", message, StringComparison.Ordinal);
        Assert.Contains("brew install --cask copilot-cli", message, StringComparison.Ordinal);
        Assert.Contains("npm install -g @github/copilot", message, StringComparison.Ordinal);
        Assert.Contains("curl -fsSL https://gh.io/copilot-install | bash", message, StringComparison.Ordinal);
        Assert.Contains("cliPath", message, StringComparison.Ordinal);
        Assert.Contains("CopilotRuntimeSource.Bundled", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a runtime reporting an older protocol is explained as a CLI to upgrade.
    /// </summary>
    [Fact]
    public void CopilotCli_ExplainProtocolMismatch_OlderServer_GivesUpgradeGuidance()
    {
        // Act
        var message = CopilotCli.ExplainProtocolMismatch("/fake/bin/copilot", new Version(1, 0, 112), 3);

        // Assert
        Assert.Contains("'/fake/bin/copilot' (version 1.0.112)", message, StringComparison.Ordinal);
        Assert.Contains("speaks SDK protocol version 3", message, StringComparison.Ordinal);
        Assert.Contains("1.0.113 or later", message, StringComparison.Ordinal);
        Assert.Contains("copilot --no-auto-update --version", message, StringComparison.Ordinal);
        Assert.Contains("winget upgrade GitHub.Copilot", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a runtime reporting a newer protocol is explained as a CLI this release of AgentKit
    ///     predates, advising an AgentKit update rather than a CLI upgrade.
    /// </summary>
    [Fact]
    public void CopilotCli_ExplainProtocolMismatch_NewerServer_AdvisesUpdatingAgentKit()
    {
        // Act
        var message = CopilotCli.ExplainProtocolMismatch("/fake/bin/copilot", null, 5);

        // Assert
        Assert.Contains("speaks SDK protocol version 5", message, StringComparison.Ordinal);
        Assert.Contains("newer than this release of AgentKit", message, StringComparison.Ordinal);
        Assert.Contains("Update AgentKit", message, StringComparison.Ordinal);
        Assert.Contains("cliPath", message, StringComparison.Ordinal);
        Assert.Contains("CopilotRuntimeSource.Bundled", message, StringComparison.Ordinal);
        Assert.DoesNotContain("winget upgrade", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the protocol version the runtime reported is read from the SDK's mismatch error.
    /// </summary>
    [Fact]
    public void CopilotCli_ParseServerProtocol_SdkMismatchMessage_ReturnsTheServerVersion()
    {
        // Arrange
        const string message = "SDK protocol version mismatch: SDK supports versions 4-4, but server reports version 3. "
            + "Please update your SDK or server to ensure compatibility.";

        // Act / Assert
        Assert.True(CopilotCli.IsProtocolMismatch(message));
        Assert.Equal(3, CopilotCli.ParseServerProtocol(message));
        Assert.Null(CopilotCli.ParseServerProtocol("connection refused"));
        Assert.False(CopilotCli.IsProtocolMismatch("connection refused"));
    }

    /// <summary>
    ///     Proves the real process runner captures a process's standard output, standard error and
    ///     exit code.
    /// </summary>
    [Fact]
    public async Task CopilotCli_RunProcessAsync_RealProcess_CapturesOutputAndExitCode()
    {
        // Arrange: the platform's shell prints to both streams and exits with 3
        var startInfo = OperatingSystem.IsWindows()
            ? Redirected(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c", "echo GitHub Copilot CLI 1.0.113.& echo problem 1>&2& exit /b 3")
            : Redirected(Path.Combine(Path.AltDirectorySeparatorChar.ToString(), "bin", "sh"), "-c", "echo 'GitHub Copilot CLI 1.0.113.'; echo problem >&2; exit 3");

        // Act
        var result = await CopilotCli.RunProcessAsync(startInfo, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.TimedOut);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal(new Version(1, 0, 113), CopilotCli.ParseVersion(result.StandardOutput));
        Assert.Contains("problem", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the real process runner stops a process that runs too long and reports it as timed out.
    /// </summary>
    [Fact]
    public async Task CopilotCli_RunProcessAsync_ExceedsTimeout_KillsAndReportsTimedOut()
    {
        // Arrange: a process that would run for half a minute
        var startInfo = LongRunning();

        // Act
        var result = await CopilotCli.RunProcessAsync(startInfo, TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.TimedOut);
    }

    /// <summary>
    ///     Proves the real process runner reports a process that exits while a process it started
    ///     still holds its output open as timed out, so the limit bounds the output as well as the exit.
    /// </summary>
    [Fact]
    public async Task CopilotCli_RunProcessAsync_ExitsWithOutputHeldOpen_ReportsTimedOut()
    {
        // Arrange: a shell that starts a half-minute background child, which inherits its output, and exits at once
        var startInfo = OperatingSystem.IsWindows()
            ? Redirected(
                Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                "/c",
                $"start /b {Path.Combine(Environment.SystemDirectory, "PING.EXE")} -n 30 127.0.0.1 & exit /b 0")
            : Redirected(Path.Combine(Path.AltDirectorySeparatorChar.ToString(), "bin", "sh"), "-c", "sleep 30 & exit 0");

        // Act
        var result = await CopilotCli.RunProcessAsync(startInfo, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.TimedOut);
    }

    /// <summary>
    ///     Proves the real process runner, when a process exits while a process it started still holds its
    ///     output open past the limit, stops the process holding the output (Windows and Linux).
    /// </summary>
    [Fact]
    public async Task CopilotCli_RunProcessAsync_ExitsWithOutputHeldOpen_StopsTheProcessHoldingIt()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows() || OperatingSystem.IsLinux(),
            "A child orphaned by the probe's exit is stopped only on Windows and Linux.");

        if (OperatingSystem.IsWindows())
        {
            // Arrange: after a one-second lead, a shell starts a second shell, which starts a two-minute
            // grandchild under a unique name and exits; the grandchild inherits the output and outlives both
            // shells, so neither its parent nor the probe is left running
            await WithUniqueWaiterAsync(async waiter =>
            {
                var startInfo = WindowsShell(
                    $"{Ping()} -n 2 127.0.0.1 >nul & start \"\" /b {System32("cmd.exe")} /d /s /c \"start \"\" /b \"{waiter.Path}\" -n 120 127.0.0.1\" & exit /b 0");

                // Act
                var result = await CopilotCli.RunProcessAsync(startInfo, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

                // Assert: the child held the output past the limit, and was stopped
                Assert.True(result.TimedOut);
                Assert.True(waiter.AllExitWithin(TimeSpan.FromSeconds(30)));
            });
            return;
        }

        // Arrange: a shell starts a two-minute child, which inherits its output, records the child's
        // process ID and exits
        var pidFile = Path.Combine(Path.GetTempPath(), $"agentkit-{Guid.NewGuid():N}.pid");
        try
        {
            var startInfo = Redirected(
                Path.Combine(Path.AltDirectorySeparatorChar.ToString(), "bin", "sh"),
                "-c",
                "sleep 120 & echo $! > \"$1\"; exit 0",
                "sh",
                pidFile);

            // Act
            var result = await CopilotCli.RunProcessAsync(startInfo, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // Assert: the child held the output past the limit, and was stopped
            Assert.True(result.TimedOut);
            Assert.True(ExitsWithin(ReadPid(pidFile), TimeSpan.FromSeconds(30)));
        }
        finally
        {
            if (File.Exists(pidFile))
            {
                KillQuietly(ReadPid(pidFile));
                File.Delete(pidFile);
            }
        }
    }

    /// <summary>
    ///     Proves that ending the tracking of a probe stops a process the probe started and left running
    ///     after the probe exited (Windows), as happens when a run ends.
    /// </summary>
    [Fact]
    public async Task CopilotCliProcessTree_Dispose_ProbeLeftAChildRunning_StopsIt()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A job object holds the probe's processes only on Windows.");

        // Arrange: after a one-second lead, a shell starts a two-minute child under a unique name and exits
        await WithUniqueWaiterAsync(waiter =>
        {
            using var process = new Process
            {
                StartInfo = WindowsShell(
                    $"{Ping()} -n 2 127.0.0.1 >nul & start \"\" /b \"{waiter.Path}\" -n 120 127.0.0.1 >nul & exit /b 0"),
            };
            process.Start();
            var tree = CopilotCliProcessTree.Track(process);
            try
            {
                process.StandardInput.Close();
                Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)));
                Assert.True(waiter.IsRunning());
            }
            finally
            {
                // Act
                tree.Dispose();
            }

            // Assert
            Assert.True(waiter.AllExitWithin(TimeSpan.FromSeconds(30)));
            return Task.CompletedTask;
        });
    }

    /// <summary>
    ///     Proves the real process runner stops a process when the run is canceled, and the
    ///     cancellation propagates.
    /// </summary>
    [Fact]
    public async Task CopilotCli_RunProcessAsync_Canceled_KillsAndThrowsOperationCanceled()
    {
        // Arrange: a process that would run for half a minute, canceled shortly after it starts
        var startInfo = LongRunning();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(500));

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CopilotCli.RunProcessAsync(startInfo, TimeSpan.FromSeconds(60), cancellation.Token));
    }

    /// <summary>
    ///     Builds start information for a process that runs about half a minute.
    /// </summary>
    /// <returns>The start information.</returns>
    private static ProcessStartInfo LongRunning() =>
        OperatingSystem.IsWindows()
            ? Redirected(Path.Combine(Environment.SystemDirectory, "PING.EXE"), "-n", "30", "127.0.0.1")
            : Redirected(Path.Combine(Path.AltDirectorySeparatorChar.ToString(), "bin", "sleep"), "30");

    /// <summary>
    ///     Builds start information that runs a command line in the Windows command shell, every stream
    ///     redirected; the command line is passed verbatim, so it may quote paths.
    /// </summary>
    /// <param name="command">The command line.</param>
    /// <returns>The start information.</returns>
    private static ProcessStartInfo WindowsShell(string command)
    {
        var startInfo = Redirected(Path.Combine(Environment.SystemDirectory, "cmd.exe"));
        startInfo.Arguments = $"/d /s /c \"{command}\"";
        return startInfo;
    }

    /// <summary>
    ///     Gets the quoted path of the system's <c>PING.EXE</c>.
    /// </summary>
    /// <returns>The quoted path.</returns>
    private static string Ping() => System32("PING.EXE");

    /// <summary>
    ///     Gets the quoted path of a program in the Windows system folder.
    /// </summary>
    /// <param name="program">The program's file name.</param>
    /// <returns>The quoted path.</returns>
    private static string System32(string program) => $"\"{Path.Combine(Environment.SystemDirectory, program)}\"";

    /// <summary>
    ///     Runs a test with a copy of <c>PING.EXE</c> under a unique name, which waits as the original does,
    ///     then stops any process still running it and deletes the copy (Windows).
    /// </summary>
    /// <param name="test">The test.</param>
    /// <returns>A task that completes when the test and the clean-up have run.</returns>
    private static async Task WithUniqueWaiterAsync(Func<UniqueWaiter, Task> test)
    {
        var name = $"akh{Guid.NewGuid():N}"[..11];
        var waiter = new UniqueWaiter(name, Path.Combine(AppContext.BaseDirectory, name + ".exe"));
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), waiter.Path);
        try
        {
            await test(waiter);
        }
        finally
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(TimeSpan.FromSeconds(10));
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                    {
                        // It exited on its own.
                    }
                }
            }

            try
            {
                File.Delete(waiter.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The copy is still being released; the output folder is disposable.
            }
        }
    }

    /// <summary>
    ///     Reads the process ID a test shell recorded.
    /// </summary>
    /// <param name="path">The file holding it.</param>
    /// <returns>The process ID.</returns>
    private static int ReadPid(string path) =>
        int.Parse(File.ReadAllText(path).Trim(), NumberStyles.None, CultureInfo.InvariantCulture);

    /// <summary>
    ///     Waits, up to a bound, for a process to be gone.
    /// </summary>
    /// <param name="pid">The process ID.</param>
    /// <param name="bound">The longest wait.</param>
    /// <returns><see langword="true"/> when the process is gone within the bound.</returns>
    private static bool ExitsWithin(int pid, TimeSpan bound)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit(bound);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    /// <summary>
    ///     Stops the test's <c>sleep</c> process if it is still running, as a best effort; a reused
    ///     process ID naming another program is left alone.
    /// </summary>
    /// <param name="pid">The process ID.</param>
    private static void KillQuietly(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (string.Equals(process.ProcessName, "sleep", StringComparison.Ordinal))
            {
                process.Kill();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // It is gone.
        }
    }

    /// <summary>
    ///     A copy of <c>PING.EXE</c> under a unique name, so the processes running it can be found.
    /// </summary>
    /// <param name="Name">The process name.</param>
    /// <param name="Path">The copy's full path.</param>
    private sealed record UniqueWaiter(string Name, string Path)
    {
        /// <summary>
        ///     Determines whether a process is running the copy.
        /// </summary>
        /// <returns><see langword="true"/> when one is.</returns>
        public bool IsRunning()
        {
            var processes = Process.GetProcessesByName(Name);
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return processes.Length > 0;
        }

        /// <summary>
        ///     Determines whether every process running the copy exits within a bound.
        /// </summary>
        /// <param name="bound">The longest wait for each.</param>
        /// <returns><see langword="true"/> when they all exit within the bound.</returns>
        public bool AllExitWithin(TimeSpan bound)
        {
            var exited = true;
            foreach (var process in Process.GetProcessesByName(Name))
            {
                using (process)
                {
                    exited &= process.WaitForExit(bound);
                }
            }

            return exited;
        }
    }

    /// <summary>
    ///     Builds start information with every stream redirected and no shell, as the probe uses.
    /// </summary>
    /// <param name="fileName">The program.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The start information.</returns>
    private static ProcessStartInfo Redirected(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
