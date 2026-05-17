using OutWit.Common.Interfaces;
using OutWit.Common.Utils;
using OutWit.Controller.Variables.Interfaces;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.VariableAdapters;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using System;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Xml.Linq;

namespace OutWit.Controller.Variables.Utils
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
        
        #region Variable Parsing Exceptions

        public static WitEngineVariableParsingException<TVariable> WrongVariableParameterException<TVariable>(this IWitVariableAdapter<TVariable> me, string variableName, IWitParameter? parameter, params Type[] expected)
            where TVariable : IWitVariable
        {
            return new WitEngineVariableParsingException<TVariable>(me.WrongVariableParameter(variableName, parameter, expected));
        }

        public static WitEngineVariableParsingException<TVariable> VariableCreateFailException<TVariable>(this IWitVariableAdapter<TVariable> me, string variableName, IWitParameter? parameter, Exception? inner = null)
            where TVariable : IWitVariable
        {
            return new WitEngineVariableParsingException<TVariable>(me.VariableCreateFail(variableName, parameter), inner);
        }

        public static WitEngineVariableParsingException<TVariable> VariableParseFailException<TVariable, TValue>(this IWitVariableAdapter<TVariable> me, string? value)
            where TVariable : WitVariable<TValue?>
        {
            return new WitEngineVariableParsingException<TVariable>(me.VariableParseFail<TVariable, TValue>(value));
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
        
        public static WitEngineActivityProcessingException<TActivity> InvalidRangeException<TActivity>(this IWitActivityAdapter<TActivity> me, object? from, object? to)
            where TActivity : IWitActivity
        {
            Debug.Assert(me is WitActivityAdapterBase<TActivity>);

            return new WitEngineActivityProcessingException<TActivity>($"{me.InvalidRangeExceptionValue(from, to)}");
        }
        
        public static WitEngineActivityProcessingException<TActivity> InvalidRangeException<TActivity>(this IWitActivityAdapter<TActivity> me, object? from, object? to, object? step)
            where TActivity : IWitActivity
        {
            Debug.Assert(me is WitActivityAdapterBase<TActivity>);

            return new WitEngineActivityProcessingException<TActivity>($"{me.InvalidRangeExceptionValue(from, to, step)}");
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

        public static string VariableCreateFail<TVariable>(this IWitVariableAdapter<TVariable> me, string variableName, IWitParameter? parameter)
            where TVariable : IWitVariable
        {
            return string.Format(me.Resources["WitVariableCreateError"], typeof(TVariable).Name, variableName, parameter?.GetType().Name ?? "");
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

        public static string VariableParseFail<TVariable, TValue>(this IWitVariableAdapter<TVariable> me, string? value)
            where TVariable : IWitVariable
        {
            return string.Format(me.Resources["WitVariableParseError"], typeof(TValue).Name, value);
        }

        public static string WrongVariableParameter<TVariable>(this IWitVariableAdapter<TVariable> me, string variableName, IWitParameter? parameter, params Type[] expected)
            where TVariable : IWitVariable
        {
            return string.Format(me.Resources["WitVariableWrongParameterError"], parameter?.GetType().Name ?? "", variableName, typeof(TVariable).Name,
                string.Join(", ", expected.Select(x => x.Name)));
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
        
        private static string InvalidRangeExceptionValue<TActivity>(this IWitActivityAdapter<TActivity> me, object? from, object? to)
            where TActivity : IWitActivity
        {
            //"The 'to' value must be greater than the 'from' value."
            return string.Format(me.Resources["InvalidRangeException"], from, to);
        }
        
        private static string InvalidRangeExceptionValue<TActivity>(this IWitActivityAdapter<TActivity> me, object? from, object? to, object? step)
            where TActivity : IWitActivity
        {
            //"The 'to' value must be greater than the 'from' value."
            return string.Format(me.Resources["InvalidRangeWithStepException"], from, to, step);
        }

        #endregion
    }
}
