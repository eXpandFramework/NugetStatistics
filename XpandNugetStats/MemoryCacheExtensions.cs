using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace XpandNugetStats{
    
    public static class MemoryCacheExtensions{
        private static readonly ConcurrentDictionary<string,CancellationTokenSource> TokenSources=new ConcurrentDictionary<string, CancellationTokenSource>();


        public static IObservable<T> GetOrCreate<T>(this IMemoryCache memoryCache,  string key,Func<ICacheEntry, IObservable<T>> factory,
            int timeout, Func<int, TimeSpan> time = null){
            return Observable.FromAsync(() => memoryCache.GetOrCreateAsync(key,
                async entry => await entry.CacheItem(memoryCache, key, Observable.Defer(() => factory(entry)), timeout, time)));
        }

        public static void ClearCache(this IMemoryCache memoryCache){
            var cancellationTokenSources = TokenSources.Values.ToArray();
            TokenSources.Clear();
            foreach (var tokenSource in cancellationTokenSources){
                tokenSource.Cancel();
            }
        }

        private static async Task<T> CacheItem<T>(this ICacheEntry cacheEntry, IMemoryCache memoryCache, string key,
            IObservable<T> factory, int timeout, Func<int, TimeSpan> time = null){
            time ??= (i => TimeSpan.FromMinutes(i));
            cacheEntry.AbsoluteExpirationRelativeToNow = time(timeout);
            var result = await factory;
            var tokenSource = new CancellationTokenSource(time(timeout-5));
            TokenSources.TryAdd(key, tokenSource);
            var expirationToken = new CancellationChangeToken(tokenSource.Token);
            cacheEntry.SlidingExpiration=time(timeout);
            cacheEntry.ExpirationTokens.Add(expirationToken);
            cacheEntry.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration(){EvictionCallback =
                (o, value, reason, state) => {
                    memoryCache.GetOrCreate(key, entry => Observable.Defer(() => factory), timeout, time).ObserveOn(ImmediateScheduler.Instance)
                        .SubscribeOn(ImmediateScheduler.Instance).Subscribe();
                }});
            return result;
        }
    }
}