using WhiteBoard.Core.Domain;

namespace WhiteBoard.Core.Services;

/// <summary>
/// 白板页面管理器：维护页面有序列表和当前激活页索引。
/// <para>
/// 不持有 Session 引用，无线程安全保证；所有调用必须来自 Session 执行器线程。
/// </para>
/// </summary>
public sealed class PageManager
{
    private readonly List<PageModel> _pages = [];
    private int _currentIndex;

    public PageModel Current => _pages.Count > 0 ? _pages[_currentIndex]
        : throw new InvalidOperationException("No pages in document.");

    public IReadOnlyList<PageModel> AllPages => _pages;

    public int CurrentIndex => _currentIndex;

    public PageModel AddPage(string? title = null, string background = "white")
    {
        var page = new PageModel(title: title ?? $"Page {_pages.Count + 1}", background: background);
        _pages.Add(page);
        return page;
    }

    public bool RemovePage(string pageId)
    {
        var idx = _pages.FindIndex(p => p.Id == pageId);
        if (idx < 0) return false;
        _pages.RemoveAt(idx);
        if (_currentIndex >= _pages.Count)
            _currentIndex = Math.Max(0, _pages.Count - 1);
        return true;
    }

    public bool GoToPage(string pageId)
    {
        var idx = _pages.FindIndex(p => p.Id == pageId);
        if (idx < 0) return false;
        _currentIndex = idx;
        return true;
    }

    public void GoToIndex(int index)
    {
        if (index < 0 || index >= _pages.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _currentIndex = index;
    }

    public PageModel? GetPage(string pageId) => _pages.Find(p => p.Id == pageId);

    public void EnsureAtLeastOnePage()
    {
        if (_pages.Count == 0) AddPage();
    }

    internal void ReplaceAll(IReadOnlyList<PageModel> pages, int currentIndex)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
            throw new ArgumentException("文档至少需要包含一个页面。", nameof(pages));
        if (currentIndex < 0 || currentIndex >= pages.Count)
            throw new ArgumentOutOfRangeException(nameof(currentIndex));

        _pages.Clear();
        _pages.AddRange(pages);
        _currentIndex = currentIndex;
    }
}