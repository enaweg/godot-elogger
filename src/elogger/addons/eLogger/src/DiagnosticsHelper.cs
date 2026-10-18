using System.Diagnostics;
using System.Linq;
using System.Reflection;
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

    public static string CleanupStackTrace(StackTrace stackTrace)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < stackTrace.FrameCount; i++)
        {
            var sf = stackTrace.GetFrame(i);

            // Frames without resolvable metadata (native, inlined or trimmed) have no method or no declaring
            // type. They carry nothing worth printing and must not throw - this runs inside the log pipeline.
            if (sf is null) continue;

            var mb = sf.GetMethod();
            if (mb?.DeclaringType is null) continue;

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
        var href = fi.FullName.Replace(Path.DirectorySeparatorChar, '/').Replace(root, "res://");
        return $"{href}:{line}";
        // return "<a href=\"" + href + "\" line=\"" + line + "\">" + href + ":" + line + "</a>";
    }
}