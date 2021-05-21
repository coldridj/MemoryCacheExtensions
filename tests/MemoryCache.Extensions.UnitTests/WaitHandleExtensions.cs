using System;
using System.Threading;
using System.Threading.Tasks;

namespace MemoryCache.Extensions.UnitTests
{
    public static class WaitHandleExtensions
    {
        public static Task<bool> WaitOneAsync(this WaitHandle waitHandle, 
            int timeoutMilliseconds,
            CancellationToken cancellationToken = default)
        {
            if (waitHandle == null)
            {
                throw new ArgumentNullException(nameof(waitHandle));
            }

            var tcs = new TaskCompletionSource<bool>();
            using var disposable = cancellationToken.Register(() => tcs.TrySetCanceled());
            var registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(
                waitHandle,
                callBack: (state, timedOut) => { tcs.TrySetResult(!timedOut); },
                state: null,
                millisecondsTimeOutInterval: timeoutMilliseconds,
                executeOnlyOnce: true);

            return tcs.Task.ContinueWith(antecedent =>
            {
                registeredWaitHandle.Unregister(waitObject: null);
                try
                {
                    return antecedent.Result;
                }
                catch
                {
                    return false;
                }
            }, cancellationToken);
        }
    }
}
