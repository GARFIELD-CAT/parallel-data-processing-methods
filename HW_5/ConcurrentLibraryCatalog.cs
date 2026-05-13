using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

public class Book
{
    public string Title { get; set; }
    public string Author { get; set; }

    public Book(string title, string author)
    {
        Title = title;
        Author = author;
    }
}


public class ConcurrentLibraryCatalog
{
    public ConcurrentDictionary<string, Book> Books = new ConcurrentDictionary<string, Book>(StringComparer.OrdinalIgnoreCase);

    public bool AddBook(string title, string author)
    {
        Book book = new Book(title, author);

        return Books.TryAdd(title, book);
    }

    public bool RemoveBook(string title)
    {
        return Books.TryRemove(title, out _);
    }

    public bool UpdateBook(string title, string newTitle, string newAuthor)
    {
        if (!Books.TryGetValue(title, out var existing)) return false;

        var updated = new Book(newTitle, newAuthor);

        return Books.TryUpdate(title, updated, existing);
    }

    public IEnumerable<Book> SearchBooks(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Enumerable.Empty<Book>();

        keyword = keyword.Trim();

        return Books.Values.Where(
            book => book.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || book.Author.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        );
    }

    public IEnumerable<Book> GetAllBooks()
    {
        return Books.Values.ToArray();
    }

    public int GetBookCount()
    {
        return Books.Count;
    }


    public void ClearCatalog()
    {
        Books.Clear();
    }

    public Book TryGetBook(string title)
    {
        if (Books.TryGetValue(title, out Book book))
            return book;

        return null;
    }
}
