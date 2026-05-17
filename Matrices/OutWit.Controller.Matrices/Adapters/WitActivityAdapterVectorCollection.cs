using Microsoft.Extensions.Logging;
using OutWit.Common.Interfaces;
using OutWit.Controller.Matrices.Activities;
using OutWit.Controller.Matrices.Interfaces;
using OutWit.Controller.Matrices.Model;
using OutWit.Controller.Matrices.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Matrices.Adapters
{
    internal sealed class WitActivityAdapterVectorCollection : WitActivityAdapterFunction<WitActivityVectorCollection>, IWitActivityAdapter<WitActivityVectorCollection>
    {
        #region Constructors

        public WitActivityAdapterVectorCollection(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityVectorCollection activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            IReadOnlyList<WitVector<double>?>? collection = null;

            switch (activity.Value)
            {
                case IWitArray array:
                    if(!pool.TryGetCollection(array, out IReadOnlyList<IWitArray?>? values) || values == null)
                        throw this.FailedToGetParameterValueException(activityVector => activityVector.Value);
                    collection = BuildFromArrays(pool, values);

                    break;

                case IWitReference reference:
                    if (!pool.TryGetCollection(reference, out collection) && collection != null)
                        throw this.FailedToGetParameterValueException(activityVector => activityVector.Value);
                    break;

            }

            var result = pool.TrySetValue(activity.ReturnReference, collection);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        }

        private IReadOnlyList<WitVector<double>?> BuildFromArrays(IWitVariablesCollection pool, IReadOnlyList<IWitArray?> arrays)
        {
            var vectors = new List<WitVector<double>?>();
            
            foreach (var array in arrays)
            {
                if(array == null)
                    continue;
                
                if (!pool.TryGetCollection(array, out IReadOnlyList<double>? values) || values == null)
                    throw this.FailedToGetParameterValueException(activityVector => activityVector.Value);
                vectors.Add(WitVector<double>.Create(values));
            }

            return vectors;
        }

        #endregion

        #region Parsing

        protected override WitActivityVectorCollection CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                if (parameters.Length != 1)
                    throw this.ParametersCountException(1);

                if (parameters[0].IsArrayOrReference())
                    return new WitActivityVectorCollection { Value = parameters[0] };

                throw this.ExpectedArrayException(activityObject => activityObject.Value);

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
