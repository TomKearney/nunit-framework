﻿// ***********************************************************************
// Copyright (c) 2013 Charlie Poole
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// ***********************************************************************

#if NET_4_5
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Runtime.ExceptionServices;

namespace NUnit.Framework.Internal
{
    internal abstract class AsyncInvocationRegion : IDisposable
    {
        private AsyncInvocationRegion()
        {
        }

        public static AsyncInvocationRegion Create(Delegate @delegate)
        {
            return Create(@delegate.Method);
        }

        public static AsyncInvocationRegion Create(MethodInfo method)
        {
            if (!IsAsyncOperation(method))
                throw new InvalidOperationException(@"Either asynchronous support is not available or an attempt 
at wrapping a non-async method invocation in an async region was done");

            if (method.ReturnType == typeof(void))
                return new AsyncVoidInvocationRegion();

            return new AsyncTaskInvocationRegion();
        }

        public static bool IsAsyncOperation(MethodInfo method)
        {
            return method.GetCustomAttributes(false)
                    .Any(attr => "System.Runtime.CompilerServices.AsyncStateMachineAttribute" == attr.GetType().FullName);
        }

        public static bool IsAsyncOperation(Delegate @delegate)
        {
            return IsAsyncOperation(@delegate.Method);
        }

        /// <summary>
        /// Waits for pending asynchronous operations to complete, if appropriate,
        /// and returns a proper result of the invocation by unwrapping task results
        /// </summary>
        /// <param name="invocationResult">The raw result of the method invocation</param>
        /// <returns>The unwrapped result, if necessary</returns>
        public abstract object WaitForPendingOperationsToComplete(object invocationResult);

        public virtual void Dispose()
        { }

        private class AsyncVoidInvocationRegion : AsyncInvocationRegion
        {
            private readonly SynchronizationContext _previousContext;
            private readonly AsyncSynchronizationContext _currentContext;

            public AsyncVoidInvocationRegion()
            {
                _previousContext = SynchronizationContext.Current;
                _currentContext = new AsyncSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(_currentContext);
            }

            public override void Dispose()
            {
                SynchronizationContext.SetSynchronizationContext(_previousContext);
            }

            public override object WaitForPendingOperationsToComplete(object invocationResult)
            {
                try
                {
                    _currentContext.WaitForPendingOperationsToComplete();
                }
                catch (Exception e)
                {
                    ExceptionDispatchInfo.Capture(e).Throw();
                }

                return invocationResult;
            }
        }

        private class AsyncTaskInvocationRegion : AsyncInvocationRegion
        {
            private const string TaskWaitMethod = "Wait";
            private const string TaskResultProperty = "Result";
            private const string SystemAggregateException = "System.AggregateException";
            private const string InnerExceptionsProperty = "InnerExceptions";
            private const BindingFlags TaskResultPropertyBindingFlags = BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.Public;

            public override object WaitForPendingOperationsToComplete(object invocationResult)
            {
                try
                {
                    invocationResult.GetType().GetMethod(TaskWaitMethod, new Type[0]).Invoke(invocationResult, null);
                }
                catch (TargetInvocationException e)
                {
                    IList<Exception> innerExceptions = GetAllExceptions(e.InnerException);

                    ExceptionDispatchInfo.Capture(innerExceptions[0]).Throw();
                }

                PropertyInfo taskResultProperty = invocationResult.GetType().GetProperty(TaskResultProperty, TaskResultPropertyBindingFlags);

                return taskResultProperty != null ? taskResultProperty.GetValue(invocationResult, null) : invocationResult;
            }

            private static IList<Exception> GetAllExceptions(Exception exception)
            {
                if (SystemAggregateException.Equals(exception.GetType().FullName))
                    return (IList<Exception>)exception.GetType().GetProperty(InnerExceptionsProperty).GetValue(exception, null);

                return new Exception[] { exception };
            }
        }
    }
}
#endif