using System.Linq;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Tests;

public class AccessibilityExtensionsTests
{
    [Test]
    public void AccessibilityTests()
    {
        var appContext = GameLoader.LoadSimpleGame();
        var mscorlib = appContext.AssembliesByName["mscorlib"];
        var coreModule = appContext.AssembliesByName["UnityEngine.CoreModule"];

        var console = GetTypeByFullName(mscorlib, "System.Console");//public
        var consoleWindowsConsole = GetTypeByFullName(mscorlib, "System.Console.WindowsConsole");//private nested
        var dateTimeFormat = GetTypeByFullName(mscorlib, "System.DateTimeFormat");//internal
        var gameObject = GetTypeByFullName(coreModule, "UnityEngine.GameObject");//public
        Assert.Multiple(() =>
        {
            AssertAccessibleTo(console, consoleWindowsConsole);
            AssertAccessibleTo(consoleWindowsConsole, console);
            AssertAccessibleTo(console, gameObject);
            AssertNotAccessibleTo(consoleWindowsConsole, gameObject);
            AssertNotAccessibleTo(gameObject, console);
            AssertAccessibleTo(consoleWindowsConsole, consoleWindowsConsole);
            AssertAccessibleTo(dateTimeFormat, consoleWindowsConsole);
            AssertNotAccessibleTo(consoleWindowsConsole, dateTimeFormat);
            AssertNotAccessibleTo(dateTimeFormat, gameObject);
        });
    }

    private static void AssertAccessibleTo(TypeAnalysisContext type1, TypeAnalysisContext type2)
    {
        Assert.That(type1.IsAccessibleTo(type2), () => $"{type1.FullName} is not accessible to {type2.FullName}, but should be.");
    }

    private static void AssertNotAccessibleTo(TypeAnalysisContext type1, TypeAnalysisContext type2)
    {
        Assert.That(!type1.IsAccessibleTo(type2), () => $"{type1.FullName} is accessible to {type2.FullName}, but shouldn't be.");
    }

    private static TypeAnalysisContext GetTypeByFullName(AssemblyAnalysisContext assembly, string fullName)
    {
        return assembly.Types.FirstOrDefault(t => t.FullName == fullName) ?? throw new($"Could not find {fullName} in {assembly.CleanAssemblyName}.");
    }
}
