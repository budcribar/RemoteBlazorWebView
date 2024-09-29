using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

public class ConcurrentList<T> : IEnumerable<T>, IDisposable
{
    private List<T> _list = new List<T>();
    private ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;

    public int Add(T item)
    {
        CheckDisposed();
        _lock.EnterWriteLock();
        try
        {
            _list.Add(item);
            return _list.Count - 1;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public T this[int index]
    {
        get
        {
            CheckDisposed();
            _lock.EnterReadLock();
            try
            {
                return _list[index];
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
        set
        {
            CheckDisposed();
            _lock.EnterWriteLock();
            try
            {
                _list[index] = value;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public int Count
    {
        get
        {
            CheckDisposed();
            _lock.EnterReadLock();
            try
            {
                return _list.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    // Add RemoveAt method
    public void RemoveAt(int index)
    {
        CheckDisposed();
        _lock.EnterWriteLock();
        try
        {
            _list.RemoveAt(index);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void CheckDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ConcurrentList<T>));
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _lock.EnterWriteLock();
                try
                {
                    _list.Clear();
                }
                finally
                {
                    _lock.ExitWriteLock();
                    _lock.Dispose();
                }
            }
            _disposed = true;
        }
    }

    // Implement IEnumerable<T>.GetEnumerator (Generic)
    public IEnumerator<T> GetEnumerator()
    {
        _lock.EnterReadLock();
        try
        {
            // Return a copy of the list to ensure thread safety
            return ((IEnumerable<T>)_list.ToArray()).GetEnumerator();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    // Explicit implementation of IEnumerable (Non-generic)
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();  // Call the generic GetEnumerator
    }
}
