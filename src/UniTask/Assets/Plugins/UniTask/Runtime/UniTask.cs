#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS0436

#if UNITASK_NETCORE || UNITY_2022_3_OR_NEWER
#define SUPPORT_VALUETASK
#endif

using Cysharp.Threading.Tasks.CompilerServices;
using Cysharp.Threading.Tasks.Internal;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Cysharp.Threading.Tasks
{
    /// <summary>
    /// Lightweight unity specified task-like object.
    /// </summary>
    [AsyncMethodBuilder(typeof(AsyncUniTaskMethodBuilder))]
    [StructLayout(LayoutKind.Auto)]
    public readonly partial struct UniTask
    {
        readonly IUniTaskSource source;
        readonly short token;
        readonly UniTaskContinuationContext continuationContext;

        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask(IUniTaskSource source, short token)
        {
            this.source = source;
            this.token = token;
            this.continuationContext = UniTaskContinuationContext.CapturedContext;
        }

        /// <summary>
        /// Creates a task over a source whose awaits resume in the given context.
        /// </summary>
        /// <param name="source">The source that completes the task, or <c>null</c> for a completed task.</param>
        /// <param name="token">The token the source validates.</param>
        /// <param name="continuationContext">Where the code after an await of this task runs when the task is still pending.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="continuationContext"/> is not a defined value.</exception>
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask(IUniTaskSource source, short token, UniTaskContinuationContext continuationContext)
        {
            if ((uint)continuationContext > (uint)UniTaskContinuationContext.ThreadPool)
            {
                throw Error.ArgumentOutOfRange(nameof(continuationContext));
            }

            this.source = source;
            this.token = token;
            this.continuationContext = continuationContext;
        }

        public UniTaskStatus Status
        {
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (source == null) return UniTaskStatus.Succeeded;
                return source.GetStatus(token);
            }
        }

        /// <summary>
        /// Gets where the code after an await of this task runs when the task is still pending.
        /// <see cref="UniTaskContinuationContext.CapturedContext"/> unless changed with <see cref="ConfigureAwait(UniTaskContinuationContext)"/>.
        /// </summary>
        public UniTaskContinuationContext ContinuationContext
        {
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return continuationContext;
            }
        }

        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Awaiter GetAwaiter()
        {
            return new Awaiter(this);
        }

        /// <summary>
        /// Returns this task with awaits resuming in the given context. Call it last, right before awaiting: tasks created from the
        /// result by other operators or async methods resume in <see cref="UniTaskContinuationContext.CapturedContext"/> again.
        /// </summary>
        /// <param name="continuationContext">Where the code after an await of the returned task runs when the task is still pending.</param>
        /// <returns>A task over the same source, with the given continuation context.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="continuationContext"/> is not a defined value.</exception>
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask ConfigureAwait(UniTaskContinuationContext continuationContext)
        {
            return new UniTask(source, token, continuationContext);
        }

        /// <summary>
        /// returns (bool IsCanceled) instead of throws OperationCanceledException.
        /// </summary>
        public UniTask<bool> SuppressCancellationThrow()
        {
            var status = Status;
            if (status == UniTaskStatus.Succeeded) return CompletedTasks.False.ConfigureAwait(continuationContext);
            if (status == UniTaskStatus.Canceled) return CompletedTasks.True.ConfigureAwait(continuationContext);
            return new UniTask<bool>(new IsCanceledSource(source), token, continuationContext);
        }

#if SUPPORT_VALUETASK

        public static implicit operator System.Threading.Tasks.ValueTask(in UniTask self)
        {
            if (self.source == null)
            {
                return default;
            }

#if (UNITASK_NETCORE && NETSTANDARD2_0)
            return self.AsValueTask();
#else
            return new System.Threading.Tasks.ValueTask(self.source, self.token);
#endif
        }

