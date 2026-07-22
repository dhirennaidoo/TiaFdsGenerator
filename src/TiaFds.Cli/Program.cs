using System;
using TiaFds.Core;
using TiaFds.Openness;

namespace TiaFds.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                string input;
                string retrieveTo;
                ParseArguments(args, out input, out retrieveTo);

                TiaProjectSummary summary = new TiaProjectReader().Read(input, retrieveTo);
                Console.WriteLine("Project name: {0}", summary.Name);
                Console.WriteLine("Project path: {0}", summary.Path);
                Console.WriteLine("Top-level devices:");
                foreach (string deviceName in summary.DeviceNames)
                {
                    Console.WriteLine("- {0}", deviceName);
                }

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Error: {0}", exception.Message);
                return 1;
            }
        }

        private static void ParseArguments(string[] args, out string input, out string retrieveTo)
        {
            input = null;
            retrieveTo = null;

            for (var index = 0; index < args.Length; index++)
            {
                string option = args[index];
                if (option != "--input" && option != "--retrieve-to")
                {
                    throw new ArgumentException("Unknown argument: " + option + Environment.NewLine + Usage());
                }

                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                {
                    throw new ArgumentException("Missing value for " + option + "." + Environment.NewLine + Usage());
                }

                if (option == "--input")
                {
                    if (input != null)
                    {
                        throw new ArgumentException("--input may only be specified once." + Environment.NewLine + Usage());
                    }

                    input = args[index];
                }
                else
                {
                    if (retrieveTo != null)
                    {
                        throw new ArgumentException("--retrieve-to may only be specified once." + Environment.NewLine + Usage());
                    }

                    retrieveTo = args[index];
                }
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException("--input is required." + Environment.NewLine + Usage());
            }
        }

        private static string Usage()
        {
            return "Usage: TiaFds.Cli.exe --input <path> [--retrieve-to <folder>]";
        }
    }
}
