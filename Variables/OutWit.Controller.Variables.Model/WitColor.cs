using System;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Variables.Model.Utils;

namespace OutWit.Controller.Variables.Model
{
    [MemoryPackable]
    public partial class WitColor : ModelBase
    {
        #region Constructors

        [MemoryPackConstructor]
        public WitColor(byte red, byte green, byte blue, byte alpha = 255)
        {
            Red = red;
            Green = green;
            Blue = blue;
            Alpha = alpha;
        }

        #endregion

        #region Functions

        public override string ToString()
        {
            return $"#{Alpha:X2}{Red:X2}{Green:X2}{Blue:X2}";
        }

        #endregion

        #region Static

        public static WitColor Parse(string s)
        {
            if(!TryParse(s, out var color))
                throw new ArgumentException($"Invalid color format: {s}", nameof(s));
            
            return color!;
        }

        public static bool TryParse(string? s, out WitColor? result)
        {
            result = null;
            
            if(!VariablesUtils.TryHexToBytes(s, out var bytes) || bytes == null)
                return false;

            switch (bytes.Length)
            {
                case 3:
                    result = new WitColor(bytes[0], bytes[1], bytes[2]);
                    return true;
                case 4:
                    result = new WitColor(bytes[1], bytes[2], bytes[3], bytes[0]);
                    return true;
                default:
                    return false;
            }
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
        {
            if (modelBase is not WitColor color)
                return false;

            return Red.Is(color.Red) &&
                   Green.Is(color.Green) &&
                   Blue.Is(color.Blue) &&
                   Alpha.Is(color.Alpha);
        }

        public override WitColor Clone()
        {
            return new WitColor(Red, Green, Blue, Alpha);
        }

        #endregion

        #region Properties

        [MemoryPackOrder(0)]
        public byte Red { get; }
        
        [MemoryPackOrder(1)]
        public byte Green { get; }
        
        [MemoryPackOrder(2)]
        public byte Blue { get; }
        
        [MemoryPackOrder(3)]
        public byte Alpha { get; }

        #endregion
    }
}