#endif

        public override string ToString()
        {
            if (source == null) return "()";
            return "(" + source.UnsafeGetStatus() + ")";
        }

        /// <summary>
        /// Memoizing inner IValueTaskSource. The result UniTask can await multiple.
        /// </summary>
        public UniTask Preserve()
        {
            if (source == null)
            {
                return this;
            }
            else
            {
                return new UniTask(new MemoizeSource(source), token, continuationContext);
            }
        }

        public UniTask<AsyncUnit> AsAsyncUnitUniTask()
        {
            if (this.source == null) return CompletedTasks.AsyncUnit.ConfigureAwait(this.continuationContext);

            var status = this.source.GetStatus(this.token);
            if (status.IsCompletedSuccessfully())
            {
                this.source.GetResult(this.token);
                return CompletedTasks.AsyncUnit.ConfigureAwait(this.continuationContext);
            }
            else if (this.source is IUniTaskSource<AsyncUnit> asyncUnitSource)
            {
                return new UniTask<AsyncUnit>(asyncUnitSource, this.token, this.continuationContext);
            }

            return new UniTask<AsyncUnit>(new AsyncUnitSource(this.source), this.token, this.continuationContext);
        }

        sealed class AsyncUnitSource : IUniTaskSource<AsyncUnit>
        {
            readonly IUniTaskSource source;

            public AsyncUnitSource(IUniTaskSource source)
            {
                this.source = source;
            }

            public AsyncUnit GetResult(short token)
            {
                source.GetResult(token);
                return AsyncUnit.Default;
            }

            public UniTaskStatus GetStatus(short token)
            {
                return source.GetStatus(token);
            }

            public void OnCompleted(Action<object> continuation, object state, short token)
            {
                source.OnCompleted(continuation, state, token);
            }

            public UniTaskStatus UnsafeGetStatus()
            {
                return source.UnsafeGetStatus();
            }

            void IUniTaskSource.GetResult(short token)
            {
                GetResult(token);
            }
        }

        sealed class IsCanceledSource : IUniTaskSource<bool>
        {
            readonly IUniTaskSource source;

            public IsCanceledSource(IUniTaskSource source)
            {
                this.source = source;
            }

            public bool GetResult(short token)
            {
                if (source.GetStatus(token) == UniTaskStatus.Canceled)
                {
                    return true;
                }

                source.GetResult(token);
                return false;
            }

            void IUniTaskSource.GetResult(short token)
            {
                GetResult(token);
            }

            public UniTaskStatus GetStatus(short token)
            {
                return source.GetStatus(token);
            }

            public UniTaskStatus UnsafeGetStatus()
            {
                return source.UnsafeGetStatus();
            }

            public void OnCompleted(Action<object> continuation, object state, short token)
            {
                source.OnCompleted(continuation, state, token);
            }
        }

        sealed class MemoizeSource : IUniTaskSource
        {
            IUniTaskSource source;
            ExceptionDispatchInfo exception;
            UniTaskStatus status;

            public MemoizeSource(IUniTaskSource source)
            {
                this.source = source;
            }

            public void GetResult(short token)
            {
                if (source == null)
                {
                    if (exception != null)
                    {
                        exception.Throw();
                    }
                }
                else
                {
                    try
                    {
                        source.GetResult(token);
                        status = UniTaskStatus.Succeeded;
                    }
                    catch (Exception ex)
                    {
                        exception = ExceptionDispatchInfo.Capture(ex);
                        if (ex is OperationCanceledException)
                        {
                            status = UniTaskStatus.Canceled;
                        }
                        else
                        {
                            status = UniTaskStatus.Faulted;
                        }
                        throw;
                    }
                    finally
                    {
                        source = null;
                    }
                }
            }

            public UniTaskStatus GetStatus(short token)
            {
                if (source == null)
                {
                    return status;
                }

                return source.GetStatus(token);
            }

            public void OnCompleted(Action<object> continuation, object state, short token)
            {
                if (source == null)
                {
                    continuation(state);
                }
                else
                {
                    source.OnCompleted(continuation, state, token);
                }
            }

            public UniTaskStatus UnsafeGetStatus()
            {
                if (source == null)
                {
                    return status;
                }

                return source.UnsafeGetStatus();
            }
        }

        public readonly struct Awaiter : ICriticalNotifyCompletion
        {
            readonly UniTask task;

            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Awaiter(in UniTask task)
            {
                this.task = task;
            }

            public bool IsCompleted
            {
                [DebuggerHidden]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return task.Status.IsCompleted();
                }
            }

            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void GetResult()
            {
                if (task.source == null) return;
                task.source.GetResult(task.token);
            }

            /// <summary>
            /// Schedules the continuation of an await. It resumes in the task's <see cref="UniTask.ContinuationContext"/>,
            /// captured on the calling thread for <see cref="UniTaskContinuationContext.CapturedContext"/>.
            /// </summary>
            /// <param name="continuation">The code to run after the task completes.</param>
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void OnCompleted(Action continuation)
            {
                if (task.source == null)
                {
                    continuation();
                }
                else
                {
                    ContinuationScheduler.OnCompleted(task.source, task.token, task.continuationContext, continuation);
                }
            }

            /// <summary>
            /// Schedules the continuation of an await. It resumes in the task's <see cref="UniTask.ContinuationContext"/>,
            /// captured on the calling thread for <see cref="UniTaskContinuationContext.CapturedContext"/>.
            /// </summary>
            /// <param name="continuation">The code to run after the task completes.</param>
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void UnsafeOnCompleted(Action continuation)
            {
                if (task.source == null)
                {
                    continuation();
                }
                else
                {
                    ContinuationScheduler.OnCompleted(task.source, task.token, task.continuationContext, continuation);
                }
            }

            /// <summary>
            /// If register manually continuation, you can use it instead of for compiler OnCompleted methods.
            /// </summary>
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SourceOnCompleted(Action<object> continuation, object state)
            {
                if (task.source == null)
                {
                    continuation(state);
                }
                else
                {
                    task.source.OnCompleted(continuation, state, task.token);
                }
            }
        }
    }

    /// <summary>
    /// Lightweight unity specified task-like object.
    /// </summary>
    [AsyncMethodBuilder(typeof(AsyncUniTaskMethodBuilder<>))]
    [StructLayout(LayoutKind.Auto)]
    public readonly struct UniTask<T>
    {
        readonly IUniTaskSource<T> source;
        readonly T result;
        readonly short token;
        readonly UniTaskContinuationContext continuationContext;

        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask(T result)
        {
            this.source = default;
            this.token = default;
            this.result = result;
            this.continuationContext = UniTaskContinuationContext.CapturedContext;
        }

        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask(IUniTaskSource<T> source, short token)
        {
            this.source = source;
            this.token = token;
            this.result = default;
            this.continuationContext = UniTaskContinuationContext.CapturedContext;
        }

        /// <summary>
        /// Creates a task over a source whose awaits resume in the given context.
        /// </summary>
        /// <param name="source">The source that completes the task.</param>
        /// <param name="token">The token the source validates.</param>
        /// <param name="continuationContext">Where the code after an await of this task runs when the task is still pending.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="continuationContext"/> is not a defined value.</exception>
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask(IUniTaskSource<T> source, short token, UniTaskContinuationContext continuationContext)
            : this(source, default, token, continuationContext)
        {
        }

        /// <summary>
        /// Creates a task from all of its fields.
        /// </summary>
        /// <param name="source">The source that completes the task, or <c>null</c> for a task completed with <paramref name="result"/>.</param>
        /// <param name="result">The result of a completed task; ignored when <paramref name="source"/> is set.</param>
        /// <param name="token">The token the source validates.</param>
        /// <param name="continuationContext">Where the code after an await of this task runs when the task is still pending.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="continuationContext"/> is not a defined value.</exception>
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        UniTask(IUniTaskSource<T> source, T result, short token, UniTaskContinuationContext continuationContext)
        {
            if ((uint)continuationContext > (uint)UniTaskContinuationContext.ThreadPool)
            {
                throw Error.ArgumentOutOfRange(nameof(continuationContext));
            }

            this.source = source;
            this.token = token;
            this.result = result;
            this.continuationContext = continuationContext;
        }

        public UniTaskStatus Status
        {
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return (source == null) ? UniTaskStatus.Succeeded : source.GetStatus(token);
            }
        }

        /// <summary>
        /// Gets where the code after an await of this task runs when the task is still pending.
        /// <see cref="UniTaskContinuationContext.CapturedContext"/> unless changed with <see cref="ConfigureAwait(UniTaskContinuationContext)"/>.
        /// </summary>
        public UniTaskContinuationContext ContinuationContext
        {
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return continuationContext;
            }
        }

        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Awaiter GetAwaiter()
        {
            return new Awaiter(this);
        }

        /// <summary>
        /// Returns this task with awaits resuming in the given context. Call it last, right before awaiting: tasks created from the
        /// result by other operators or async methods resume in <see cref="UniTaskContinuationContext.CapturedContext"/> again.
        /// </summary>
        /// <param name="continuationContext">Where the code after an await of the returned task runs when the task is still pending.</param>
        /// <returns>A task over the same source or result, with the given continuation context.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="continuationContext"/> is not a defined value.</exception>
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask<T> ConfigureAwait(UniTaskContinuationContext continuationContext)
        {
            return new UniTask<T>(source, result, token, continuationContext);
        }

        /// <summary>
        /// Memoizing inner IValueTaskSource. The result UniTask can await multiple.
        /// </summary>
        public UniTask<T> Preserve()
        {
            if (source == null)
            {
                return this;
            }
            else
            {
                return new UniTask<T>(new MemoizeSource(source), token, continuationContext);
            }
        }

        public UniTask AsUniTask()
        {
            if (this.source == null) return UniTask.CompletedTask.ConfigureAwait(this.continuationContext);

            var status = this.source.GetStatus(this.token);
            if (status.IsCompletedSuccessfully())
            {
                this.source.GetResult(this.token);
                return UniTask.CompletedTask.ConfigureAwait(this.continuationContext);
            }

            // Converting UniTask<T> -> UniTask is zero overhead.
            return new UniTask(this.source, this.token, this.continuationContext);
        }

        public static implicit operator UniTask(UniTask<T> self)
        {
            return self.AsUniTask();
        }

