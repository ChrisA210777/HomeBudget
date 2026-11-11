using System.Runtime.CompilerServices;

namespace Budget.Client.Tests;

/// <summary>Settings for the whole test run.</summary>
internal static class TestSetup
{
    /// <summary>
    /// bUnit's WaitFor helpers give up after one second by default, which shared CI runners sometimes miss when busy,
    /// making tests fail now and then. Five seconds only slows a test that's really failing.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialise() => Bunit.TestContextBase.DefaultWaitTimeout = TimeSpan.FromSeconds(5);
}
