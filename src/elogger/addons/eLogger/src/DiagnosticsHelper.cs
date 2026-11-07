using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text.RegularExpressions;
using Godot;

namespace Enaweg.Logger;

public static class DiagnosticsHelper
{
    static readonly Regex typeBeautifyRegex = new("`.+$", RegexOptions.Compiled);

    static readonly Dictionary<Type, string> builtInTypeNames = new()
    {
        { typeof(void), "void" },
        { typeof(bool), "bool" },
        { typeof(byte), "byte" },
        { typeof(char), "char" },
        { typeof(decimal), "decimal" },
        { typeof(double), "double" },
        { typeof(float), "float" },
        { typeof(int), "int" },
        { typeof(long), "long" },
        { typeof(object), "object" },
        { typeof(sbyte), "sbyte" },
        { typeof(short), "short" },
        { typeof(string), "string" },
        { typeof(uint), "uint" },
        { typeof(ulong), "ulong" },
        { typeof(ushort), "ushort" }
    };

    static string? applicationDataPath;
    static bool displayFilenames = true;

    /// <summary>
    /// Formats an exception like <see cref="Exception.ToString" /> - type, message and inner exceptions - but with
    /// cleaned-up stack traces. Inner exceptions follow their outer exception, each introduced by <c>---&gt;</c>.
    /// </summary>
    public static string CleanupException(Exception exception)
    {
        var sb = new StringBuilder();
        AppendException(sb, exception, false);
        return sb.ToString();
    }

