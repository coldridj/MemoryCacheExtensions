using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace MemoryCache.Extensions
{
    public static class MemoryCacheExtensions
    {
        internal static readonly KeyTask[] KeyTasks =
            new KeyTask[Math.Max(Environment.ProcessorCount * 32, 128)];

        internal class KeyTask
        {
            public object Key { get; }

            public TaskCompletionSource<object> TaskCompletionSource { get; }

            public KeyTask(object key)
            {
                Key = key;
                TaskCompletionSource = new TaskCompletionSource<object>();
            }
        }

        /// <summary>
        /// Tries to synchronize execution of the factory method on GetOrCreate which is subject to race conditions
        /// see discussion https://stackoverflow.com/questions/20149796/memorycache-thread-safety-is-locking-necessary/45825792#45825792
        /// </summary>
        /// <typeparam name="TItem"></typeparam>
        /// <param name="memoryCache"></param>
        /// <param name="key"></param>
        /// <param name="factory"></param>
        /// <returns></returns>
        public static Task<TItem> GetOrCreateSafeAsync<TItem>(this IMemoryCache memoryCache,
            object key, 
            Func<ICacheEntry, Task<TItem>> factory) 
            where TItem : class
        {
            #region argument validation

            if (memoryCache == null)
            {
                throw new ArgumentNullException(nameof(memoryCache));
            }

            if (key == null || (key is string notNullStringKey && string.IsNullOrWhiteSpace(notNullStringKey)))
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            #endregion

            return memoryCache.GetOrCreateSafeAsyncInternal(key, factory, 0);
        }

        private static async Task<TItem> GetOrCreateSafeAsyncInternal<TItem>(this IMemoryCache memoryCache,
            object key, 
            Func<ICacheEntry, Task<TItem>> factory,
            int recursionCount) 
            where TItem : class
        {
            // TODO: this exists to short cut a potential stack overflow exception or bucket exhaustion/
            if (recursionCount > 1)
            {
                // KeyTasks size is arbitrary as is the recursion limit and the implementation needs reviewing
                throw new InvalidOperationException("Too many hash collisions for unequal keys");
            }
            try
            {
                // if the cache already contains the key we don't need to execute the factory
                if (memoryCache.TryGetValue(key, out var result))
                {
                    return (TItem) result;
                }

                // compute the bucket index for the factory based on the key
                var bucketIndex = (uint) key.GetHashCode() % (uint) KeyTasks.Length;
                // if the bucket is empty, exchange the value with a new key task
                var keyTask = Interlocked.CompareExchange(ref KeyTasks[bucketIndex],
                    new KeyTask(key), null);
                // compare exchange returns the original value at location so keytask will be null if the bucket was empty
                if (keyTask == null)
                {
                    try
                    {
                        // get task from bucket
                        keyTask = KeyTasks[bucketIndex] 
                                  ?? throw new InvalidOperationException($"TaskLock {bucketIndex} is null: this should never happen");
                        using var entry = memoryCache.CreateEntry(key);
                        // expiration should be set by the factory lambda - we could make this an explicit requirement
                        entry.Value = await factory(entry);
                        // replace the bucket value with null, allow the key task to be garbage collected
                        Interlocked.CompareExchange(ref KeyTasks[bucketIndex], null, keyTask);
                        // set the result on the key task which will allow any other tasks waiting for factory completion to return
                        keyTask.TaskCompletionSource.SetResult(entry.Value);
                        return (TItem) entry.Value;
                    }
                    catch (Exception ex)
                    {
                        keyTask.TaskCompletionSource.SetException(ex);
                        throw;
                    }
                    finally
                    {
                        // make sure the key task is evicted in the event of an exception
                        Interlocked.CompareExchange(ref KeyTasks[bucketIndex], null, keyTask);
                    }
                }

                // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                if (keyTask == null)
                {
                    // Resharper/visual studio can't comprehend the interlocked behaviour and coalescing throw on line 72
                    throw new InvalidOperationException("This should be unreachable.");
                }

                if (keyTask.Key.Equals(key))
                {
                    return (TItem) await keyTask.TaskCompletionSource.Task;
                }
                if (keyTask.Key is string taskKey && key is string stringKey && taskKey.Equals(stringKey, StringComparison.Ordinal))
                {
                    // non-interned strings wont have reference equality so do an ordinal comparison if necessary
                    return (TItem)await keyTask.TaskCompletionSource.Task;
                }

                // hash collision, let's wait for the factory with colliding key to complete
                await keyTask.TaskCompletionSource.Task.ContinueWith(t =>
                {
                    // use continuation to suppress exception on the other factory task
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
                return await memoryCache.GetOrCreateSafeAsyncInternal(key, factory, ++recursionCount);
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }
}
