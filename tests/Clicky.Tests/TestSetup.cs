using System.Runtime.CompilerServices;
using Clicky.Core;

namespace Clicky.Tests;

internal static class TestSetup
{
    [ModuleInitializer]
    internal static void DisableFileLogging() => Log.FilePath = null;
}
