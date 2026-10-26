namespace Cysharp.Threading.Tasks
{
    /// <summary>
    /// Specifies where the code after an <c>await</c> of a <see cref="UniTask"/> or <see cref="UniTask{T}"/> runs when the task
    /// is still pending at the time it is awaited. Select it with <see cref="UniTask.ConfigureAwait(UniTaskContinuationContext)"/>.
    /// </summary>
    /// <remarks>
    /// A task that has already completed when it is awaited continues synchronously on the awaiting thread whatever the value;
    /// use <c>UniTask.SwitchToMainThread</c> or <see cref="UniTask.SwitchToThreadPool"/> to always switch threads.
    /// The value only applies to awaits: callbacks registered with <c>SourceOnCompleted</c> still run on the thread that completes the task.
    /// The tasks of the player loop waits (<c>UniTask.Yield</c>, <c>NextFrame</c>, <c>DelayFrame</c>, <c>Delay</c>, <c>WaitForSeconds</c>,
    /// <c>WaitForEndOfFrame</c>, <c>WaitForFixedUpdate</c>, <c>WaitUntil</c>, <c>WaitWhile</c>, <c>WaitUntilCanceled</c> and
    /// <c>WaitUntilValueChanged</c>) default to <see cref="MainThread"/>: they complete in the player loop, so their continuations run
    /// inline there instead of being queued back to the thread pool.
    /// </remarks>
    public enum UniTaskContinuationContext : byte
    {
        /// <summary>
        /// The default. Captures the context of the awaiting code when the await starts and resumes in it: the Unity main thread when
        /// awaiting on it, otherwise <see cref="System.Threading.SynchronizationContext.Current"/>, otherwise the thread pool.
        /// The continuation runs inline when the task completes in that context.
        /// </summary>
        CapturedContext = 0,

        /// <summary>
        /// Resumes on the Unity main thread: inline when the task completes on the main thread, otherwise in the next
        /// <c>PlayerLoopTiming.Update</c> like <c>UniTask.SwitchToMainThread</c>. Outside Unity it behaves like <see cref="CapturedContext"/>.
        /// </summary>
        MainThread = 1,

        /// <summary>
        /// Resumes on a thread pool thread: inline when the task completes on a thread pool thread, otherwise queued to the thread pool.
        /// </summary>
        ThreadPool = 2,
    }
}
