using System.Collections.Concurrent;

namespace LibrarySystem;


public class Book
{
    public string Title { get; set; }
    public string Author { get; set; }

    public Book(string title, string author)
    {
        Title = title;
        Author = author;
    }

    public override string ToString() => $"\"{Title}\" by {Author}";
}

public class ConcurrentLibraryCatalog
{
    private readonly ConcurrentDictionary<string, Book> _books = new();

    public bool AddBook(string title, string author)
    {
        return _books.TryAdd(title, new Book(title, author));
    }

    public bool RemoveBook(string title)
    {
        return _books.TryRemove(title, out _);
    }

    public bool UpdateBook(string title, string newTitle, string newAuthor)
    {
        if (!_books.TryGetValue(title, out Book oldBook))
            return false;

        var newBook = new Book(newTitle, newAuthor);

        return _books.TryUpdate(title, newBook, oldBook);
    }

    public Book[] SearchBooks(string keyword)
    {
        var results = new List<Book>();

        foreach (var book in _books.Values)
        {
            if (book.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                book.Author.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(book);
            }
        }
        return results.ToArray();
    }

    public Book[] GetAllBooks()
    {
        return _books.Values.ToArray();
    }

    public int GetBookCount()
    {
        return _books.Count;
    }

    public void ClearCatalog()
    {
        _books.Clear();
    }

    public bool TryGetBook(string title, out Book? book)
    {
        return _books.TryGetValue(title, out book);
    }
}