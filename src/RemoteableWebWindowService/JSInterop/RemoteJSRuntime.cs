using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using PeakSwc.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteableWebWindowService
{
    public class RemoteJSRuntime :JSRuntime
    {
       
        private readonly ILogger<RemoteJSRuntime> _logger;
        private readonly IPC _ipc;

        public RemoteJSRuntime(IPC ipc, ILogger<RemoteJSRuntime> logger)
        {
            _ipc = ipc;
            //ptions = options.Value;
            _logger = logger;
            DefaultAsyncTimeout = TimeSpan.FromMinutes(1);
            JsonSerializerOptions.Converters.Add(new ElementReferenceJsonConverter());
        }

        private static Type VoidTaskResultType = typeof(Task).Assembly
          .GetType("System.Threading.Tasks.VoidTaskResult", true);


        protected override void BeginInvokeJS(long asyncHandle, string identifier, string argsJson)
        {
            if (_ipc is null)
            {
                throw new InvalidOperationException("JavaScript interop calls cannot be issued at this time.");
            }

            Log.BeginInvokeJS(_logger, asyncHandle, identifier);

            _ipc.SendMessage($"JS.BeginInvokeJS", asyncHandle,identifier,argsJson);
        }

        protected override void EndInvokeDotNet(DotNetInvocationInfo invocationInfo, in DotNetInvocationResult invocationResult)
        {
            // The other params aren't strictly required and are only used for logging
            var resultOrError = invocationResult.Success ? HandlePossibleVoidTaskResult(invocationResult.Result) : invocationResult.Exception.ToString();
            if (resultOrError != null)
            {
                _ipc.SendMessage("JS.EndInvokeDotNet", invocationInfo.CallId, invocationResult.Success, resultOrError);
            }
            else
            {
                _ipc.SendMessage("JS.EndInvokeDotNet", invocationInfo.CallId, invocationResult.Success);
            }
        }

        private static object HandlePossibleVoidTaskResult(object result)
        {
            // Looks like the TaskGenericsUtil logic in Microsoft.JSInterop doesn't know how to
            // understand System.Threading.Tasks.VoidTaskResult
            return result?.GetType() == VoidTaskResultType ? null : result;
        }

        public static class Log
        {
            private static readonly Action<ILogger, long, string, Exception> _beginInvokeJS =
                LoggerMessage.Define<long, string>(
                    LogLevel.Debug,
                    new EventId(1, "BeginInvokeJS"),
                    "Begin invoke JS interop '{AsyncHandle}': '{FunctionIdentifier}'");

            private static readonly Action<ILogger, string, string, string, Exception> _invokeStaticDotNetMethodException =
                LoggerMessage.Define<string, string, string>(
                LogLevel.Debug,
                new EventId(2, "InvokeDotNetMethodException"),
                "There was an error invoking the static method '[{AssemblyName}]::{MethodIdentifier}' with callback id '{CallbackId}'.");

            private static readonly Action<ILogger, string, long, string, Exception> _invokeInstanceDotNetMethodException =
                LoggerMessage.Define<string, long, string>(
                LogLevel.Debug,
                new EventId(2, "InvokeDotNetMethodException"),
                "There was an error invoking the instance method '{MethodIdentifier}' on reference '{DotNetObjectReference}' with callback id '{CallbackId}'.");

            private static readonly Action<ILogger, string, string, string, Exception> _invokeStaticDotNetMethodSuccess =
                LoggerMessage.Define<string, string, string>(
                LogLevel.Debug,
                new EventId(3, "InvokeDotNetMethodSuccess"),
                "Invocation of '[{AssemblyName}]::{MethodIdentifier}' with callback id '{CallbackId}' completed successfully.");

            private static readonly Action<ILogger, string, long, string, Exception> _invokeInstanceDotNetMethodSuccess =
                LoggerMessage.Define<string, long, string>(
                LogLevel.Debug,
                new EventId(3, "InvokeDotNetMethodSuccess"),
                "Invocation of '{MethodIdentifier}' on reference '{DotNetObjectReference}' with callback id '{CallbackId}' completed successfully.");


            internal static void BeginInvokeJS(ILogger logger, long asyncHandle, string identifier) =>
                _beginInvokeJS(logger, asyncHandle, identifier, null);

            internal static void InvokeDotNetMethodException(ILogger logger, in DotNetInvocationInfo invocationInfo, Exception exception)
            {
                if (invocationInfo.AssemblyName != null)
                {
                    _invokeStaticDotNetMethodException(logger, invocationInfo.AssemblyName, invocationInfo.MethodIdentifier, invocationInfo.CallId, exception);
                }
                else
                {
                    _invokeInstanceDotNetMethodException(logger, invocationInfo.MethodIdentifier, invocationInfo.DotNetObjectId, invocationInfo.CallId, exception);
                }
            }

            internal static void InvokeDotNetMethodSuccess(ILogger<RemoteJSRuntime> logger, in DotNetInvocationInfo invocationInfo)
            {
                if (invocationInfo.AssemblyName != null)
                {
                    _invokeStaticDotNetMethodSuccess(logger, invocationInfo.AssemblyName, invocationInfo.MethodIdentifier, invocationInfo.CallId, null);
                }
                else
                {
                    _invokeInstanceDotNetMethodSuccess(logger, invocationInfo.MethodIdentifier, invocationInfo.DotNetObjectId, invocationInfo.CallId, null);
                }

            }
        }
    }
}
