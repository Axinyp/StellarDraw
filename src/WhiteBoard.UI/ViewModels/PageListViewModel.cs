using System.Collections.ObjectModel;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Services;

namespace WhiteBoard.UI.ViewModels;

/// <summary>
/// 页面列表 ViewModel：将 <see cref="PageManager"/> 的页面列表桥接为 <see cref="ObservableCollection{T}"/>，
/// 供 <see cref="Controls.PageStrip"/> 绑定展示。
/// 页面切换（GoToPage）同步更新 <see cref="CurrentPage"/> 触发 PropertyChanged。
/// </summary>
public sealed class PageListViewModel : ViewModelBase, IDisposable
{
    private readonly PageManager _manager;
    private PageModel?           _current;

    public ObservableCollection<PageModel> Pages   { get; } = [];
    public PageModel? CurrentPage
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    public PageListViewModel(PageManager manager)
    {
        _manager = manager;
        Refresh();
    }

    public void Refresh()
    {
        Pages.Clear();
        foreach (var p in _manager.AllPages)
            Pages.Add(p);
        CurrentPage = _manager.AllPages.Count > 0 ? _manager.Current : null;
    }

    public bool GoToPage(string pageId)
    {
        if (!_manager.GoToPage(pageId)) return false;
        CurrentPage = _manager.Current;
        return true;
    }

    public PageModel AddPage(string? title = null)
    {
        var page = _manager.AddPage(title);
        Pages.Add(page);
        _manager.GoToPage(page.Id);
        CurrentPage = page;
        return page;
    }

    public bool RemovePage(string pageId)
    {
        var page = Pages.FirstOrDefault(p => p.Id == pageId);
        if (page is null) return false;
        if (!_manager.RemovePage(pageId)) return false;
        Pages.Remove(page);
        CurrentPage = _manager.AllPages.Count > 0 ? _manager.Current : null;
        return true;
    }

    public void Dispose() { }
}
