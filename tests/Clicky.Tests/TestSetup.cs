using System.Runtime.CompilerServices;

namespace Clicky.Tests;

internal static class TestSetup
{
    /// <summary>Keeps the tests from writing into the real ~/.local/share/clicky.</summary>
    [ModuleInitializer]
    internal static void DisableFileLogging() => Log.FilePath = null;
}
