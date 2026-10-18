using System.Linq;
using GdUnit4;
using Godot;
using Godot.Collections;
using ErrorType = Godot.Logger.ErrorType;
using Microsoft.Extensions.Logging;
using ZLogger;
using static GdUnit4.Assertions;

namespace Enaweg.Logger.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class GodotOSLoggerTest
{
    [TestCase]
    public void LogError_WithEmptyRationale_FallsBackToCode()
    {
        var recorder = new LogRecorder();
        var sut = new GodotOSLogger(recorder.CreateLogger("test"));

        // GD.PushError and the ERR_* macros put the message in "code" and leave "rationale" empty.
        LogError(sut, code: "something went wrong", rationale: "", errorType: ErrorType.Error);

        AssertThat(recorder.Entries).HasSize(1);
        AssertThat(recorder.Entries[0].Level).IsEqual(LogLevel.Error);
        AssertThat(recorder.Entries[0].Message).IsEqual("something went wrong");
    }

    [TestCase]
    public void LogError_WithRationale_PrefersRationale()
    {
        var recorder = new LogRecorder();
        var sut = new GodotOSLogger(recorder.CreateLogger("test"));

        LogError(sut, code: "Condition \"x\" is true.", rationale: "x must not be set", errorType: ErrorType.Error);

        AssertThat(recorder.Entries).HasSize(1);
        AssertThat(recorder.Entries[0].Message).IsEqual("x must not be set");
    }

    [TestCase]
    public void LogError_WithWarningType_LogsAtWarningLevel()
    {
        var recorder = new LogRecorder();
        var sut = new GodotOSLogger(recorder.CreateLogger("test"));

        LogError(sut, code: "heads up", rationale: "", errorType: ErrorType.Warning);

        AssertThat(recorder.Entries).HasSize(1);
        AssertThat(recorder.Entries[0].Level).IsEqual(LogLevel.Warning);
        AssertThat(recorder.Entries[0].Message).IsEqual("heads up");
    }

    [TestCase]
    public void LogError_ForScriptAndShaderTypes_LogsAtErrorLevel()
    {
        var recorder = new LogRecorder();
        var sut = new GodotOSLogger(recorder.CreateLogger("test"));

        LogError(sut, code: "script boom", rationale: "", errorType: ErrorType.Script);
        LogError(sut, code: "shader boom", rationale: "", errorType: ErrorType.Shader);

        AssertThat(recorder.Entries).HasSize(2);
        AssertThat(recorder.Entries[0].Level).IsEqual(LogLevel.Error);
        AssertThat(recorder.Entries[0].Message).IsEqual("script boom");
        AssertThat(recorder.Entries[1].Level).IsEqual(LogLevel.Error);
        AssertThat(recorder.Entries[1].Message).IsEqual("shader boom");
    }

    [TestCase]
    public void LogError_WithAnUnknownErrorType_IsStillReportedAsAnError()
    {
        var recorder = new LogRecorder();
        var sut = new GodotOSLogger(recorder.CreateLogger);

        // An error type added by a future Godot version must be reported, not dropped.
        LogError(sut, code: "future", rationale: "", errorType: (ErrorType)99);

        AssertThat(recorder.Entries).ContainsExactly(new RecordedLog("Godot.Engine", LogLevel.Error, "future"));
    }

