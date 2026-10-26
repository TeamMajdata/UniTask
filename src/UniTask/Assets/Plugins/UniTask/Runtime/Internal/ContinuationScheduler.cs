using System;
using System.Diagnostics;
using System.Threading;

namespace Cysharp.Threading.Tasks.Internal
{
    /// <summary>
    /// Registers the continuations of awaits of <see cref="UniTask"/> and <see cref="UniTask{T}"/> so that they resume in the
    /// context selected by <see cref="UniTaskContinuationContext"/>.
    /// </summary>
    internal static class ContinuationScheduler
    {
#if UNITY_2018_3_OR_NEWER
        static readonly Action<object> resumeOnMainThreadDelegate = ResumeOnMainThread;
#endif
        static readonly Action<object> resumeOnThreadPoolDelegate = ResumeOnThreadPool;
        static readonly Action<object> resumeOnSynchronizationContextDelegate = ResumeOnSynchronizationContext;
        static readonly WaitCallback invokeOnThreadPoolCallback = InvokeContinuation;
        static readonly SendOrPostCallback invokeOnSynchronizationContextCallback = InvokeContinuation;

        /// <summary>
        /// Registers the continuation of an await on a pending source. Must be called on the awaiting thread, because
        /// <see cref="UniTaskContinuationContext.CapturedContext"/> captures that thread's context here.
        /// </summary>
        /// <param name="source">The pending source of the awaited task.</param>
        /// <param name="token">The token of the awaited task.</param>
        /// <param name="continuationContext">Where the continuation has to run.</param>
        /// <param name="continuation">The continuation of the await, usually the state machine's <c>MoveNext</c>.</param>
        /// <exception cref="InvalidOperationException">The source rejects the registration, for example because the task is awaited twice.</exception>
        [DebuggerHidden]
        public static void OnCompleted(IUniTaskSource source, short token, UniTaskContinuationContext continuationContext, Action continuation)
        {
            switch (continuationContext)
            {
#if UNITY_2018_3_OR_NEWER
                case UniTaskContinuationContext.MainThread:
                    source.OnCompleted(resumeOnMainThreadDelegate, continuation, token);
                    break;
#endif
                case UniTaskContinuationContext.ThreadPool:
                    source.OnCompleted(resumeOnThreadPoolDelegate, continuation, token);
                    break;
                default:
                    OnCompletedInCapturedContext(source, token, continuation);
                    break;
            }
        }

        /// <summary>Captures the context of the awaiting thread and registers a continuation that resumes in it.</summary>
        /// <param name="source">The pending source of the awaited task.</param>
        /// <param name="token">The token of the awaited task.</param>
        /// <param name="continuation">The continuation of the await.</param>
        [DebuggerHidden]
        static void OnCompletedInCapturedContext(IUniTaskSource source, short token, Action continuation)
        {
            var synchronizationContext = SynchronizationContext.Current;
#if UNITY_2018_3_OR_NEWER
            // The main thread without a context, or with the context PlayerLoopHelper captured at startup (which only runs work on
            // the main thread), resumes on the main thread without allocating per-await state.
            var isMainThreadContext = synchronizationContext == null
                ? PlayerLoopHelper.IsMainThread
                : synchronizationContext == PlayerLoopHelper.UnitySynchronizationContext;
            if (isMainThreadContext)
            {
                source.OnCompleted(resumeOnMainThreadDelegate, continuation, token);
                return;
            }
#endif
            if (synchronizationContext == null)
            {
                source.OnCompleted(resumeOnThreadPoolDelegate, continuation, token);
            }
            else
            {
                var state = SynchronizationContextContinuation.Create(synchronizationContext, continuation);
                source.OnCompleted(resumeOnSynchronizationContextDelegate, state, token);
            }
        }

#if UNITY_2018_3_OR_NEWER
        /// <summary>Runs a continuation inline on the main thread, or queues it to the next <see cref="PlayerLoopTiming.Update"/>.</summary>
        /// <param name="state">The continuation.</param>
        [DebuggerHidden]
        static void ResumeOnMainThread(object state)
        {
            var continuation = (Action)state;
            if (PlayerLoopHelper.IsMainThread)
            {
                continuation();
            }
            else
            {
                PlayerLoopHelper.AddContinuation(PlayerLoopTiming.Update, continuation);
            }
        }
#endif

        /// <summary>Runs a continuation inline on a thread pool thread, or queues it to the thread pool.</summary>
        /// <param name="state">The continuation.</param>
        [DebuggerHidden]
        static void ResumeOnThreadPool(object state)
        {
            if (Thread.CurrentThread.IsThreadPoolThread)
            {
                ((Action)state).Invoke();
            }
            else
            {
                ThreadPool.UnsafeQueueUserWorkItem(invokeOnThreadPoolCallback, state);
            }
        }

        /// <summary>Runs a continuation in the synchronization context captured with it.</summary>
        /// <param name="state">The <see cref="SynchronizationContextContinuation"/> created when the await started.</param>
        [DebuggerHidden]
        static void ResumeOnSynchronizationContext(object state)
        {
            ((SynchronizationContextContinuation)state).Resume();
        }

        /// <summary>Invokes a continuation passed as callback state.</summary>
        /// <param name="state">The continuation.</param>
        [DebuggerHidden]
        static void InvokeContinuation(object state)
        {
            ((Action)state).Invoke();
        }

        /// <summary>A pooled pair of a captured synchronization context and the continuation that has to resume in it.</summary>
        sealed class SynchronizationContextContinuation : ITaskPoolNode<SynchronizationContextContinuation>
        {
            static TaskPool<SynchronizationContextContinuation> pool;

            SynchronizationContextContinuation nextNode;

            /// <summary>Gets the next node of the pool's free list.</summary>
            public ref SynchronizationContextContinuation NextNode => ref nextNode;

            SynchronizationContext synchronizationContext;
            Action continuation;

            static SynchronizationContextContinuation()
            {
                TaskPool.RegisterSizeGetter(typeof(SynchronizationContextContinuation), () => pool.Size);
            }

            SynchronizationContextContinuation()
            {
            }

            /// <summary>Gets a pooled instance holding a context and a continuation.</summary>
            /// <param name="synchronizationContext">The captured context.</param>
            /// <param name="continuation">The continuation that has to resume in <paramref name="synchronizationContext"/>.</param>
            /// <returns>The instance; <see cref="Resume"/> returns it to the pool.</returns>
            public static SynchronizationContextContinuation Create(SynchronizationContext synchronizationContext, Action continuation)
            {
                if (!pool.TryPop(out var result))
                {
                    result = new SynchronizationContextContinuation();
                }

                result.synchronizationContext = synchronizationContext;
                result.continuation = continuation;
                return result;
            }

            /// <summary>
            /// Returns this instance to the pool, then runs the continuation inline when the current context is the captured one,
            /// or posts it to the captured context otherwise.
            /// </summary>
            [DebuggerHidden]
            public void Resume()
            {
                var context = synchronizationContext;
                var action = continuation;
                synchronizationContext = null;
                continuation = null;
                pool.TryPush(this);

                if (SynchronizationContext.Current == context)
                {
                    action();
                }
                else
                {
                    context.Post(invokeOnSynchronizationContextCallback, action);
                }
            }
        }
    }
}
