using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// A logger that keeps the warnings it is given, so a test can read what an adapter said.
    /// </summary>
    internal sealed class MockLogger : ILogger
    {
        #region Fields

        private readonly List<string> m_warnings = new();

        #endregion

        #region Functions

        /// <summary>
        /// Forgets the warnings kept so far.
        /// </summary>
        public void Clear()
        {
            lock (m_warnings)
                m_warnings.Clear();
        }

        /// <summary>
        /// The warnings kept so far that contain the given text.
        /// </summary>
        /// <param name="text">The text to look for.</param>
        /// <returns>The warnings, in the order they came.</returns>
        public IReadOnlyList<string> WarningsWith(string text)
        {
            lock (m_warnings)
                return m_warnings.Where(warning => warning.Contains(text, StringComparison.Ordinal)).ToList();
        }

        #endregion

        #region ILogger

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Warning)
                return;

            string message = formatter(state, exception);

            lock (m_warnings)
                m_warnings.Add(message);
        }

        #endregion
    }
}
