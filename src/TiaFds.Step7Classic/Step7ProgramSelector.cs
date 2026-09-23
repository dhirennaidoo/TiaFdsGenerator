using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaFds.Step7Classic
{
    // STEP 7 V5 program folders are frequently all named "S7 Program", so the --plc value may
    // name the program, its CPU, or the full "Station/CPU/Program" path. Ambiguity is an error.
    public static class Step7ProgramSelector
    {
        public static Step7ProgramIdentity Select(IReadOnlyList<Step7ProgramIdentity> programs, string name)
        {
            if (programs == null) throw new ArgumentNullException(nameof(programs));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A program, CPU, or Station/CPU/Program name is required.", nameof(name));

            string wanted = name.Trim();
            foreach (Func<Step7ProgramIdentity, string> key in new Func<Step7ProgramIdentity, string>[]
                     {
                         program => program.Path,
                         program => program.Name,
                         program => program.CpuName
                     })
            {
                List<Step7ProgramIdentity> matches = programs
                    .Where(program => string.Equals(key(program), wanted, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count == 1) return matches[0];
                if (matches.Count > 1)
                    throw new Step7ProgramSelectionException(
                        "'" + wanted + "' matches more than one S7 program. Use one of: " + Describe(matches));
            }

            return null;
        }

        public static string Describe(IEnumerable<Step7ProgramIdentity> programs)
        {
            List<string> paths = programs.Select(program => program.Path).ToList();
            return paths.Count == 0 ? "None" : string.Join(", ", paths);
        }
    }

    public sealed class Step7ProgramSelectionException : Exception
    {
        public Step7ProgramSelectionException(string message) : base(message) { }
    }
}
