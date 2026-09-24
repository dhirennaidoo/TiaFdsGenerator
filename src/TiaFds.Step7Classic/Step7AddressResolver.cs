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
            if (string.IsNullOrWhiteSpace(memberPath)) return false;
            foreach (string name in blockNames)
                if (memberPath.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // True when the text is an absolute DB bit address or pointer (DB50.DBX 400.0,
        // P#DB50.DBX 400.0), whether or not it can be resolved.
        public static bool IsDbBitAddress(string text)
        {
            return text != null && DbBitAddress.IsMatch(text);
        }

        public Step7AddressResolution Resolve(string text, string expectedDataType)
        {
            Match match = text == null ? Match.Empty : DbBitAddress.Match(text);
            if (!match.Success) return Step7AddressResolution.NotAnAddress;

            int dbNumber = int.Parse(match.Groups["db"].Value, CultureInfo.InvariantCulture);
            string address = int.Parse(match.Groups["byte"].Value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture) + "." + match.Groups["bit"].Value;
            string location = "DB" + dbNumber.ToString(CultureInfo.InvariantCulture) + ".DBX " + address;

            List<DataBlockMemberInfo> members;
            if (!membersByBlock.TryGetValue(dbNumber, out members))
                return Step7AddressResolution.Failed(location + " is not in an extracted global DB structure.");

            List<DataBlockMemberInfo> atAddress = members
                .Where(member => string.Equals(member.Address, address, StringComparison.Ordinal))
                .ToList();
            if (atAddress.Count == 0)
                return Step7AddressResolution.Failed("No member starts at " + location + ".");

            List<DataBlockMemberInfo> candidates = string.IsNullOrWhiteSpace(expectedDataType)
                ? atAddress
                : atAddress.Where(member => string.Equals(member.DataTypeName, expectedDataType,
                    StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 1) return Step7AddressResolution.Resolved(candidates[0].MemberPath);
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
        public static readonly Step7AddressResolution NotAnAddress = new Step7AddressResolution(false, null, null);

        private Step7AddressResolution(bool isAddress, string memberPath, string failureReason)
        {
            IsAddress = isAddress;
            MemberPath = memberPath;
            FailureReason = failureReason;
        }

        public bool IsAddress { get; }
        public string MemberPath { get; }
        public string FailureReason { get; }
        public bool IsResolved => MemberPath != null;

        public static Step7AddressResolution Resolved(string memberPath)
        {
            return new Step7AddressResolution(true, memberPath, null);
        }

        public static Step7AddressResolution Failed(string reason)
        {
            return new Step7AddressResolution(true, null, reason);
        }
    }
}
