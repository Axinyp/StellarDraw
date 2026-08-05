using System.Collections.ObjectModel;
using WhiteBoard.Core.Domain;

namespace WhiteBoard.UI.ViewModels;

/// <summary>
/// 页面列表 ViewModel：展示由 <see cref="WhiteboardSession"/> 在执行器内捕获的页面元数据快照。
/// 不持有 <c>PageManager</c>，因此不会从 UI 线程修改页面集合。
/// </summary>
public sealed class PageListViewModel : ViewModelBase, IDisposable
{
    private PageInfo? _currentPage;

    public ObservableCollection<PageInfo> Pages { get; } = [];

    public PageInfo? CurrentPage
    {
        get => _currentPage;
        private set => Set(ref _currentPage, value);
    }

    internal void Apply(PageCollectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Pages.Clear();
        foreach (var page in snapshot.Pages)
            Pages.Add(page);

        CurrentPage = Pages.FirstOrDefault(page => page.Id == snapshot.CurrentPageId);
    }

    public void Dispose()
    { }
}
