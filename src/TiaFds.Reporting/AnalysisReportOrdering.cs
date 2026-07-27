using System;
using System.Collections.Generic;

namespace TiaFds.Reporting
{
    internal static class AnalysisReportOrdering
    {
        private static readonly string[] Families =
        {
            "AnalogueInput",
            "AnalogueOutput",
            "DigitalInput",
            "DigitalOutput",
            "Drive",
            "Speed",
            "Valve"
        };

        internal static IEnumerable<string> KnownFamilies
        {
            get { return Families; }
        }

        internal static int CompareFamily(string left, string right)
        {
            int leftRank = FamilyRank(left);
            int rightRank = FamilyRank(right);
            int result = leftRank.CompareTo(rightRank);
            return result != 0 ? result : CompareText(left, right);
        }

        internal static int CompareSeverity(string left, string right)
        {
            int result = SeverityRank(left).CompareTo(SeverityRank(right));
            return result != 0 ? result : CompareText(left, right);
        }

        internal static int CompareText(string left, string right)
        {
            int result = StringComparer.OrdinalIgnoreCase.Compare(
                left ?? string.Empty, right ?? string.Empty);
            return result != 0
                ? result
                : StringComparer.Ordinal.Compare(left ?? string.Empty, right ?? string.Empty);
        }

        internal static int CompareNullable(int? left, int? right)
        {
            if (left.HasValue && right.HasValue) return left.Value.CompareTo(right.Value);
            if (left.HasValue) return -1;
            return right.HasValue ? 1 : 0;
        }

        private static int FamilyRank(string family)
        {
            for (var index = 0; index < Families.Length; index++)
                if (string.Equals(Families[index], family, StringComparison.OrdinalIgnoreCase))
                    return index;
            return Families.Length;
        }

        private static int SeverityRank(string severity)
        {
            if (string.Equals(severity, "Error", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(severity, "Warning", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(severity, "Information", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(severity, "Info", StringComparison.OrdinalIgnoreCase)) return 2;
            return 3;
        }
    }
}
