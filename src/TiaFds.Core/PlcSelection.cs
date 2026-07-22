using System;
using System.Collections.Generic;

namespace TiaFds.Core
{
    public static class PlcSelection
    {
        public static PlcInfo FindByName(IReadOnlyList<PlcInfo> plcs, string name)
        {
            if (plcs == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            foreach (PlcInfo plc in plcs)
            {
                if (string.Equals(plc.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return plc;
                }
            }

            return null;
        }
    }
}
