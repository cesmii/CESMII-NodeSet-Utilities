using Microsoft.Extensions.Logging;
using System;
using Xunit.Abstractions;

namespace CESMII.NodeSetUtilities.Tests
{
    internal class XUnitTestLogger<T> : ILogger<T>
    {
        private ITestOutputHelper _output;
        private readonly LogLevel _logLevel;

        public XUnitTestLogger(ITestOutputHelper output, LogLevel logLevel = LogLevel.Warning)
        {
            this._output = output;
            this._logLevel = logLevel;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return new DummyDisposable();
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= _logLevel;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                _output.WriteLine($"{logLevel} {formatter(state, exception)}");
            }
        }

        private class DummyDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}