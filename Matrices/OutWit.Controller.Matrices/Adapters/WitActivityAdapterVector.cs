using Microsoft.Extensions.Logging;
using OutWit.Common.Interfaces;
using OutWit.Controller.Matrices.Activities;
using OutWit.Controller.Matrices.Interfaces;
using OutWit.Controller.Matrices.Model;
using OutWit.Controller.Matrices.Model.Interfaces;
using OutWit.Controller.Matrices.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Matrices.Adapters
{
    internal sealed class WitActivityAdapterVector : WitActivityAdapterFunction<WitActivityVector>, IWitActivityAdapter<WitActivityVector>
    {
        #region Constructors

        public WitActivityAdapterVector(IWitProcessingManager processingManager, IResources resources, ILogger logger) :
            base(processingManager, logger)
        {
            Resources = resources;
        } 

        #endregion

        #region Processing

        protected override async Task Process(WitActivityVector activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            VectorType vectorType = VectorType.Row;
            WitVector<double>? vector = null;
            
            if (activity.Type != null && (!pool.TryGetString(activity.Type, out string? typeString) || !VectorType.TryParse(typeString, false, out vectorType)))
                throw this.FailedToGetParameterValueException(activityVector => activityVector.Type);

            switch (activity.Data)
            {
                case IWitArray array:
                    if(!pool.TryGetCollection(array, out IReadOnlyList<double>? values) || values == null)
                        throw this.FailedToGetParameterValueException(activityVector => activityVector.Type);
                    vector = WitVector<double>.Create(values, vectorType);
                    break;

                case IWitConstant constant:
                    if (!pool.TryGetValue(constant, out int constantSize))
                        throw this.FailedToGetParameterValueException(activityVector => activityVector.Data);
                    
                    vector = WitVector<double>.Create(constantSize, vectorType);
                    break;

                case IWitReference reference:
                    if (pool.TryGetCollection(reference, out IReadOnlyList<double>? collection) && collection != null)
                        vector = WitVector<double>.Create(collection, vectorType);
                    else if (pool.TryGetValue(reference, out IWitVector<double>? value) && value != null)
                        vector = WitVector<double>.Create(value, vectorType);
                    else if(pool.TryGetValue(reference, out int size))
                        vector = WitVector<double>.Create(size, vectorType);
                    else
                        throw this.FailedToGetParameterValueException(activityVector => activityVector.Data);
                    break;

            }

            var result = pool.TrySetValue(activity.ReturnReference, vector);

            if (!string.IsNullOrEmpty(activity.ReturnReference) && !result)
                throw this.FailedToSetReturnValueException(activity.ReturnReference);
        }

        #endregion

        #region Parsing

        protected override WitActivityVector CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 1:
                        return CreateActivity(parameters[0]);
                    
                    case 2:
                        return CreateActivity(parameters[0], parameters[1]);

                    default:
                        throw this.ParametersCountException(1, 2);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);
            }
            
        }

        private WitActivityVector CreateActivity(IWitParameter data)
        {
            if (!data.IsArrayOrReference() && !data.IsNumericOrReference())
                throw this.ExpectedArrayException(vector => vector.Data);

            return new WitActivityVector
            {
                Data = data
            };
        }

        private WitActivityVector CreateActivity(IWitParameter data, IWitParameter type)
        {
            if (!data.IsArrayOrReference() && !data.IsNumericOrReference())
                throw this.ExpectedArrayException(vector => vector.Data);

            if (!type.IsStringOrReference())
                throw this.ExpectedStringException(vector => vector.Type);

            return new WitActivityVector
            {
                Data = data,
                Type = type,
            };
        }

        #endregion


        public IResources Resources { get; }

    }
}
