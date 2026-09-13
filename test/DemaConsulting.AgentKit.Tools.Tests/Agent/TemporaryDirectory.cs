namespace DemaConsulting.AgentKit.Tools.Tests.Agent;

/// <summary>
///     Creates a real temporary directory and removes it when the test is done with it.
/// </summary>
/// <remarks>
///     <para>
///     Path grants are judged against real, resolved locations, so a test that narrows a child's
///     reach has to narrow it to somewhere that actually exists. This fixture is the smallest thing
///     that provides one.
///     </para>
///     <para>
///     The directory is named with a fresh identifier so that concurrently executing tests, and
///     leftovers from an interrupted run, can never collide. Removal is best-effort: a failure to
///     delete a temporary directory must not fail a test that has already proved its point.
///     </para>
///     <para>
///     Instances are not thread-safe and are not intended to be shared between tests.
///     </para>
/// </remarks>
internal sealed class TemporaryDirectory : IDisposable
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TemporaryDirectory"/> class, creating the
    ///     directory on disk.
    /// </summary>
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"agent-kit-agent-tests-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path);
    }

    /// <summary>
    ///     Gets the full path of the created directory.
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     Removes the directory and everything under it.
    /// </summary>
    /// <remarks>
    ///     Failures are swallowed: the directory is under the operating system's temporary location
    ///     and a leftover there is harmless, whereas a teardown failure masking a passing test is
    ///     not.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file left open by another process is not this test's concern.
        }
        catch (UnauthorizedAccessException)
        {
            // A read-only leftover is not this test's concern either.
        }
    }
}
