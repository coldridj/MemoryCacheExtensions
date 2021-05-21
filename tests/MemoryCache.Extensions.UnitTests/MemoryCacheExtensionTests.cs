using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace MemoryCache.Extensions.UnitTests
{
    public class MemoryCacheExtensionTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public MemoryCacheExtensionTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        public async Task GetOrCreateSafeAsyncThrowsWithNullMemoryCache()
        {
            IMemoryCache memoryCache = null;
            var key = new object();
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                memoryCache.GetOrCreateSafeAsync<object>(key, (cacheEntry) => new Task<object>(() => 0)));
            Assert.Equal("memoryCache", ex.ParamName);
        }

        [Fact]
        public async Task GetOrCreateSafeAsyncThrowsWithNullObjectKey()
        {
            var memoryCache = new Mock<IMemoryCache>().Object;
            object key = null;
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                memoryCache.GetOrCreateSafeAsync<object>(key, (cacheEntry) => new Task<object>(() => 0)));
            Assert.Equal("key", ex.ParamName);
        }

        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [Theory]
        public async Task GetOrCreateSafeAsyncThrowsWithInvalidStringKey(string key)
        {
            var memoryCache = new Mock<IMemoryCache>().Object;
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                memoryCache.GetOrCreateSafeAsync<object>(key, (cacheEntry) => new Task<object>(() => 0)));
            Assert.Equal("key", ex.ParamName);
        }

        [Fact]
        public async Task GetOrCreateSafeAsyncThrowsWithNullFactoryFunction()
        {
            var memoryCache = new Mock<IMemoryCache>().Object;
            var key = new object();
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                memoryCache.GetOrCreateSafeAsync<object>(key, null));
            Assert.Equal("factory", ex.ParamName);
        }

        [Fact]
        public async Task MultipleCallsWithSameObjectKeyExecuteFactoryOnce()
        {
            var waitHandle = new ManualResetEvent(false);
            var index = 0;
            const string expectedIndex = "1";
            var key = new object();

            var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(Options.Create(new MemoryCacheOptions()));
            var taskFactory = new Func<Task<string>>(() =>
            {
                var task = new Func<ICacheEntry, Task<string>>(cacheEntry =>
                {
                    return waitHandle.WaitOneAsync(-1).ContinueWith(t => (++index).ToString());
                });
                return memoryCache.GetOrCreateSafeAsync(key, task);
            });

            var tasks = new[] { taskFactory(), taskFactory() };
            var results = await Task.WhenAll(tasks.Concat(new[] {
                Task.Delay(100).ContinueWith(t => waitHandle.Set().ToString())
            }));

            Assert.All(results.Take(tasks.Length),
                s => Assert.Equal(expectedIndex, s));
        }

        [Fact]
        public async Task MultipleCallsWithEqualObjectKeyExecuteFactoryOnce()
        {
            var waitHandle = new ManualResetEvent(false);
            var index = 0;
            const string expectedIndex = "1";
            Func<object> keyFactory = () => new TestKey(12345, true);

            var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(Options.Create(new MemoryCacheOptions()));
            var taskFactory = new Func<Task<string>>(() =>
            {
                var task = new Func<ICacheEntry, Task<string>>(cacheEntry =>
                {
                    return waitHandle.WaitOneAsync(-1).ContinueWith(t => (++index).ToString());
                });
                return memoryCache.GetOrCreateSafeAsync(keyFactory(), task);
            });

            var tasks = new[] { taskFactory(), taskFactory() };
            var results = await Task.WhenAll(tasks.Concat(new[] {
                Task.Delay(100).ContinueWith(t => waitHandle.Set().ToString())
            }));

            Assert.All(results.Take(tasks.Length),
                s => Assert.Equal(expectedIndex, s));
        }

        [Fact]
        public async Task MultipleCallsWithInternedStringKeyExecuteFactoryOnce()
        {
            var waitHandle = new ManualResetEvent(false);
            var index = 0;
            const string expectedIndex = "1";
            var key = "key";

            var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(Options.Create(new MemoryCacheOptions()));
            var taskFactory = new Func<Task<string>>(() =>
            {
                var task = new Func<ICacheEntry, Task<string>>(cacheEntry =>
                {
                    return waitHandle.WaitOneAsync(-1).ContinueWith(t => (++index).ToString());
                });
                return memoryCache.GetOrCreateSafeAsync(key, task);
            });

            var tasks = new[] { taskFactory(), taskFactory() };
            var results = await Task.WhenAll(tasks.Concat(new[] {
                Task.Delay(100).ContinueWith(t => waitHandle.Set().ToString())
            }));

            Assert.All(results.Take(tasks.Length),
                s => Assert.Equal(expectedIndex, s));
        }

        [Fact]
        public async Task MultipleCallsWithNonInternedStringKeyExecuteFactoryOnce()
        {
            var waitHandle = new ManualResetEvent(false);
            var index = 0;
            const string expectedIndex = "1";
            // this must capture a variable within its closure or the compiler will optimize and intern the string
            Func<object> keyFactory = () => $"{index}:key";

            var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(Options.Create(new MemoryCacheOptions()));
            var taskFactory = new Func<Task<string>>(() =>
            {
                var task = new Func<ICacheEntry, Task<string>>(cacheEntry =>
                {
                    return waitHandle.WaitOneAsync(-1).ContinueWith(t => (++index).ToString());
                });
                return memoryCache.GetOrCreateSafeAsync(keyFactory(), task);
            });

            var tasks = new[] { taskFactory(), taskFactory() };
            var results = await Task.WhenAll(tasks.Concat(new[] {
                Task.Delay(100).ContinueWith(t => waitHandle.Set().ToString())
            }));

            Assert.All(results.Take(tasks.Length),
                s => Assert.Equal(expectedIndex, s));
        }

        [Fact]
        public async Task CacheWithExistingItemSkipsFactory()
        {
            var index = 0;
            var expectedIndex = index.ToString();
            var key = new object();
            var task = new Func<ICacheEntry, Task<string>>(cacheEntry => Task.FromResult((++index).ToString()));

            var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(Options.Create(new MemoryCacheOptions()));
            memoryCache.Set(key, expectedIndex);

            var item = await memoryCache.GetOrCreateSafeAsync<string>(key, task);

            Assert.Equal(expectedIndex, item);
            Assert.Equal(0, index);
        }

        [Fact]
        public async Task SingleThreadFactorySucceeds()
        {
            var index = 0;
            var key = new object();
            var task = new Func<ICacheEntry, Task<string>>(cacheEntry => Task.FromResult((++index).ToString()));
            var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(Options.Create(new MemoryCacheOptions()));

            var item = await memoryCache.GetOrCreateSafeAsync<string>(key, task);

            Assert.Equal("1", item);
            Assert.Equal(1, index);
        }

        [Fact]
        public async Task CallWithHashCollisionExecuteBothFactories()
        {
            var waitHandle = new ManualResetEvent(false);
            var index = 0;
            var keyFactory =  new Func<object>(() => new TestKey(12345, false));

            var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(Options.Create(new MemoryCacheOptions()));
            var taskFactory = new Func<Task<string>>(() =>
            {
                var task = new Func<ICacheEntry, Task<string>>(cacheEntry =>
                {
                    return waitHandle.WaitOneAsync(-1).ContinueWith(t => (++index).ToString());
                });
                return memoryCache.GetOrCreateSafeAsync(keyFactory(), task);
            });

            var tasks = new[] {taskFactory(), taskFactory()};
            var results = await Task.WhenAll(tasks.Concat(new[] {
                Task.Delay(100).ContinueWith(t => waitHandle.Set().ToString())
            }));

            Assert.Equal("1", results[0]);
            Assert.Equal("2", results[1]);
        }

        [Fact]
        public async Task CallWithRepeatedHashCollisionsThrows()
        {
            var waitHandle = new ManualResetEvent(false);
            var index = 0;
            var keyFactory = new Func<object>(() => new TestKey(12345, false));

            var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(Options.Create(new MemoryCacheOptions()));
            var taskFactory = new Func<Task<string>>(() =>
            {
                var task = new Func<ICacheEntry, Task<string>>(cacheEntry =>
                {
                    return waitHandle.WaitOneAsync(-1).ContinueWith(t =>
                    {
                        return (++index).ToString();
                    });
                });
                return memoryCache.GetOrCreateSafeAsync(keyFactory(), task);
            });

            var tasks = new[] { taskFactory(), taskFactory(), taskFactory() };
            await Assert.ThrowsAsync<InvalidOperationException>(() => Task.WhenAll(tasks.Concat(new[] {
                Task.Delay(100).ContinueWith(t => waitHandle.Set().ToString())
            })));
        }

        [Fact]
        public async Task CallWithHashCollisionSurvivesExceptionInFirstFactory()
        {
            var waitHandle = new ManualResetEvent(false);
            var index = 0;
            var keyFactory = new Func<object>(() => new TestKey(12345, false));
            var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(Options.Create(new MemoryCacheOptions()));

            const string expectedException = "First factory exception";

            var firstFactory = new Func<Task<string>>(() =>
            {
                var task = new Func<ICacheEntry, Task<string>>(async cacheEntry =>
                {
                    await waitHandle.WaitOneAsync(-1);
                    throw new Exception(expectedException);
                });
                return memoryCache.GetOrCreateSafeAsync(keyFactory(), task);
            });

            var secondFactory = new Func<Task<string>>(() =>
            {
                var task = new Func<ICacheEntry, Task<string>>(cacheEntry =>
                {
                    return waitHandle.WaitOneAsync(-1).ContinueWith(t =>
                    {
                        return (++index).ToString();
                    });
                });
                return memoryCache.GetOrCreateSafeAsync(keyFactory(), task);
            });

            var secondFactoryTask = secondFactory();
            var tasks = new[] { firstFactory(), secondFactoryTask };
            var ex = await Assert.ThrowsAsync<Exception>(()=> Task.WhenAll(tasks.Concat(new[] {
                Task.Delay(1000).ContinueWith(t =>
                {
                    return waitHandle.Set().ToString();
                })
            })));
            Assert.Equal(expectedException, ex.Message);
            Assert.Equal("1", secondFactoryTask.Result);
        }


        private class TestKey
        {
            private readonly int _hashCode;
            private readonly bool _equal;

            public TestKey(int hashCode, bool equal)
            {
                _hashCode = hashCode;
                _equal = equal;
            }
            
            public override int GetHashCode()
            {
                return _hashCode;
            }

            public override bool Equals(object obj)
            {
                return _equal;
            }
        }
    }
}
