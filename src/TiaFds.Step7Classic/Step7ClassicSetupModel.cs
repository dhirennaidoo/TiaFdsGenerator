using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TiaFds.Core;

namespace TiaFds.Step7Classic
{
    // Models the STEP 7 Classic library's drive setup routine FnBlk.Setup.Drv, which Global
    // Setup calls on the first scan only. Its loops use indirect addressing and cannot be
    // evaluated generically, so the routine is recognised by name and its effect is applied
    // as the library rule CLASSIC_SETUP_DRV_STARTUP, verified against the A-Mill FC51 code:
    //
    //   zero db.cm.Drv; for i in 0 .. NoDrives-1:
    //       db.cm.Drv[i].ILK := TRUE; db.cm.Drv[i].CR := TRUE      (L W#16#50; OW -> bits 0.4, 0.6)
    //       db.cm.Drv[i].DrvType := db.Setup.Drv[i].DrvType        (copy by index, 50 / 20 bytes)
    //
    // The pairing is by index, not by name, so a setup entry named differently from its drive
    // is reported; drives at or beyond NoDrives receive no setup and are reported too.
    internal static class Step7ClassicSetupModel
    {
        public const string Rule = "CLASSIC_SETUP_DRV_STARTUP";
        public const string SetupFunctionName = "FnBlk.Setup.Drv";
        public const string DriveContainer = "db.cm.Drv";
        public const string SetupContainer = "db.Setup.Drv";
        private const string DefaultMask = "W#16#50";

        public static void Apply(
            Step7ProgramModel program,
            Step7SymbolLookup symbols,
            IReadOnlyList<DataBlockStructureInfo> structures,
            IReadOnlyList<BlockCallInfo> calls,
            PlcInventoryBuilder builder)
        {
            Step7CodeBlockModel setup = program.CodeBlocks.FirstOrDefault(block =>
                block.DecodeError == null &&
                string.Equals(symbols.NameOf(Step7BlockCallMapper.Prefix(block.Kind), block.Number),
                    SetupFunctionName, StringComparison.OrdinalIgnoreCase));
            if (setup == null) return;
            if (!calls.Any(call => string.Equals(call.CalledBlockName, SetupFunctionName, StringComparison.OrdinalIgnoreCase)))
                return;

            DataBlockStructureInfo drives = structures.FirstOrDefault(item =>
                string.Equals(item.BlockName, DriveContainer, StringComparison.OrdinalIgnoreCase));
            DataBlockStructureInfo setups = structures.FirstOrDefault(item =>
                string.Equals(item.BlockName, SetupContainer, StringComparison.OrdinalIgnoreCase));
            string source = PlcInventoryBuilder.BuildGroupPath(Step7InventoryMapper.BlocksGroupPath, SetupFunctionName);
            int? driveCount = DriveCount(setup);
            if (drives == null || setups == null || !driveCount.HasValue)
            {
                builder.AddDiagnostic(new InventoryDiagnostic("Warning", Step7DiagnosticCodes.SetupRoutineNotRecognised, source,
                    "FnBlk.Setup.Drv is called but its drive count, db.cm.Drv or db.Setup.Drv could not be identified; " +
                    "drive startup values and DrvType are not modelled."));
                return;
            }

            bool setsDefaults = setup.Networks.SelectMany(network => network.Statements).Any(statement =>
                string.Equals(statement.Command, "L", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Compact(statement.Operand), DefaultMask, StringComparison.OrdinalIgnoreCase));

            List<DataBlockMemberInfo> driveEntries = drives.Members.OrderBy(member => AddressKey(member.Address)).ToList();
            List<DataBlockMemberInfo> setupEntries = setups.Members.OrderBy(member => AddressKey(member.Address)).ToList();
            for (var index = 0; index < driveEntries.Count; index++)
            {
                DataBlockMemberInfo drive = driveEntries[index];
                if (index >= driveCount.Value || index >= setupEntries.Count)
                {
                    builder.AddDiagnostic(new InventoryDiagnostic("Warning", Step7DiagnosticCodes.DriveNotInitialisedBySetup,
                        drive.MemberPath,
                        "Drive index " + index.ToString(CultureInfo.InvariantCulture) + " is beyond the " +
                        driveCount.Value.ToString(CultureInfo.InvariantCulture) + " drives FnBlk.Setup.Drv initialises; " +
                        "it receives no DrvType, presets, or ILK/CR startup values (zeroed at startup)."));
                    continue;
                }

                DataBlockMemberInfo entry = setupEntries[index];
                string evidence = SetupContainer + "." + entry.Name + " (index " + index.ToString(CultureInfo.InvariantCulture) + ")";
                if (!string.Equals(entry.Name, drive.Name, StringComparison.OrdinalIgnoreCase))
                    builder.AddDiagnostic(new InventoryDiagnostic("Warning", Step7DiagnosticCodes.SetupEntryNameMismatch,
                        drive.MemberPath,
                        "FnBlk.Setup.Drv pairs drives with setup entries by index: " + drive.MemberPath +
                        " receives the setup of " + SetupContainer + "." + entry.Name + "."));

                DataBlockMemberInfo driveType = entry.Children.FirstOrDefault(child =>
                    string.Equals(child.Name, "DrvType", StringComparison.OrdinalIgnoreCase));
                if (driveType != null && driveType.ActualValue != null &&
                    drive.Children.Any(child => string.Equals(child.Name, "DrvType", StringComparison.OrdinalIgnoreCase)))
                    builder.AddStartupValue(new StartupValueInfo(drive.MemberPath + ".DrvType", driveType.ActualValue,
                        Rule, SetupFunctionName, evidence));

                if (!setsDefaults) continue;
                foreach (string member in new[] { "ILK", "CR" })
                    if (drive.Children.Any(child => string.Equals(child.Name, member, StringComparison.OrdinalIgnoreCase)))
                        builder.AddStartupValue(new StartupValueInfo(drive.MemberPath + "." + member, "TRUE",
                            Rule, SetupFunctionName, "FnBlk.Setup.Drv sets ILK and CR ON (" + DefaultMask + ") for " + evidence));
            }
        }

        // "L 108; T #NoDrives" (or the temporary's local-word address when not symbolised).
        private static int? DriveCount(Step7CodeBlockModel setup)
        {
            Step7InterfaceParameterModel temporary = setup.InterfaceParameters.FirstOrDefault(parameter =>
                string.Equals(parameter.Section, "TEMP", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(parameter.Name, "NoDrives", StringComparison.OrdinalIgnoreCase));
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "#NoDrives" };
            if (temporary != null && temporary.ByteAddress.HasValue)
                targets.Add("LW" + temporary.ByteAddress.Value.ToString(CultureInfo.InvariantCulture));

            foreach (Step7NetworkModel network in setup.Networks)
                for (var index = 1; index < network.Statements.Count; index++)
                {
                    Step7StatementModel load = network.Statements[index - 1];
                    Step7StatementModel transfer = network.Statements[index];
                    int value;
                    if (string.Equals(load.Command, "L", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(transfer.Command, "T", StringComparison.OrdinalIgnoreCase) &&
                        targets.Contains(Compact(transfer.Operand)) &&
                        int.TryParse(Compact(load.Operand), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                        return value;
                }
            return null;
        }

        private static int AddressKey(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return int.MaxValue;
            string[] parts = address.Split('.');
            int byteAddress, bitAddress;
            return int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byteAddress) &&
                   parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out bitAddress)
                ? byteAddress * 8 + bitAddress
                : int.MaxValue;
        }

        private static string Compact(string text)
        {
            return (text ?? string.Empty).Replace(" ", string.Empty).Trim();
        }
    }
}
