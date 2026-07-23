using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace TiaFds.Core
{
    internal static class SnapshotJsonSettings
    {
        public static JsonSerializerSettings Create()
        {
            return new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Culture = System.Globalization.CultureInfo.InvariantCulture,
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                Formatting = Formatting.Indented,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore
            };
        }
    }
}
