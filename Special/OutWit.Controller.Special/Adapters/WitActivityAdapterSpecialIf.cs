using Microsoft.Extensions.Logging;
using OutWit.Common.Interfaces;
using OutWit.Controller.Special.Activities;
using OutWit.Controller.Special.Interfaces;
using OutWit.Controller.Special.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using System;
using System.Threading.Tasks;
using OutWit.Common.Values;
using OutWit.Engine.Data.Status;

namespace OutWit.Controller.Special.Adapters
{
    internal sealed class WitActivityAdapterSpecialIf : WitActivityAdapterComposite<WitActivitySpecialIf>, IWitActivityAdapter<WitActivitySpecialIf>
    {
        #region Constructors

        public WitActivityAdapterSpecialIf(IWitControllerManager controllerManager, IWitProcessingManager processingManager, IResources resources, ILogger logger)
            :base(controllerManager, processingManager, logger)
        {
            Resources = resources;
        }

        #endregion

        #region Processing

        protected override async Task ProcessInner(WitActivitySpecialIf activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status, bool reportProgress)
        {
            if(activity.Activities.Count == 0)
                return;
            
            if (activity.Right == null)
            {
                if(!pool.TryGetValue(activity.Left, out bool value) || !value)
                    return;

                await Execute(activity, pool, activityStatus, status);
            }
            else
            {
                if (!pool.TryGetObject(activity.Left, out object? leftValue))
                    throw this.FailedToGetParameterValueException(activityIf => activityIf.Left);
                
                if (!pool.TryGetObject(activity.Right, out object? rightValue))
                    throw this.FailedToGetParameterValueException(activityIf => activityIf.Right);
                
                string? condition = activity.Condition?.Condition;
                if (string.IsNullOrEmpty(condition))
                    throw this.FailedToGetParameterValueException(activityIf => activityIf.Condition);

                switch (condition)
                {
                    case "==":
                    {
                        if (leftValue.Check(rightValue))
                            await Execute(activity, pool, activityStatus, status);
                        break;
                    }
                    case "!=":
                    {
                        if (!leftValue.Check(rightValue))
                            await Execute(activity, pool, activityStatus, status);
                        break;
                    }
                    case ">":
                    {
                        if (leftValue is IComparable l && rightValue is IComparable r && l.CompareTo(r) > 0)
                            await Execute(activity, pool, activityStatus, status);
                        break;
                    }
                    case ">=":
                    {
                        if (leftValue is IComparable l && rightValue is IComparable r && l.CompareTo(r) >= 0)
                            await Execute(activity, pool, activityStatus, status);
                        break;
                    }
                    case "<":
                    {
                        if (leftValue is IComparable l && rightValue is IComparable r && l.CompareTo(r) < 0)
                            await Execute(activity, pool, activityStatus, status);
                        break;
                    }
                    case "<=":
                    {
                        if (leftValue is IComparable l && rightValue is IComparable r && l.CompareTo(r) <= 0)
                            await Execute(activity, pool, activityStatus, status);
                        break;
                    }
                    
                    case "&&":
                    {
                        if (leftValue is bool l && rightValue is bool r && l && r)
                            await Execute(activity, pool, activityStatus, status);
                        break;
                    }
                    case "||":
                    {
                        if (leftValue is bool l && rightValue is bool r && (l || r))
                            await Execute(activity, pool, activityStatus, status);
                        break;
                    }
                }
            }
        }

        private async Task Execute(WitActivitySpecialIf activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
        {
            foreach (var childActivity in activity.Activities)
            {
                if (status.IsFailed())
                    return;
                
                await ProcessingManager.WaitAsync(status.JobId);
                ProcessingManager.ThrowIfCancellationRequested(status.JobId);

                status.AddChild(await ControllerManager.Process(status.EngineId, status.JobId, childActivity, activityStatus, pool, false));
            }
        }

        #endregion

        #region Parsing


        protected override WitActivitySpecialIf CreateActivity(IWitParameter[] parameters)
        {
            try
            {
                switch (parameters.Length)
                {
                    case 1:
                        return CreateActivity(parameters[0]);

                    case 3:
                        return CreateActivity(parameters[0], parameters[1], parameters[2]);

                    default:
                        throw this.ParametersCountException(1, 3);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, this.ActivityCreateFail(parameters));
                throw this.ActivityCreateFailException(parameters);
            }
            
           
        }
        
        private WitActivitySpecialIf CreateActivity(IWitParameter left)
        {
            if (!left.IsBooleanOrReference())
                throw this.ExpectedBooleanException(specialIf => specialIf.Left);
            
            return new WitActivitySpecialIf
            {
                Left = left
            };
        }

        private WitActivitySpecialIf CreateActivity(IWitParameter left, IWitParameter condition, IWitParameter right)
        {
            if (left is IWitCondition)
                throw this.ExpectedException(specialIf => specialIf.Left);

            if (condition is not IWitCondition conditionValue)
                throw this.ExpectedConditionException(specialIf => specialIf.Condition);

            if (right is IWitCondition)
                throw this.ExpectedException(specialIf => specialIf.Right);

            return new WitActivitySpecialIf
            {
                Left = left,
                Condition = conditionValue,
                Right = right
            };
        }

        #endregion


        public IResources Resources { get; }
    }
}
