using OutWit.Controller.Variables.Adapters;
using OutWit.Controller.Variables.Properties;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.Variables.Activities;
using OutWit.Controller.Variables.Collections;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Variables
{
    [WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
    public class WitControllerVariablesModule : WitPluginBase, IWitControllerNode, IWitControllerHost
    {
        public override void Initialize(IServiceCollection services)
        {

            services.AddVariable<WitVariableArray>();
            services.AddVariable<WitVariableBlob>();
            services.AddVariable<WitVariableBoolean>();
            services.AddVariable<WitVariableByte>();
            services.AddVariable<WitVariableColor>();
            services.AddVariable<WitVariableDateTime>();
            services.AddVariable<WitVariableDateTimeOffset>();
            services.AddVariable<WitVariableDecimal>();
            services.AddVariable<WitVariableDouble>();
            services.AddVariable<WitVariableFloat>();
            services.AddVariable<WitVariableGuid>();
            services.AddVariable<WitVariableInteger>();
            services.AddVariable<WitVariableLong>();
            services.AddVariable<WitVariableSByte>();
            services.AddVariable<WitVariableShort>();
            services.AddVariable<WitVariableString>();
            services.AddVariable<WitVariableTimeSpan>();
            services.AddVariable<WitVariableUInteger>();
            services.AddVariable<WitVariableULong>();
            services.AddVariable<WitVariableUShort>();
            services.AddVariable<WitVariableObject>();
            services.AddVariable<WitVariableTuple>();
            services.AddVariable<WitVariableProcessingOptions>();
            
            services.AddCollection<WitVariableBlobCollection>();
            services.AddCollection<WitVariableBooleanCollection>();
            services.AddCollection<WitVariableByteCollection>();
            services.AddCollection<WitVariableColorCollection>();
            services.AddCollection<WitVariableDateTimeCollection>();
            services.AddCollection<WitVariableDateTimeOffsetCollection>();
            services.AddCollection<WitVariableDecimalCollection>();
            services.AddCollection<WitVariableDoubleCollection>();
            services.AddCollection<WitVariableFloatCollection>();
            services.AddCollection<WitVariableGuidCollection>();
            services.AddCollection<WitVariableIntegerCollection>();
            services.AddCollection<WitVariableLongCollection>();
            services.AddCollection<WitVariableSByteCollection>();
            services.AddCollection<WitVariableShortCollection>();
            services.AddCollection<WitVariableStringCollection>();
            services.AddCollection<WitVariableTimeSpanCollection>();
            services.AddCollection<WitVariableUIntegerCollection>();
            services.AddCollection<WitVariableULongCollection>();
            services.AddCollection<WitVariableUShortCollection>();
            services.AddCollection<WitVariableObjectCollection>();
            services.AddCollection<WitVariableTupleCollection>();

            services.AddActivityAdapter<WitActivityByteRange, WitActivityAdapterByteRange>();
            services.AddActivityAdapter<WitActivityDecimalRange, WitActivityAdapterDecimalRange>();
            services.AddActivityAdapter<WitActivityDoubleRange, WitActivityAdapterDoubleRange>();
            services.AddActivityAdapter<WitActivityFloatRange, WitActivityAdapterFloatRange>();
            services.AddActivityAdapter<WitActivityIntegerRange, WitActivityAdapterIntegerRange>();
            services.AddActivityAdapter<WitActivityLongRange, WitActivityAdapterLongRange>();
            services.AddActivityAdapter<WitActivitySByteRange, WitActivityAdapterSByteRange>();
            services.AddActivityAdapter<WitActivityShortRange, WitActivityAdapterShortRange>();
            services.AddActivityAdapter<WitActivityUIntegerRange, WitActivityAdapterUIntegerRange>();
            services.AddActivityAdapter<WitActivityULongRange, WitActivityAdapterULongRange>();
            services.AddActivityAdapter<WitActivityUShortRange, WitActivityAdapterUShortRange>();
            
            services.AddActivityAdapter<WitActivityArray, WitActivityAdapterArray>();
            services.AddActivityAdapter<WitActivityBoolean, WitActivityAdapterBoolean>();
            services.AddActivityAdapter<WitActivityByte, WitActivityAdapterByte>();
            services.AddActivityAdapter<WitActivityColor, WitActivityAdapterColor>();
            services.AddActivityAdapter<WitActivityDateTime, WitActivityAdapterDateTime>();
            services.AddActivityAdapter<WitActivityDateTimeNow, WitActivityAdapterDateTimeNow>();
            services.AddActivityAdapter<WitActivityDateTimeOffset, WitActivityAdapterDateTimeOffset>();
            services.AddActivityAdapter<WitActivityDateTimeOffsetNow, WitActivityAdapterDateTimeOffsetNow>();
            services.AddActivityAdapter<WitActivityDecimal, WitActivityAdapterDecimal>();
            services.AddActivityAdapter<WitActivityDouble, WitActivityAdapterDouble>();
            services.AddActivityAdapter<WitActivityFloat, WitActivityAdapterFloat>();
            services.AddActivityAdapter<WitActivityGuid, WitActivityAdapterGuid>();
            services.AddActivityAdapter<WitActivityNewGuid, WitActivityAdapterNewGuid>();
            services.AddActivityAdapter<WitActivityInteger, WitActivityAdapterInteger>();
            services.AddActivityAdapter<WitActivityLong, WitActivityAdapterLong>();
            services.AddActivityAdapter<WitActivitySByte, WitActivityAdapterSByte>();
            services.AddActivityAdapter<WitActivityShort, WitActivityAdapterShort>();
            services.AddActivityAdapter<WitActivityString, WitActivityAdapterString>();
            services.AddActivityAdapter<WitActivityTimeSpan, WitActivityAdapterTimeSpan>();
            services.AddActivityAdapter<WitActivityUInteger, WitActivityAdapterUInteger>();
            services.AddActivityAdapter<WitActivityULong, WitActivityAdapterULong>();
            services.AddActivityAdapter<WitActivityUShort, WitActivityAdapterUShort>();
            services.AddActivityAdapter<WitActivityObject, WitActivityAdapterObject>();
            services.AddActivityAdapter<WitActivityTuple, WitActivityAdapterTuple>();
            services.AddActivityAdapter<WitActivityProcessingOptions, WitActivityAdapterProcessingOptions>();

            services.AddActivityAdapter<WitActivityTupleCollection, WitActivityAdapterTupleCollection>();
            services.AddActivityAdapter<WitActivityBooleanCollection, WitActivityAdapterBooleanCollection>();
            services.AddActivityAdapter<WitActivityByteCollection, WitActivityAdapterByteCollection>();
            services.AddActivityAdapter<WitActivityColorCollection, WitActivityAdapterColorCollection>();
            services.AddActivityAdapter<WitActivityDateTimeCollection, WitActivityAdapterDateTimeCollection>();
            services.AddActivityAdapter<WitActivityDateTimeOffsetCollection, WitActivityAdapterDateTimeOffsetCollection>();
            services.AddActivityAdapter<WitActivityDecimalCollection, WitActivityAdapterDecimalCollection>();
            services.AddActivityAdapter<WitActivityDoubleCollection, WitActivityAdapterDoubleCollection>();
            services.AddActivityAdapter<WitActivityFloatCollection, WitActivityAdapterFloatCollection>();
            services.AddActivityAdapter<WitActivityGuidCollection, WitActivityAdapterGuidCollection>();
            services.AddActivityAdapter<WitActivityIntegerCollection, WitActivityAdapterIntegerCollection>();
            services.AddActivityAdapter<WitActivityLongCollection, WitActivityAdapterLongCollection>();
            services.AddActivityAdapter<WitActivitySByteCollection, WitActivityAdapterSByteCollection>();
            services.AddActivityAdapter<WitActivityShortCollection, WitActivityAdapterShortCollection>();
            services.AddActivityAdapter<WitActivityStringCollection, WitActivityAdapterStringCollection>();
            services.AddActivityAdapter<WitActivityTimeSpanCollection, WitActivityAdapterTimeSpanCollection>();
            services.AddActivityAdapter<WitActivityUIntegerCollection, WitActivityAdapterUIntegerCollection>();
            services.AddActivityAdapter<WitActivityULongCollection, WitActivityAdapterULongCollection>();
            services.AddActivityAdapter<WitActivityUShortCollection, WitActivityAdapterUShortCollection>();
            services.AddActivityAdapter<WitActivityObjectCollection, WitActivityAdapterObjectCollection>();

            services.AddResources<Resources>();
        }
    }
}
