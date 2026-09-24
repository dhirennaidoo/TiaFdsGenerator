using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using TiaFds.Core;

namespace TiaFds.Step7Classic
{
    // Resolves absolute DB addresses to symbolic member paths using the decoded DB layouts.
    // Several members can share one address (a UDT and its first BOOL both start at 400.0),
    // so the expected datatype decides; anything else is reported, never guessed.
    public sealed class Step7AddressResolver
    {
        private static readonly Regex DbBitAddress = new Regex(
            @"^\s*(?<pointer>P#)?\s*DB\s*(?<db>\d+)\s*\.\s*DBX\s*(?<byte>\d+)\s*\.\s*(?<bit>[0-7])\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex DbValueAddress = new Regex(
            @"^\s*DB\s*(?<db>\d+)\s*\.\s*DB(?<width>[BWD])\s*(?<byte>\d+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private readonly Dictionary<int, string> blockNamesByNumber = new Dictionary<int, string>();

        private readonly Dictionary<int, List<DataBlockMemberInfo>> membersByBlock =
            new Dictionary<int, List<DataBlockMemberInfo>>();
        private readonly HashSet<string> knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> blockNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public Step7AddressResolver(IEnumerable<DataBlockStructureInfo> structures)
        {
            foreach (DataBlockStructureInfo structure in structures ?? new DataBlockStructureInfo[0])
            {
                if (!structure.BlockNumber.HasValue) continue;
                blockNames.Add(structure.BlockName);
                blockNamesByNumber[structure.BlockNumber.Value] = structure.BlockName;
                var members = new List<DataBlockMemberInfo>();
                foreach (DataBlockMemberInfo member in structure.Members) Flatten(member, members);
                membersByBlock[structure.BlockNumber.Value] = members;
                foreach (DataBlockMemberInfo member in members) knownPaths.Add(member.MemberPath);
            }
        }

        public bool IsKnownPath(string memberPath)
        {
            return !string.IsNullOrWhiteSpace(memberPath) && knownPaths.Contains(memberPath);
        }

        // True when the path's container is an extracted DB, e.g. "db.cm.Drv.X" for db.cm.Drv.
        public bool IsKnownBlock(string memberPath)
        {
            return BlockNameOf(memberPath) != null;
        }

        // The extracted DB whose name prefixes the path (longest match, since DB symbols
        // contain dots: "db.cm.Drv.AM_M1035" -> "db.cm.Drv").
        public string BlockNameOf(string memberPath)
        {
            if (string.IsNullOrWhiteSpace(memberPath)) return null;
            string best = null;
            foreach (string name in blockNames)
                if (memberPath.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase) &&
                    (best == null || name.Length > best.Length))
                    best = name;
            return best;
        }

        // True when the text is an absolute DB bit address or pointer (DB50.DBX 400.0,
        // P#DB50.DBX 400.0), whether or not it can be resolved.
        public static bool IsDbBitAddress(string text)
        {
            return text != null && DbBitAddress.IsMatch(text);
        }

        // Bit, byte, word or dword DB access (DB50.DBX 400.6, DB3.DBW 10, P#DB50.DBX 400.0).
        public static bool IsDbAddress(string text)
        {
            return text != null && (DbBitAddress.IsMatch(text) || DbValueAddress.IsMatch(text));
        }

        public Step7AddressResolution Resolve(string text, string expectedDataType)
        {
            Match match = text == null ? Match.Empty : DbBitAddress.Match(text);
            bool valueAccess = false;
            if (!match.Success)
            {
                match = text == null ? Match.Empty : DbValueAddress.Match(text);
                if (!match.Success) return Step7AddressResolution.NotAnAddress;
                valueAccess = true;
            }

            int dbNumber = int.Parse(match.Groups["db"].Value, CultureInfo.InvariantCulture);
            string address = int.Parse(match.Groups["byte"].Value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture) + "." + (valueAccess ? "0" : match.Groups["bit"].Value);
            string location = "DB" + dbNumber.ToString(CultureInfo.InvariantCulture) +
                (valueAccess ? ".DB" + match.Groups["width"].Value.ToUpperInvariant() + " " + match.Groups["byte"].Value
                             : ".DBX " + address);

            List<DataBlockMemberInfo> members;
            if (!membersByBlock.TryGetValue(dbNumber, out members))
                return Step7AddressResolution.Failed(location + " is not in an extracted global DB structure.");
            string blockName = blockNamesByNumber[dbNumber];

            List<DataBlockMemberInfo> atAddress = members
                .Where(member => string.Equals(member.Address, address, StringComparison.Ordinal))
                .ToList();
            if (atAddress.Count == 0)
                return Step7AddressResolution.Failed("No member starts at " + location + ".");

            List<DataBlockMemberInfo> candidates;
            if (valueAccess && string.IsNullOrWhiteSpace(expectedDataType))
                // A byte/word/dword load targets an elementary, non-BOOL member.
                candidates = atAddress.Where(member => member.Children.Count == 0 &&
                    !string.Equals(member.DataTypeName, "BOOL", StringComparison.OrdinalIgnoreCase)).ToList();
            else
                candidates = string.IsNullOrWhiteSpace(expectedDataType)
                    ? atAddress
                    : atAddress.Where(member => string.Equals(member.DataTypeName, expectedDataType,
                        StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 1)
                return Step7AddressResolution.Resolved(candidates[0].MemberPath, blockName);
            if (candidates.Count == 0)
                return Step7AddressResolution.Failed("No member of type '" + expectedDataType + "' starts at " +
                    location + " (found: " + string.Join(", ", atAddress.Select(Describe)) + ").");
            return Step7AddressResolution.Failed("More than one member starts at " + location + ": " +
                string.Join(", ", candidates.Select(Describe)) + ".");
        }

        private static string Describe(DataBlockMemberInfo member)
        {
            return member.MemberPath + " : " + member.DataTypeName;
        }

        private static void Flatten(DataBlockMemberInfo member, ICollection<DataBlockMemberInfo> target)
        {
            if (member == null) return;
            target.Add(member);
            foreach (DataBlockMemberInfo child in member.Children) Flatten(child, target);
        }
    }

    public sealed class Step7AddressResolution
    {
        public static readonly Step7AddressResolution NotAnAddress = new Step7AddressResolution(false, null, null, null);

        private Step7AddressResolution(bool isAddress, string memberPath, string blockName, string failureReason)
        {
            IsAddress = isAddress;
            MemberPath = memberPath;
            BlockName = blockName;
            FailureReason = failureReason;
        }

        public bool IsAddress { get; }
        public string MemberPath { get; }
        // Symbolic name of the DB that owns MemberPath, e.g. "db.cm.Drv".
        public string BlockName { get; }
        public string FailureReason { get; }
        public bool IsResolved => MemberPath != null;

        public static Step7AddressResolution Resolved(string memberPath, string blockName)
        {
            return new Step7AddressResolution(true, memberPath, blockName, null);
        }

        public static Step7AddressResolution Failed(string reason)
        {
            return new Step7AddressResolution(true, null, null, reason);
        }
    }
}
