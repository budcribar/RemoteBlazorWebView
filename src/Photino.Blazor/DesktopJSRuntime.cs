﻿using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using System;
using System.Threading.Tasks;

namespace Photino.Blazor
{
    internal class DesktopJSRuntime : JSRuntime
    {
        private readonly IPC _ipc;
        private static Type VoidTaskResultType = typeof(Task).Assembly
            .GetType("System.Threading.Tasks.VoidTaskResult", true);

        public DesktopJSRuntime(IPC ipc)
        {
            this.JsonSerializerOptions.Converters.Add(new ElementReferenceJsonConverter());
            _ipc = ipc ?? throw new ArgumentNullException(nameof(ipc));
        }


        protected override void EndInvokeDotNet(DotNetInvocationInfo invocationInfo, in DotNetInvocationResult invocationResult)
        {
            // The other params aren't strictly required and are only used for logging
            var resultOrError = invocationResult.Success ? HandlePossibleVoidTaskResult(invocationResult.Result) : invocationResult.Exception.ToString();
            if (resultOrError != null)
            {
                _ipc.Send("JS.EndInvokeDotNet", invocationInfo.CallId, invocationResult.Success, resultOrError);
            }
            else
            {
                _ipc.Send("JS.EndInvokeDotNet", invocationInfo.CallId, invocationResult.Success);
            }
        }

        private static object HandlePossibleVoidTaskResult(object result)
        {
            // Looks like the TaskGenericsUtil logic in Microsoft.JSInterop doesn't know how to
            // understand System.Threading.Tasks.VoidTaskResult
            return result?.GetType() == VoidTaskResultType ? null : result;
        }

        protected override void BeginInvokeJS(long taskId, string identifier, string argsJson, JSCallResultType resultType, long targetInstanceId)
        {
            //New (.NET 5)  version of Microsoft.JSInterop added this overload - we revert to the previous overload.
            _ipc.Send("JS.BeginInvokeJS", taskId, identifier, argsJson, resultType, targetInstanceId);
            //throw new NotImplementedException();
        }
    }
}
