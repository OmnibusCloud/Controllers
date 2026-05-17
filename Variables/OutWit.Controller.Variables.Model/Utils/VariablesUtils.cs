using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OutWit.Controller.Variables.Model.Utils
{
    public static class VariablesUtils
    {
        public static bool TryHexToBytes(string? hex, out byte[]? bytes)
        {
            bytes = null;

            if (string.IsNullOrWhiteSpace(hex))
                return false;

            if (hex.StartsWith("#"))
                hex = hex.Substring(1);

            if (hex.Length % 2 != 0)
                return false;

            int byteCount = hex.Length / 2;
            var result = new byte[byteCount];

            for (int i = 0; i < byteCount; i++)
            {
                if (!byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result[i]))
                    return false;
            }

            bytes = result;
            return true;
        }
    }
}
