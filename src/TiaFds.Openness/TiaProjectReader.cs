using System;
using System.Collections.Generic;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
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
            var plcs = new List<PlcInfo>();
            var plcKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hardwareDevices = new List<HardwareDeviceInfo>();

            foreach (Device device in project.Devices)
            {
                deviceNames.Add(device.Name);
                var items = new List<HardwareItemInfo>();
                foreach (DeviceItem deviceItem in device.DeviceItems)
                {
                    items.Add(CreateHardwareItem(device, deviceItem, plcs, plcKeys));
                }

                hardwareDevices.Add(new HardwareDeviceInfo(device.Name, items));
            }

            return new TiaProjectSummary(
                project.Name,
                project.Path.FullName,
                deviceNames,
                plcs,
                hardwareDevices);
        }

        private static HardwareItemInfo CreateHardwareItem(
            Device device,
            DeviceItem deviceItem,
            ICollection<PlcInfo> plcs,
            ISet<string> plcKeys)
        {
            HardwareSoftwareInfo softwareInfo = null;
            var serviceProvider = (IEngineeringServiceProvider)deviceItem;
            SoftwareContainer softwareContainer = serviceProvider.GetService<SoftwareContainer>();
            if (softwareContainer != null && softwareContainer.Software != null)
            {
                Software software = softwareContainer.Software;
                var plcSoftware = software as PlcSoftware;
                softwareInfo = new HardwareSoftwareInfo(
                    software.Name,
                    plcSoftware == null ? software.GetType().Name : "PlcSoftware");

                if (plcSoftware != null)
                {
                    string key = string.Join(
                        "\u001f",
                        device.Name,
                        deviceItem.Name,
                        plcSoftware.Name);

                    if (plcKeys.Add(key))
                    {
                        plcs.Add(new PlcInfo(
                            plcSoftware.Name,
                            device.Name,
                            deviceItem.Name));
                    }
                }
            }

            var childItems = new List<HardwareItemInfo>();
            foreach (DeviceItem childItem in deviceItem.DeviceItems)
            {
                childItems.Add(CreateHardwareItem(device, childItem, plcs, plcKeys));
            }

            return new HardwareItemInfo(deviceItem.Name, softwareInfo, childItems);
        }
    }
}
