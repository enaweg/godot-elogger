using System;
using System.Diagnostics;
using GdUnit4;
using static GdUnit4.Assertions;

namespace Enaweg.Logger.Tests;

[TestSuite]
[RequireGodotRuntime]
public class DiagnosticsHelperTest
{
    [TestCase]
    public void CleanupStackTrace_IncludesCallingMethodAndParameters()
    {
        var result = CaptureStackTraceFrom(42, "hello");

        AssertThat(result).Contains(nameof(CleanupStackTrace_IncludesCallingMethodAndParameters));
        AssertThat(result).Contains("CaptureStackTraceFrom");
        AssertThat(result).Contains("int value");
        AssertThat(result).Contains("string text");
    }

    [TestCase]
    public void CleanupStackTrace_OnEmptyStackTrace_ReturnsEmptyString()
    {
        // An exception that was never thrown carries no captured frames.
        var emptyTrace = new StackTrace(new Exception(), false);

        var result = DiagnosticsHelper.CleanupStackTrace(emptyTrace);

        AssertThat(result).IsEqual(string.Empty);
    }

    [TestCase]
    public void CleanupStackTrace_ExcludesMicrosoftLoggingFrames()
    {
        var result = CaptureStackTraceFrom(1, "x");

        AssertThat(result).NotContains("Microsoft.Extensions.Logging");
        AssertThat(result).NotContains("ZLogger.");
    }

    static string CaptureStackTraceFrom(int value, string text)
    {
        var stackTrace = new StackTrace(true);
        return DiagnosticsHelper.CleanupStackTrace(stackTrace);
    }
}
