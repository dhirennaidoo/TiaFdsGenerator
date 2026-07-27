using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TiaFds.Core
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ExtractedBooleanExpressionKind
    {
        Operand,
        Constant,
        Not,
        And,
        Or,
        Unknown
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ExtractedLogicResolutionStatus
    {
        Complete,
        Partial,
        Unsupported
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ExtractedBooleanExpression
    {
        [JsonConstructor]
        public ExtractedBooleanExpression(
            ExtractedBooleanExpressionKind kind,
            string displayText,
            string resolvedPath,
            bool? constantValue,
            IReadOnlyList<ExtractedBooleanExpression> children)
        {
            Kind = kind;
            DisplayText = displayText;
            ResolvedPath = resolvedPath;
            ConstantValue = constantValue;
            Children = Copy(children);
        }

        [JsonProperty("kind")]
        public ExtractedBooleanExpressionKind Kind { get; }
        [JsonProperty("displayText")]
        public string DisplayText { get; }
        [JsonProperty("resolvedPath")]
        public string ResolvedPath { get; }
        [JsonProperty("constantValue")]
        public bool? ConstantValue { get; }
        [JsonProperty("children")]
        public IReadOnlyList<ExtractedBooleanExpression> Children { get; }

        private static IReadOnlyList<ExtractedBooleanExpression> Copy(
            IReadOnlyList<ExtractedBooleanExpression> source)
        {
            if (source == null || source.Count == 0)
                return new ExtractedBooleanExpression[0];
            var result = new ExtractedBooleanExpression[source.Count];
            for (var index = 0; index < source.Count; index++) result[index] = source[index];
            return result;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ExtractedLogicAssignment
    {
        [JsonConstructor]
        public ExtractedLogicAssignment(
            string destinationExpression,
            string resolvedDestinationPath,
            ExtractedBooleanExpression sourceExpression,
            string originalSourceText,
            string blockName,
            int? blockNumber,
            string blockType,
            string blockLanguage,
            int? networkNumber,
            string networkTitle,
            string networkComment,
            int statementOrder,
            ExtractedLogicResolutionStatus resolutionStatus)
        {
            DestinationExpression = destinationExpression;
            ResolvedDestinationPath = resolvedDestinationPath;
            SourceExpression = sourceExpression;
            OriginalSourceText = originalSourceText;
            BlockName = blockName;
            BlockNumber = blockNumber;
            BlockType = blockType;
            BlockLanguage = blockLanguage;
            NetworkNumber = networkNumber;
            NetworkTitle = networkTitle;
            NetworkComment = networkComment;
            StatementOrder = statementOrder;
            ResolutionStatus = resolutionStatus;
        }

        [JsonProperty("destinationExpression")]
        public string DestinationExpression { get; }
        [JsonProperty("resolvedDestinationPath")]
        public string ResolvedDestinationPath { get; }
        [JsonProperty("sourceExpression")]
        public ExtractedBooleanExpression SourceExpression { get; }
        [JsonProperty("originalSourceText")]
        public string OriginalSourceText { get; }
        [JsonProperty("blockName")]
        public string BlockName { get; }
        [JsonProperty("blockNumber")]
        public int? BlockNumber { get; }
        [JsonProperty("blockType")]
        public string BlockType { get; }
        [JsonProperty("blockLanguage")]
        public string BlockLanguage { get; }
        [JsonProperty("networkNumber")]
        public int? NetworkNumber { get; }
        [JsonProperty("networkTitle")]
        public string NetworkTitle { get; }
        [JsonProperty("networkComment")]
        public string NetworkComment { get; }
        [JsonProperty("statementOrder")]
        public int StatementOrder { get; }
        [JsonProperty("resolutionStatus")]
        public ExtractedLogicResolutionStatus ResolutionStatus { get; }
    }
}
