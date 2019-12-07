using System;
using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace XpandNugetStats{
    public static class MemoryCacheExtensions{
        private static readonly ConcurrentDictionary<IMemoryCache, ConcurrentDictionary<string, CancellationTokenSource>> Memories =
                new ConcurrentDictionary<IMemoryCache, ConcurrentDictionary<string, CancellationTokenSource>>();

        public static IObservable<T> GetOrCreate<T>(this IMemoryCache memoryCache,  string key,Func<ICacheEntry, IObservable<T>> factory,
            int timeout, Func<int, TimeSpan> time = null){

            return memoryCache.GetOrCreateAsync(key, entry => entry.CacheItem(memoryCache, key, factory(entry), timeout, time))
                .ToObservable();
        }

        public static void ClearCache(this IMemoryCache memoryCache, string key=null){
            if (Memories.TryGetValue(memoryCache, out var tokens)){
                if (key!=null){
                    if (tokens.TryGetValue(key, out var token)){
                        token.Cancel();
                    }
                }
                else{
                    foreach (var tokenKey in tokens.Keys){
                        if (tokens.TryGetValue(tokenKey, out var value)){
                            value.Cancel();
                        }
                    }
                }
            }
        }

        private static async Task<T> CacheItem<T>(this ICacheEntry cacheEntry, IMemoryCache memoryCache, string key,
            IObservable<T> factory, int timeout, Func<int, TimeSpan> time = null){
            time ??= (i => TimeSpan.FromMinutes(i));
            cacheEntry.AbsoluteExpirationRelativeToNow = time(timeout);
            var result = await factory;

            var cacheEntryOptions = new MemoryCacheEntryOptions();
            memoryCache.Set(key, result, cacheEntryOptions);
            cacheEntryOptions.RegisterPostEvictionCallback(async (keyArg, o, reason, state) =>
                await cacheEntry.CacheItem(memoryCache, key, factory, timeout, time));
            var tokenSources = Memories.GetOrAdd(memoryCache, cache => new ConcurrentDictionary<string, CancellationTokenSource>());
            var tokenSource = new CancellationTokenSource(time(timeout-5));
            var expirationToken = new CancellationChangeToken(tokenSource.Token);
            tokenSources.AddOrUpdate(key, s => tokenSource, (s, source) => tokenSource );
            cacheEntryOptions.AddExpirationToken(expirationToken);
            
            

            return result;
        }
    }
}