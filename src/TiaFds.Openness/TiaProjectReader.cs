using System;
using System.Collections.Generic;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaFds.Core;

namespace TiaFds.Openness
{
    public sealed class TiaProjectReader
    {
        public TiaProjectSummary Read(string inputPath, string retrieveTo)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                throw new ArgumentException("An input path is required.", nameof(inputPath));
            }

            var input = new FileInfo(Path.GetFullPath(inputPath));
            if (!input.Exists)
            {
                throw new FileNotFoundException("The TIA Portal project or archive was not found.", input.FullName);
            }

            using (var tiaPortal = new TiaPortal(TiaPortalMode.WithoutUserInterface))
            {
                Project project = null;
                try
                {
                    project = OpenOrRetrieve(tiaPortal, input, retrieveTo);
                    return CreateSummary(project);
                }
                finally
                {
                    if (project != null)
                    {
                        project.Close();
                    }
                }
            }
        }

        private static Project OpenOrRetrieve(TiaPortal tiaPortal, FileInfo input, string retrieveTo)
        {
            if (string.Equals(input.Extension, ".ap15_1", StringComparison.OrdinalIgnoreCase))
            {
                return tiaPortal.Projects.Open(input);
            }

            if (string.Equals(input.Extension, ".zap15_1", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(retrieveTo))
                {
                    throw new ArgumentException("--retrieve-to is required for a .zap15_1 archive.", nameof(retrieveTo));
                }

                var destination = new DirectoryInfo(Path.GetFullPath(retrieveTo));
                if (!destination.Exists)
                {
                    destination.Create();
                }

                return tiaPortal.Projects.Retrieve(input, destination);
            }

            throw new NotSupportedException("Input must have the .ap15_1 or .zap15_1 extension.");
        }

        private static TiaProjectSummary CreateSummary(Project project)
        {
            var deviceNames = new List<string>();
            foreach (Device device in project.Devices)
            {
                deviceNames.Add(device.Name);
            }

            return new TiaProjectSummary(project.Name, project.Path.FullName, deviceNames);
        }
    }
}
