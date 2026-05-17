using Microsoft.Extensions.Logging;
using OutWit.Common.Interfaces;
using OutWit.Controller.Special.Activities;
using OutWit.Controller.Special.Interfaces;
using OutWit.Controller.Special.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;

namespace OutWit.Controller.Special.Adapters
{
    internal sealed class WitActivityAdapterSpecialZip : WitActivityAdapterFunction<WitActivitySpecialZip>, IWitActivityAdapter<WitActivitySpecialZip>
    {
        #region Constructors
        public WitActivityAdapterSpecialZip(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task Process(WitActivitySpecialZip activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            if(activity.Values == null)
                return;
            
            var values = new List<IReadOnlyList<object?>>(activity.Values.Length);

            for (int i = 0; i < activity.Values.Length; i++)
            {
                IReadOnlyList<object?>? collection = GetCollectionFromArray(activity.Values[i], pool) ??
                                                     GetCollectionFromReference(activity.Values[i], pool);
                if (collection == null)
                    throw this.FailedToGetParameterValueException(zip => zip.Values![i]);

                values.Add(collection);
            }

            var tupple = new List<object?[]>();
            for (int i = 0; i < values[0].Count; i++)
            {
                var row = new object?[values.Count];
                for (int j = 0; j < values.Count; j++)
                {
                    row[j] = values[j][i];
                }
                tupple.Add(row);
            }
            
            bool result = pool.TrySetValue(activity.ReturnReference, tupple);
            
            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);

        }
        
        private IReadOnlyList<object?>? GetCollectionFromReference(IWitParameter parameter, IWitVariablesCollection pool)
        {
            if(parameter is not IWitReference reference)
                return null;
            
            if(!pool.TryGetObject(reference, out object? value))
                return null;

            if (value is not IEnumerable collection)
                return null;
            
            if(value is IWitArray array)
                return GetCollectionFromArray(array, pool);

            return collection.Cast<object?>().ToList();
        }
        
        private IReadOnlyList<object?>? GetCollectionFromArray(IWitParameter parameter, IWitVariablesCollection pool)
        {
            if(parameter is not IWitArray array)
                return null;
            
            return !pool.TryGetCollection(array, out IReadOnlyList<object?>? value) ? null : value;
        }

        #endregion

        #region Parising

        protected override WitActivitySpecialZip CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                if (parameters.Length < 2)
                    throw this.ParametersCountException(2);
                return new WitActivitySpecialZip
                {
                    Values = parameters
                };
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);
            }
        }

        #endregion

        public IResources Resources { get; }
    }
}
