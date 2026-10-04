using Xunit.Abstractions;
using Xunit.Sdk;

namespace SeraphHorizons.PackTests;

/// <summary>
/// A scenario that reads <c>World.BootDiagnostics</c> for what the boot logged. Atlas keeps that list
/// growing for as long as the server runs, so in a class whose world several features share
/// (<see cref="SharedWorldScenarios"/>, <see cref="WoodworkingScenarios"/>) it would also hold whatever
/// the other features' scenarios logged before it. The class's <see cref="BootLogFirst"/> runs these
/// first, so what they read is the boot's.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ReadsBootLogAttribute : Attribute;

/// <summary>xunit's own order, with the <see cref="ReadsBootLogAttribute"/> scenarios moved to the
/// front. For a shared class: <c>[TestCaseOrderer(BootLogFirst.Name, BootLogFirst.Assembly)]</c>.</summary>
public sealed class BootLogFirst(IMessageSink diagnostics) : ITestCaseOrderer
{
    public const string Name = "SeraphHorizons.PackTests.BootLogFirst";
    public const string Assembly = "PackTests";

    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : ITestCase
    {
        var ordered = new DefaultTestCaseOrderer(diagnostics).OrderTestCases(testCases).ToList();
        bool First(TTestCase test) =>
            test.TestMethod.Method.GetCustomAttributes(typeof(ReadsBootLogAttribute).AssemblyQualifiedName).Any();
        return ordered.Where(First).Concat(ordered.Where(test => !First(test)));
    }
}
