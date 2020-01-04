using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpMux.Extensions
{
    public static class SemaphoreExtensions
    {
        public static async ValueTask<SemaphoreLock> TakeLock(this SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            return new SemaphoreLock(semaphore);
        }

        public struct SemaphoreLock : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;

            public SemaphoreLock(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }
            public void Dispose()
            {
                _semaphore.Release();
            }
        }
    }
}
