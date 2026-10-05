using System.Collections.Generic;
using Newtonsoft.Json;

namespace TiaFds.Core
{
    // How an actual parameter was obtained. Null for actuals that were read as written
    // (TIA wires, P# pointers, symbols); STEP 7 Classic local-data actuals are traced.
    public static class CallParameterTraceStatus
    {
        // A local-data actual (L 0.0, LW1) was replaced by the operand or expression wired to it.
        public const string Traced = "Traced";
        // A local-data actual could not be traced; the raw address is kept and diagnosed.
        public const string NotTraced = "NotTraced";
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class CallParameterInfo
    {
        public CallParameterInfo(
            string formalName,
            string direction,
            string formalDataType,
            string actualExpression,
            string resolvedMemberPath)
            : this(formalName, direction, formalDataType, actualExpression, resolvedMemberPath, null, null, null)
        {
        }

        [JsonConstructor]
        public CallParameterInfo(
            string formalName,
            string direction,
            string formalDataType,
            string actualExpression,
            string resolvedMemberPath,
            string rawActualExpression,
            ExtractedBooleanExpression actualExpressionTree,
            string traceStatus)
        {
            RawActualExpression = string.IsNullOrWhiteSpace(rawActualExpression) ? null : rawActualExpression;
            ActualExpressionTree = actualExpressionTree;
            TraceStatus = string.IsNullOrWhiteSpace(traceStatus) ? null : traceStatus;
            FormalName = formalName;
            Direction = direction;
            FormalDataType = formalDataType;
            ActualExpression = actualExpression;
            ResolvedMemberPath = resolvedMemberPath;
        }

        [JsonProperty("formalName")]
        public string FormalName { get; }

        [JsonProperty("direction")]
        public string Direction { get; }

        [JsonProperty("formalDataType")]
        public string FormalDataType { get; }

        [JsonProperty("actualExpression")]
        public string ActualExpression { get; }

        [JsonProperty("resolvedMemberPath")]
        public string ResolvedMemberPath { get; }

        // The actual as decompiled when it differs from actualExpression (e.g. "L 0.0").
        [JsonProperty("rawActualExpression", NullValueHandling = NullValueHandling.Ignore)]
        public string RawActualExpression { get; }

        // Boolean actuals traced through local data: the wired expression, so constants
        // (glb.One) and bypass flags can be classified.
        [JsonProperty("actualExpressionTree", NullValueHandling = NullValueHandling.Ignore)]
        public ExtractedBooleanExpression ActualExpressionTree { get; }

        // CallParameterTraceStatus value, or null when the actual was read as written.
        [JsonProperty("traceStatus", NullValueHandling = NullValueHandling.Ignore)]
        public string TraceStatus { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class BlockCallInfo
    {
        [JsonConstructor]
        public BlockCallInfo(
            string callingBlockName,
            int? callingBlockNumber,
            string callingBlockType,
            string callingBlockPath,
            string calledBlockName,
            int? calledBlockNumber,
            string calledBlockType,
            int? networkNumber,
            string networkTitle,
            int callOrdinal,
            IReadOnlyList<CallParameterInfo> parameters,
            IReadOnlyList<InventoryDiagnostic> diagnostics)
        {
            CallingBlockName = callingBlockName;
            CallingBlockNumber = callingBlockNumber;
            CallingBlockType = callingBlockType;
            CallingBlockPath = callingBlockPath;
            CalledBlockName = calledBlockName;
            CalledBlockNumber = calledBlockNumber;
            CalledBlockType = calledBlockType;
            NetworkNumber = networkNumber;
            NetworkTitle = networkTitle;
            CallOrdinal = callOrdinal;
            Parameters = CopyParameters(parameters);
            Diagnostics = CopyDiagnostics(diagnostics);
        }

        [JsonProperty("callingBlockName")]
        public string CallingBlockName { get; }
        [JsonProperty("callingBlockNumber")]
        public int? CallingBlockNumber { get; }
        [JsonProperty("callingBlockType")]
        public string CallingBlockType { get; }
        [JsonProperty("callingBlockPath")]
        public string CallingBlockPath { get; }
        [JsonProperty("calledBlockName")]
        public string CalledBlockName { get; }
        [JsonProperty("calledBlockNumber")]
        public int? CalledBlockNumber { get; }
        [JsonProperty("calledBlockType")]
        public string CalledBlockType { get; }
        [JsonProperty("networkNumber")]
        public int? NetworkNumber { get; }
        [JsonProperty("networkTitle")]
        public string NetworkTitle { get; }
        [JsonProperty("callOrdinal")]
        public int CallOrdinal { get; }
        [JsonProperty("parameters")]
        public IReadOnlyList<CallParameterInfo> Parameters { get; }
        [JsonProperty("diagnostics")]
        public IReadOnlyList<InventoryDiagnostic> Diagnostics { get; }

        private static IReadOnlyList<CallParameterInfo> CopyParameters(IReadOnlyList<CallParameterInfo> source)
        {
            var result = source == null ? new List<CallParameterInfo>() : new List<CallParameterInfo>(source);
            result.Sort((left, right) =>
            {
                int value = System.StringComparer.Ordinal.Compare(left.FormalName ?? string.Empty, right.FormalName ?? string.Empty);
                return value != 0 ? value : System.StringComparer.Ordinal.Compare(
                    left.ActualExpression ?? string.Empty, right.ActualExpression ?? string.Empty);
            });
            return result.ToArray();
        }

        private static IReadOnlyList<InventoryDiagnostic> CopyDiagnostics(IReadOnlyList<InventoryDiagnostic> source)
        {
            var result = source == null ? new List<InventoryDiagnostic>() : new List<InventoryDiagnostic>(source);
            result.Sort((left, right) =>
            {
                int value = System.StringComparer.Ordinal.Compare(left.Code ?? string.Empty, right.Code ?? string.Empty);
                return value != 0 ? value : System.StringComparer.Ordinal.Compare(left.Source ?? string.Empty, right.Source ?? string.Empty);
            });
            return result.ToArray();
        }
    }
}
