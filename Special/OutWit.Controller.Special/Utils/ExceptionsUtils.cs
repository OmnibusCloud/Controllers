using System;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Resources;
using OutWit.Common.Utils;
using OutWit.Controller.Special.Interfaces;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special.Utils
{
    internal static class ExceptionsUtils
    {
        #region Activity Parsing Exceptions

        public static WitEngineActivityParsingException<TActivity> ActivityCreateFailException<TActivity>(this IWitActivityAdapter<TActivity> me, IWitParameter[] parameters)
            where TActivity : IWitActivity
        {
            return parameters.Length == 0
                ? new WitEngineActivityParsingException<TActivity>(me.ActivityCreateFail())
                : new WitEngineActivityParsingException<TActivity>(me.ActivityCreateFail(parameters));
        }

        #endregion
        
        #region Parameters Count Exception

        public static WitEngineActivityParsingException<TActivity> ParametersCountException<TActivity>(this IWitActivityAdapter<TActivity> me, params int[] expectedCount)
            where TActivity : IWitActivity
        {

            Debug.Assert(me is WitActivityAdapterBase<TActivity>);
            
            switch (expectedCount.Length)
            {
                case 0:
                    return new WitEngineActivityParsingException<TActivity>(me.ActivityWrongParameters("0"));
                case 1:
                    return new WitEngineActivityParsingException<TActivity>(me.ActivityWrongParameters($"{expectedCount[0]}"));
                
                default:
                    return new WitEngineActivityParsingException<TActivity>(me.ActivityWrongParameters(string.Join(", ", expectedCount)));
            }
        }

        #endregion

        #region Processing Exceptions

        public static WitEngineActivityProcessingException<TActivity> FailedToGetParameterValueException<TActivity>(this IWitActivityAdapter<TActivity> me,
            Expression<Func<TActivity, IWitParameter?>> parameter)
            where TActivity : IWitActivity
        {
            Debug.Assert(me is WitActivityAdapterBase<TActivity>);

            return new WitEngineActivityProcessingException<TActivity>($"{me.FailedToGetParameterValue(parameter.NameOfProperty())}");
        }

        public static WitEngineActivityProcessingException<TActivity> FailedToSetReturnValueException<TActivity>(this IWitActivityAdapter<TActivity> me, string? returnReference)
            where TActivity : IWitActivity
        {
            Debug.Assert(me is WitActivityAdapterBase<TActivity>);

            return new WitEngineActivityProcessingException<TActivity>($"{me.FailedToSetReturnValue(returnReference)}");
        }

        public static WitEngineActivityProcessingException<TActivity> ManuallyTriggeredException<TActivity>(this IWitActivityAdapter<TActivity> me)
            where TActivity : IWitActivity
        {
            Debug.Assert(me is WitActivityAdapterBase<TActivity>);

            return new WitEngineActivityProcessingException<TActivity>($"{me.ManualException()}");
        }

        #endregion

        #region Expected Exceptions

        public static WitEngineActivityParsingException<TActivity> ExpectedException<TActivity>(this IWitActivityAdapter<TActivity> me, 
            Expression<Func<TActivity, IWitParameter?>> parameter, Func<IWitActivityAdapter<TActivity>, string>? innerText = null)
            where TActivity : IWitActivity
        {
            Debug.Assert(me is WitActivityAdapterBase<TActivity>);
            
            return innerText == null
                ? new WitEngineActivityParsingException<TActivity>($"{me.ActivityWrongParameter(parameter.NameOfProperty())}")
                : new WitEngineActivityParsingException<TActivity>($"{me.ActivityWrongParameter(parameter.NameOfProperty())} : {innerText(me)}");
        }

        public static WitEngineActivityParsingException<TActivity> ExpectedStringException<TActivity>(this IWitActivityAdapter<TActivity> me, Expression<Func<TActivity, IWitParameter?>> parameter)
            where TActivity : IWitActivity
        {
            return me.ExpectedException(parameter, ExpectedString);
        }

        public static WitEngineActivityParsingException<TActivity> ExpectedNumericException<TActivity>(this IWitActivityAdapter<TActivity> me, Expression<Func<TActivity, IWitParameter?>> parameter)
            where TActivity : IWitActivity
        {
            return me.ExpectedException(parameter, ExpectedNumeric);
        }

        public static WitEngineActivityParsingException<TActivity> ExpectedBooleanException<TActivity>(this IWitActivityAdapter<TActivity> me, Expression<Func<TActivity, IWitParameter?>> parameter)
            where TActivity : IWitActivity
        {
            return me.ExpectedException(parameter, ExpectedBoolean);
        }

        public static WitEngineActivityParsingException<TActivity> ExpectedArrayException<TActivity>(this IWitActivityAdapter<TActivity> me, Expression<Func<TActivity, IWitParameter?>> parameter)
            where TActivity : IWitActivity
        {
            return me.ExpectedException(parameter, ExpectedArray);
        }

        public static WitEngineActivityParsingException<TActivity> ExpectedReferenceException<TActivity>(this IWitActivityAdapter<TActivity> me, Expression<Func<TActivity, IWitParameter?>> parameter)
            where TActivity : IWitActivity
        {
            return me.ExpectedException(parameter, ExpectedReference);
        }

        public static WitEngineActivityParsingException<TActivity> ExpectedConditionException<TActivity>(this IWitActivityAdapter<TActivity> me, Expression<Func<TActivity, IWitParameter?>> parameter)
            where TActivity : IWitActivity
        {
            return me.ExpectedException(parameter, ExpectedCondition);
        }

        #endregion

        #region Strings

        private static string ExpectedString<TActivity>(this IWitActivityAdapter<TActivity> me)
            where TActivity : IWitActivity
        {
            return me.Resources["WitActivityExpectedStringError"];
        }

        private static string ExpectedNumeric<TActivity>(this IWitActivityAdapter<TActivity> me)
            where TActivity : IWitActivity
        {
            return me.Resources["WitActivityExpectedNumericError"];
        }

        private static string ExpectedBoolean<TActivity>(this IWitActivityAdapter<TActivity> me)
            where TActivity : IWitActivity
        {
            return me.Resources["WitActivityExpectedBooleanError"];
        }

        private static string ExpectedArray<TActivity>(this IWitActivityAdapter<TActivity> me)
            where TActivity : IWitActivity
        {
            return me.Resources["WitActivityExpectedArrayError"];
        }

        private static string ExpectedReference<TActivity>(this IWitActivityAdapter<TActivity> me)
            where TActivity : IWitActivity
        {
            return me.Resources["WitActivityExpectedReferenceError"];
        }

        private static string ExpectedCondition<TActivity>(this IWitActivityAdapter<TActivity> me)
            where TActivity : IWitActivity
        {
            return me.Resources["WitActivityExpectedConditionError"];
        }

        private static string ActivityWrongParameters<TActivity>(this IWitActivityAdapter<TActivity> me, string expectedCount)
            where TActivity : IWitActivity
        {
            return string.Format(me.Resources["WitActivityWrongParametersError"], typeof(TActivity).Name, expectedCount);
        }

        private static string ActivityWrongParameter<TActivity>(this IWitActivityAdapter<TActivity> me, string? parameter)
            where TActivity : IWitActivity
        {
            return string.Format(me.Resources["WitActivityWrongParameterError"], parameter, typeof(TActivity).Name);
        }

        public static string ActivityCreateFail<TActivity>(this IWitActivityAdapter<TActivity> me, IWitParameter[] parameters)
            where TActivity : IWitActivity
        {
            return string.Format(me.Resources["WitActivityCreateError"], typeof(TActivity).Name,
                string.Join(", ", parameters.Select(parameter => $"{parameter}")));
        }

        public static string ActivityCreateFail<TActivity>(this IWitActivityAdapter<TActivity> me)
            where TActivity : IWitActivity
        {
            return string.Format(me.Resources["WitActivityNoParametersCreateError"], typeof(TActivity).Name);
        }

        private static string FailedToGetParameterValue<TActivity>(this IWitActivityAdapter<TActivity> me, string? parameter)
            where TActivity : IWitActivity
        {
            return string.Format(me.Resources["FailedToGetParameterValueError"], parameter, typeof(TActivity).Name);
        }

        private static string FailedToSetReturnValue<TActivity>(this IWitActivityAdapter<TActivity> me, string? parameter)
            where TActivity : IWitActivity
        {
            return string.Format(me.Resources["FailedToSetReturnValueError"], parameter, typeof(TActivity).Name);
        }

        private static string ManualException<TActivity>(this IWitActivityAdapter<TActivity> me)
            where TActivity : IWitActivity
        {
            return string.Format(me.Resources["WitActivityManualError"], typeof(TActivity).Name);
        }

        #endregion
    }
}
