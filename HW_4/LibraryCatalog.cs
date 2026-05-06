using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

public class LibraryCatalog
{
    private readonly List<(string Title, string Author)> _books = new();
    private readonly ReaderWriterLockSlim _rw = new(LockRecursionPolicy.NoRecursion);

    // Add / Remove / Update (write) — используем EnterWriteLock / ExitWriteLock
    public void AddBook(string title, string author)
    {
        _rw.EnterWriteLock();
        try
        {
            _books.Add((title, author));
        }
        finally
        {
            _rw.ExitWriteLock();
        }
    }

    public void RemoveBook(string title)
    {
        _rw.EnterWriteLock();
        try
        {
            _books.RemoveAll(b => b.Title == title);
        }
        finally
        {
            _rw.ExitWriteLock();
        }
    }

    public void UpdateBook(string title, string newTitle, string newAuthor)
    {
        _rw.EnterWriteLock();
        try
        {
            for (int i = 0; i < _books.Count; i++)
            {
                if (_books[i].Title == title)
                    _books[i] = (newTitle, newAuthor);
            }
        }
        finally
        {
            _rw.ExitWriteLock();
        }
    }

    // Read operations — EnterReadLock / ExitReadLock
    public List<(string Title, string Author)> SearchBooks(string keyword)
    {
        _rw.EnterReadLock();
        try
        {
            var k = keyword?.ToLowerInvariant() ?? string.Empty;
            return _books.Where(b => b.Title.ToLowerInvariant().Contains(k) || b.Author.ToLowerInvariant().Contains(k)).ToList();
        }
        finally
        {
            _rw.ExitReadLock();
        }
    }

    public List<(string Title, string Author)> GetAllBooks()
    {
        _rw.EnterReadLock();
        try
        {
            return new List<(string Title, string Author)>(_books);
        }
        finally
        {
            _rw.ExitReadLock();
        }
    }

    // Таймаутные версии (TryEnterReadLock/TryEnterWriteLock)
    public bool TryAddBook(string title, string author, int timeoutMs)
    {
        if (_rw.TryEnterWriteLock(timeoutMs))
        {
            try
            {
                _books.Add((title, author));
                return true;
            }
            finally
            {
                _rw.ExitWriteLock();
            }
        }
        return false;
    }

    public (bool success, List<(string Title, string Author)> results) TrySearchBooks(string keyword, int timeoutMs)
    {
        if (_rw.TryEnterReadLock(timeoutMs))
        {
            try
            {
                var k = keyword?.ToLowerInvariant() ?? string.Empty;
                var res = _books.Where(b => b.Title.ToLowerInvariant().Contains(k) || b.Author.ToLowerInvariant().Contains(k)).ToList();
                return (true, res);
            }
            finally
            {
                _rw.ExitReadLock();
            }
        }
        return (false, new List<(string, string)>());
    }

    // Освобождаем ресурсы
    public void Dispose() => _rw.Dispose();
}
