using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    }

    public struct SemaphoreLock : IDisposable, IEquatable<SemaphoreLock>
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

        public override bool Equals(object? obj)
        {
            if (!(obj is SemaphoreLock)) { return false; }
            return Equals((SemaphoreLock)obj);
        }

        public bool Equals([AllowNull] SemaphoreLock other)
        {
            return _semaphore == other._semaphore;
        }

        public override int GetHashCode()
        {
            return _semaphore.GetHashCode();
        }

        public static bool operator ==(SemaphoreLock left, SemaphoreLock right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SemaphoreLock left, SemaphoreLock right)
        {
            return !(left == right);
        }
    }
}
