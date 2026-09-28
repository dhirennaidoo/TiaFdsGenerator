using Newtonsoft.Json;

namespace TiaFds.Core
{
    // A value a member is known to hold from CPU start, established by a recognised library
    // initialisation routine rather than by cyclic logic (e.g. STEP 7 Classic FnBlk.Setup.Drv
    // setting every drive's ILK and CR TRUE and copying its DrvType from db.Setup.Drv).
    // Cyclic assignments to the same member supersede it.
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class StartupValueInfo
    {
        [JsonConstructor]
        public StartupValueInfo(string memberPath, string value, string rule, string sourceBlockName, string evidence)
        {
            MemberPath = memberPath ?? string.Empty;
            Value = value ?? string.Empty;
            Rule = rule;
            SourceBlockName = sourceBlockName;
            Evidence = evidence;
        }

        [JsonProperty("memberPath")]
        public string MemberPath { get; }

        // Invariant text: TRUE/FALSE for BOOL, digits for integers.
        [JsonProperty("value")]
        public string Value { get; }

        [JsonProperty("rule")]
        public string Rule { get; }

        [JsonProperty("sourceBlockName")]
        public string SourceBlockName { get; }

        [JsonProperty("evidence")]
        public string Evidence { get; }

        public bool? BooleanValue
        {
            get
            {
                if (string.Equals(Value, "TRUE", System.StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(Value, "FALSE", System.StringComparison.OrdinalIgnoreCase)) return false;
                return null;
            }
        }

        public int? IntegerValue
        {
            get
            {
                int value;
                return int.TryParse(Value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out value) ? value : (int?)null;
            }
        }
    }
}