    [TestCase]
    public void LogError_ForwardsGodotSourceLocationAsCallerInfo()
    {
        var sink = new MemorySink();
        using var factory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddZLoggerLogProcessor(sink);
        });
        var sut = new GodotOSLogger(factory.CreateLogger);

        sut._LogError("Node::add_child", "scene/main/node.cpp", 1234, "code", "", false, (int)ErrorType.Error,
            new Array<ScriptBacktrace>());

        // The location belongs to the engine code that raised the error, not to GodotOSLogger.
        var entry = sink.Entries.Single();
        AssertThat(entry.MemberName).IsEqual("Node::add_child");
        AssertThat(entry.FilePath).IsEqual("scene/main/node.cpp");
        AssertThat(entry.LineNumber).IsEqual(1234);
    }

    [TestCase]
    public void LogMessage_WhileAProcessorIsWritingToGodot_IsIgnored()
    {
        var recorder = new LogRecorder();
        var sut = new GodotOSLogger(recorder.CreateLogger("test"));

        using (GodotLogGuard.Enter())
        {
            sut._LogMessage("echo of our own output", false);
        }

        AssertThat(recorder.Entries).IsEmpty();
    }

    [TestCase]
    public void LogError_WhileAProcessorIsWritingToGodot_IsIgnored()
    {
        var recorder = new LogRecorder();
        var sut = new GodotOSLogger(recorder.CreateLogger("test"));

        // GodotDebugLogProcessor writes errors and warnings with GD.PushError/GD.PushWarning, which arrive here.
        using (GodotLogGuard.Enter())
        {
            LogError(sut, code: "echo of our own error", rationale: "", errorType: ErrorType.Error);
            LogError(sut, code: "echo of our own warning", rationale: "", errorType: ErrorType.Warning);
        }

        AssertThat(recorder.Entries).IsEmpty();
    }

    [TestCase]
    public void LogError_UsesACategoryPerGodotErrorType()
    {
        var recorder = new LogRecorder();
        var sut = new GodotOSLogger(recorder.CreateLogger);

        LogError(sut, code: "engine", rationale: "", errorType: ErrorType.Error);
        LogError(sut, code: "warn", rationale: "", errorType: ErrorType.Warning);
        LogError(sut, code: "script", rationale: "", errorType: ErrorType.Script);
        LogError(sut, code: "shader", rationale: "", errorType: ErrorType.Shader);

        AssertThat(recorder.Entries).ContainsExactly(
            new RecordedLog("Godot.Engine", LogLevel.Error, "engine"),
            new RecordedLog("Godot.Engine", LogLevel.Warning, "warn"),
            new RecordedLog("Godot.Script", LogLevel.Error, "script"),
            new RecordedLog("Godot.Shader", LogLevel.Error, "shader"));
    }

    [TestCase]
    public void LogMessage_UsesTheOutputCategory()
    {
        var recorder = new LogRecorder();
        var sut = new GodotOSLogger(recorder.CreateLogger);

        sut._LogMessage("printed", false);
        sut._LogMessage("printed to stderr", true);

        AssertThat(recorder.Entries).ContainsExactly(
            new RecordedLog("Godot.Output", LogLevel.Information, "printed"),
            new RecordedLog("Godot.Output", LogLevel.Error, "printed to stderr"));
    }

    [TestCase]
    public void Categories_HonourTheConfiguredPrefix()
    {
        var recorder = new LogRecorder();
        var sut = new GodotOSLogger(recorder.CreateLogger, "MyGame.Engine");

        LogError(sut, code: "shader", rationale: "", errorType: ErrorType.Shader);
        sut._LogMessage("printed", false);

        AssertThat(recorder.Entries).ContainsExactly(
            new RecordedLog("MyGame.Engine.Shader", LogLevel.Error, "shader"),
            new RecordedLog("MyGame.Engine.Output", LogLevel.Information, "printed"));
    }

    [TestCase]
    public void DefaultCategoryPrefix_IsUsedByTheOptions()
    {
        // The documented filter examples ("Godot.Output", "Godot.Shader", ...) rely on this default.
        AssertThat(new ZLoggerGodotDebugOptions().EngineCategoryPrefix).IsEqual("Godot");
    }

    static void LogError(GodotOSLogger sut, string code, string rationale, ErrorType errorType) =>
        sut._LogError("SomeFunction", "res://some_file.cs", 42, code, rationale, false, (int)errorType,
            new Array<ScriptBacktrace>());
}