    static void AppendException(StringBuilder sb, Exception exception, bool isInner)
    {
        if (isInner)
        {
            sb.Append("---> ");
        }

        sb.Append(exception.GetType().FullName).Append(": ").AppendLine(exception.Message);
        var cleaned = CleanupStackTrace(new StackTrace(exception, true), out var unresolvedFrames);
        sb.Append(SelectTrace(cleaned, unresolvedFrames, exception.StackTrace));

        // An AggregateException's InnerException is only the first of its inner exceptions.
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                AppendException(sb, inner, true);
            }
        }
        else if (exception.InnerException is { } inner)
        {
            AppendException(sb, inner, true);
        }
    }

    /// <summary>
    /// Picks the trace to print for an exception. Under NativeAOT, which Godot's iOS export uses, stack frames carry
    /// no method metadata, so the cleaned-up trace comes out empty although the runtime's own trace still names every
    /// frame. Print that one then rather than an exception without any trace.
    /// </summary>
    internal static string SelectTrace(string cleaned, int unresolvedFrames, string? raw)
    {
        if (cleaned.Length > 0 || unresolvedFrames == 0 || string.IsNullOrEmpty(raw))
        {
            return cleaned;
        }

        return raw + System.Environment.NewLine;
    }

    public static string CleanupStackTrace(StackTrace stackTrace) => CleanupStackTrace(stackTrace, out _);

    static string CleanupStackTrace(StackTrace stackTrace, out int unresolvedFrames)
    {
        unresolvedFrames = 0;
        var sb = new StringBuilder();
        for (var i = 0; i < stackTrace.FrameCount; i++)
        {
            var sf = stackTrace.GetFrame(i);

            // Frames without resolvable metadata (native, inlined, trimmed or NativeAOT) have no method or no
            // declaring type. They carry nothing worth printing and must not throw - this runs inside the log
            // pipeline.
            if (sf is null) continue;

            var mb = sf.GetMethod();
            if (mb?.DeclaringType is null)
            {
                unresolvedFrames++;
                continue;
            }

            // Runtime plumbing such as ExceptionDispatchInfo.Throw and TaskAwaiter is marked [StackTraceHidden];
            // the runtime's own trace leaves it out, so this one does too.
            if (IsHidden(mb)) continue;

            // An async or iterator method runs as MoveNext of a compiler-generated state machine. Show the method
            // that was written, as the runtime's own trace does, instead of "<Method>d__3.MoveNext()".
            mb = ResolveStateMachineMethod(mb);
            if (mb.DeclaringType is null) continue;

            if (IgnoreLine(mb)) continue;

            // return type
            if (mb is MethodInfo mi)
            {
                sb.Append(BeautifyType(mi.ReturnType, false));
                sb.Append(" ");
            }

            // method name
            sb.Append(BeautifyType(mb.DeclaringType, false));
            if (!mb.IsConstructor)
            {
                sb.Append(".");
            }

            sb.Append(mb.Name);
            if (mb.IsGenericMethod)
            {
                sb.Append("<");
                sb.Append(string.Join(", ", mb.GetGenericArguments().Select(x => BeautifyType(x, true))));
                sb.Append(">");
            }

            // parameter
            sb.Append("(");
            sb.Append(string.Join(", ",
                mb.GetParameters().Select(p => BeautifyType(p.ParameterType, true) + " " + p.Name)));
            sb.Append(")");

            // file name
            if (displayFilenames && sf.GetILOffset() != -1)
            {
                string? fileName = null;
                try
                {
                    fileName = sf.GetFileName();
                }
                catch (NotSupportedException)
                {
                    displayFilenames = false;
                }
                catch (SecurityException)
                {
                    displayFilenames = false;
                }

                if (fileName != null)
                {
                    sb.Append(' ');
                    sb.AppendFormat(CultureInfo.InvariantCulture, "(at {0})",
                        AppendHyperLink(fileName, sf.GetFileLineNumber().ToString()));
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    static bool IsHidden(MethodBase method) =>
        method.IsDefined(typeof(StackTraceHiddenAttribute), false) ||
        (method.DeclaringType?.IsDefined(typeof(StackTraceHiddenAttribute), false) ?? false);

    /// <summary>
    /// Maps the <c>MoveNext</c> of a compiler-generated async or iterator state machine onto the method it was
    /// generated for, which carries a <see cref="StateMachineAttribute" /> naming the state machine type.
    /// </summary>
    static MethodBase ResolveStateMachineMethod(MethodBase method)
    {
        var stateMachine = method.DeclaringType;
        var owner = stateMachine?.DeclaringType;
        if (stateMachine is null || owner is null || method.Name != "MoveNext" ||
            !stateMachine.IsDefined(typeof(CompilerGeneratedAttribute), false))
        {
            return method;
        }

        // The attribute names the generic definition of a state machine that belongs to a generic method or type.
        var definition = stateMachine.IsGenericType ? stateMachine.GetGenericTypeDefinition() : stateMachine;
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                                 BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var candidate in owner.GetMethods(all))
        {
            foreach (var attribute in candidate.GetCustomAttributes<StateMachineAttribute>(false))
            {
                if (attribute.StateMachineType == stateMachine || attribute.StateMachineType == definition)
                {
                    return candidate;
                }
            }
        }

        return method;
    }

    static string BeautifyType(Type t, bool shortName)
    {
        if (builtInTypeNames.TryGetValue(t, out var builtin))
        {
            return builtin;
        }

        if (t.IsGenericParameter) return t.Name;
        if (t.IsArray) return BeautifyType(t.GetElementType()!, shortName) + "[]";
        if (t.FullName?.StartsWith("System.ValueTuple") ?? false)
        {
            return "(" + string.Join(", ", t.GetGenericArguments().Select(x => BeautifyType(x, true))) + ")";
        }

        if (!t.IsGenericType)
        {
            return shortName ? t.Name : t.FullName ?? t.Name;
        }

        var innerFormat = string.Join(", ", t.GetGenericArguments().Select(x => BeautifyType(x, true)));

        var genericType = t.GetGenericTypeDefinition().FullName;
        if (genericType is null)
        {
            return typeBeautifyRegex.Replace(shortName ? t.Name : t.FullName ?? t.Name, "") + "<" + innerFormat +
                   ">";
        }

        if (genericType == "System.Threading.Tasks.Task`1")
        {
            genericType = "Task";
        }

        return typeBeautifyRegex.Replace(genericType, "").Replace("Cysharp.Threading.Tasks.Triggers.", "")
                   .Replace("Cysharp.Threading.Tasks.Internal.", "").Replace("Cysharp.Threading.Tasks.", "") + "<" +
               innerFormat + ">";
    }

    static bool IgnoreLine(MethodBase methodInfo)
    {
        var declareType = methodInfo.DeclaringType?.FullName;
        if (declareType is null)
        {
            return false;
        }

        if (declareType.StartsWith("ZLogger"))
        {
            return true;
        }

        if (declareType.StartsWith("Microsoft.Extensions.Logging"))
        {
            return true;
        }

        return false;
    }

    static string AppendHyperLink(string path, string line)
    {
        var fi = new FileInfo(path);
        if (fi.Directory == null)
        {
            return fi.Name;
        }

        var root = applicationDataPath ??= ProjectSettings.GlobalizePath("res://");
        var href = ToResPath(fi.FullName, root);
        return $"{href}:{line}";
        // return "<a href=\"" + href + "\" line=\"" + line + "\">" + href + ":" + line + "</a>";
    }

    /// <summary>
    /// Maps a source file inside the project onto its <c>res://</c> path. Files elsewhere keep their full path, with
    /// forward slashes.
    /// </summary>
    internal static string ToResPath(string fullPath, string projectRoot)
    {
        var path = fullPath.Replace(Path.DirectorySeparatorChar, '/');

        // GlobalizePath("res://") does not resolve to a directory in an exported game, where the result can be empty.
        // string.Replace throws on an empty search string, which would fail the log statement that is being written.
        if (string.IsNullOrEmpty(projectRoot))
        {
            return path;
        }

        if (!projectRoot.EndsWith('/'))
        {
            projectRoot += "/";
        }

        // Only a leading match is the project; the drive letter's case can differ between PDB paths and Godot's.
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.StartsWith(projectRoot, comparison)
            ? string.Concat("res://", path.AsSpan(projectRoot.Length))
            : path;
    }
}