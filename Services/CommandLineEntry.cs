#nullable enable
using System;

namespace KillerFamily.CommandLine
{
    /// <summary>Recognizes app commands without taking filenames or normal launches away from the UI.</summary>
    public static class CommandLineEntry
    {
        public static string[]? GetArguments(string[] arguments, string[] commands, string commandPrefix = "")
        {
            if (arguments.Length == 0) return null;
            if (arguments[0].Equals("--cli", StringComparison.OrdinalIgnoreCase))
            {
                var legacy = new string[arguments.Length - 1];
                Array.Copy(arguments, 1, legacy, 0, legacy.Length);
                return legacy;
            }
            string first = arguments[0].ToLowerInvariant();
            if (first == "help" || first == "--help" || first == "-h" || first == "/?")
            {
                var help = (string[])arguments.Clone();
                help[0] = "--help";
                return help;
            }
            foreach (string command in commands)
                if (first.Equals(command, StringComparison.OrdinalIgnoreCase))
                {
                    var routed = (string[])arguments.Clone();
                    routed[0] = commandPrefix + command;
                    return routed;
                }
            return null;
        }
    }
}