#if SUPPORT_VALUETASK

        public static implicit operator System.Threading.Tasks.ValueTask<T>(in UniTask<T> self)
        {
            if (self.source == null)
            {
                return new System.Threading.Tasks.ValueTask<T>(self.result);
            }

#if (UNITASK_NETCORE && NETSTANDARD2_0)
            return self.AsValueTask();
#else
            return new System.Threading.Tasks.ValueTask<T>(self.source, self.token);
#endif
        }

#endif

        /// <summary>
        /// returns (bool IsCanceled, T Result) instead of throws OperationCanceledException.
        /// </summary>
        public UniTask<(bool IsCanceled, T Result)> SuppressCancellationThrow()
        {
            if (source == null)
            {
                return new UniTask<(bool IsCanceled, T Result)>((false, result)).ConfigureAwait(continuationContext);
            }

            return new UniTask<(bool, T)>(new IsCanceledSource(source), token, continuationContext);
        }

        public override string ToString()
        {
            return (this.source == null) ? result?.ToString()
                 : "(" + this.source.UnsafeGetStatus() + ")";
        }

        sealed class IsCanceledSource : IUniTaskSource<(bool, T)>
        {
            readonly IUniTaskSource<T> source;

            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public IsCanceledSource(IUniTaskSource<T> source)
            {
                this.source = source;
            }

            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public (bool, T) GetResult(short token)
            {
                if (source.GetStatus(token) == UniTaskStatus.Canceled)
                {
                    return (true, default);
                }

                var result = source.GetResult(token);
                return (false, result);
            }

            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void IUniTaskSource.GetResult(short token)
            {
                GetResult(token);
            }

            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public UniTaskStatus GetStatus(short token)
            {
                return source.GetStatus(token);
            }

            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public UniTaskStatus UnsafeGetStatus()
            {
                return source.UnsafeGetStatus();
            }

            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void OnCompleted(Action<object> continuation, object state, short token)
            {
                source.OnCompleted(continuation, state, token);
            }
        }

        sealed class MemoizeSource : IUniTaskSource<T>
        {
            IUniTaskSource<T> source;
            T result;
            ExceptionDispatchInfo exception;
            UniTaskStatus status;

            public MemoizeSource(IUniTaskSource<T> source)
            {
                this.source = source;
            }

            public T GetResult(short token)
            {
                if (source == null)
                {
                    if (exception != null)
                    {
                        exception.Throw();
                    }
                    return result;
                }
                else
                {
                    try
                    {
                        result = source.GetResult(token);
                        status = UniTaskStatus.Succeeded;
                        return result;
                    }
                    catch (Exception ex)
                    {
                        exception = ExceptionDispatchInfo.Capture(ex);
                        if (ex is OperationCanceledException)
                        {
                            status = UniTaskStatus.Canceled;
                        }
                        else
                        {
                            status = UniTaskStatus.Faulted;
                        }
                        throw;
                    }
                    finally
                    {
                        source = null;
                    }
                }
            }

            void IUniTaskSource.GetResult(short token)
            {
                GetResult(token);
            }

            public UniTaskStatus GetStatus(short token)
            {
                if (source == null)
                {
                    return status;
                }

                return source.GetStatus(token);
            }

            public void OnCompleted(Action<object> continuation, object state, short token)
            {
                if (source == null)
                {
                    continuation(state);
                }
                else
                {
                    source.OnCompleted(continuation, state, token);
                }
            }

            public UniTaskStatus UnsafeGetStatus()
            {
                if (source == null)
                {
                    return status;
                }

                return source.UnsafeGetStatus();
            }
        }

        public readonly struct Awaiter : ICriticalNotifyCompletion
        {
            readonly UniTask<T> task;

            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Awaiter(in UniTask<T> task)
            {
                this.task = task;
            }

            public bool IsCompleted
            {
                [DebuggerHidden]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return task.Status.IsCompleted();
                }
            }

            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T GetResult()
            {
                var s = task.source;
                if (s == null)
                {
                    return task.result;
                }
                else
                {
                    return s.GetResult(task.token);
                }
            }

            /// <summary>
            /// Schedules the continuation of an await. It resumes in the task's <see cref="UniTask{T}.ContinuationContext"/>,
            /// captured on the calling thread for <see cref="UniTaskContinuationContext.CapturedContext"/>.
            /// </summary>
            /// <param name="continuation">The code to run after the task completes.</param>
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void OnCompleted(Action continuation)
            {
                var s = task.source;
                if (s == null)
                {
                    continuation();
                }
                else
                {
                    ContinuationScheduler.OnCompleted(s, task.token, task.continuationContext, continuation);
                }
            }

            /// <summary>
            /// Schedules the continuation of an await. It resumes in the task's <see cref="UniTask{T}.ContinuationContext"/>,
            /// captured on the calling thread for <see cref="UniTaskContinuationContext.CapturedContext"/>.
            /// </summary>
            /// <param name="continuation">The code to run after the task completes.</param>
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void UnsafeOnCompleted(Action continuation)
            {
                var s = task.source;
                if (s == null)
                {
                    continuation();
                }
                else
                {
                    ContinuationScheduler.OnCompleted(s, task.token, task.continuationContext, continuation);
                }
            }

            /// <summary>
            /// If register manually continuation, you can use it instead of for compiler OnCompleted methods.
            /// </summary>
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SourceOnCompleted(Action<object> continuation, object state)
            {
                var s = task.source;
                if (s == null)
                {
                    continuation(state);
                }
                else
                {
                    s.OnCompleted(continuation, state, task.token);
                }
            }
        }
    }
}

