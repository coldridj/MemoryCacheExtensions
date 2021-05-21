# MemoryCacheExtensions

Extensions for [IMemoryCache](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.memory.imemorycache?view=dotnet-plat-ext-5.0&viewFallbackFrom=netstandard-2.0) to enhance functionality and work around know limitations in the default implementation

## GetOrCreateSafeAsync

```cs
public static async Task<TItem> GetOrCreateSafeAsync<TItem>(this IMemoryCache memoryCache, object key, Func<ICacheEntry, Task<TItem>> factory) where TItem : class
```

Tries to synchronize execution of the factory method on GetOrCreate which is subject to race conditions,
see discussion: https://stackoverflow.com/questions/20149796/memorycache-thread-safety-is-locking-necessary/45825792#45825792

Usage:
```cs 
return await _memoryCache.GetOrCreateSafeAsync(cacheKey, async cacheEntry =>
{
    var cacheItem = await _asyncFactory.GetAsync();
    cacheEntry.SetAbsoluteExpiration(DateTime.UtcNow.AddHours(1));
    return cacheItem;
});
```

- Recommendation is to use new object as cache key where appropriate (eg. cached singleton)
  
## Dependencies

- Microsoft.Extensions.Caching.Memory