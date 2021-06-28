﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

namespace PeakSWC.RemoteBlazorWebView.Windows
{
    /// <summary>
    /// A dispatcher that does not dispatch but invokes directly.
    /// </summary>
    public class DesktopDispatcher : Dispatcher, IDispatcher
    {
        private readonly DesktopSynchronizationContext context;

        /// <summary>
        /// Initializes a new instance of the <see cref="DesktopDispatcher"/> class.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token to pass to the synchronizationcontext.</param>
        public DesktopDispatcher(CancellationToken cancellationToken)
        {
            this.context = new DesktopSynchronizationContext(cancellationToken);
            this.context.UnhandledException += (sender, e) =>
            {
                this.OnUnhandledException(new UnhandledExceptionEventArgs(e, false));
            };
        }

        /// <summary>
        /// Gets and internal reference to the context.
        /// </summary>
        internal DesktopSynchronizationContext Context => this.context;

        /// <summary>
        /// Returns a value that determines whether using the dispatcher to invoke a work
        /// item is required from the current context.
        /// </summary>
        /// <returns> true if invoking is required, otherwise false.</returns>
        public override bool CheckAccess() => System.Threading.SynchronizationContext.Current == this.context;

        /// <summary>
        /// Invokes the given System.Action in the context of the associated
        /// Microsoft.AspNetCore.Components.RenderTree.Renderer.
        /// </summary>
        /// <param name="workItem">The action to execute.</param>
        /// <returns>
        /// A System.Threading.Tasks.Task that will be completed when the action has finished
        /// executing.
        /// </returns>
        public override Task InvokeAsync(Action workItem)
        {
            if (this.CheckAccess())
            {
                workItem();
                return Task.CompletedTask;
            }

            var taskCompletionSource = new TaskCompletionSource<object>();

            this.context.Post(
                state =>
                {
                    var taskCompletionSource = state as TaskCompletionSource<object>;
                    try
                    {
                        workItem();
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
                        taskCompletionSource?.SetResult(null);
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
                    }
                    catch (OperationCanceledException)
                    {
                        taskCompletionSource?.SetCanceled();
                    }
                    catch (Exception exception)
                    {
                        taskCompletionSource?.SetException(exception);
                    }
                }, taskCompletionSource);

            return taskCompletionSource.Task;
        }

        /// <summary>
        /// Invokes the given System.Func'1 in the context of the associated
        /// Microsoft.AspNetCore.Components.RenderTree.Renderer.
        /// </summary>
        /// <param name="workItem">The action to execute.</param>
        /// <returns>
        /// A System.Threading.Tasks.Task that will be completed when the action has finished
        /// executing.
        /// </returns>
        public override Task InvokeAsync(Func<Task> workItem)
        {
            if (this.CheckAccess())
            {
                return workItem();
            }

            var taskCompletionSource = new TaskCompletionSource<object>();

            this.context.Post(
                async state =>
                {
                    var taskCompletionSource = state as TaskCompletionSource<object>;
                    try
                    {
                        await workItem();
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
                        taskCompletionSource?.SetResult(null);
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
                    }
                    catch (OperationCanceledException)
                    {
                        taskCompletionSource?.SetCanceled();
                    }
                    catch (Exception exception)
                    {
                        taskCompletionSource?.SetException(exception);
                    }
                }, taskCompletionSource);

            return taskCompletionSource.Task;
        }

        /// <summary>
        /// Invokes the given System.Func'1 in the context of the associated
        /// Microsoft.AspNetCore.Components.RenderTree.Renderer.
        /// </summary>
        /// <param name="workItem">The action to execute.</param>
        /// <returns>
        /// A System.Threading.Tasks.Task that will be completed when the action has finished
        /// executing.
        /// </returns>
        /// <typeparam name="TResult">The return type.</typeparam>
        public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem)
        {
            if (this.CheckAccess())
            {
                return Task.FromResult(workItem());
            }

            var taskCompletionSource = new TaskCompletionSource<TResult>();

            this.context.Post(
                state =>
                {
                    var taskCompletionSource = state as TaskCompletionSource<object>;
                    try
                    {
                        TResult result = workItem();
#pragma warning disable CS8604 // Possible null reference argument.
                        taskCompletionSource?.SetResult(result);
#pragma warning restore CS8604 // Possible null reference argument.
                    }
                    catch (OperationCanceledException)
                    {
                        taskCompletionSource?.SetCanceled();
                    }
                    catch (Exception exception)
                    {
                        taskCompletionSource?.SetException(exception);
                    }
                }, taskCompletionSource);

            return taskCompletionSource.Task;
        }

        /// <summary>
        /// Invokes the given System.Func'1 in the context of the associated
        /// Microsoft.AspNetCore.Components.RenderTree.Renderer.
        /// </summary>
        /// <param name="workItem">The action to execute.</param>
        /// <returns>
        /// A System.Threading.Tasks.Task that will be completed when the action has finished
        /// executing.
        /// </returns>
        /// <typeparam name="TResult">The return type.</typeparam>
        public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
        {
            if (this.CheckAccess())
            {
                return workItem();
            }

            var taskCompletionSource = new TaskCompletionSource<TResult>();

            this.context.Post(
                async state =>
                {
                    var taskCompletionSource = state as TaskCompletionSource<object>;
                    try
                    {
                        TResult result = await workItem();
#pragma warning disable CS8604 // Possible null reference argument.
                        taskCompletionSource?.SetResult(result);
#pragma warning restore CS8604 // Possible null reference argument.
                    }
                    catch (OperationCanceledException)
                    {
                        taskCompletionSource?.SetCanceled();
                    }
                    catch (Exception exception)
                    {
                        taskCompletionSource?.SetException(exception);
                    }
                }, taskCompletionSource);

            return taskCompletionSource.Task;
        }
    }
}