namespace YouTubeCommentsFetcher.Web.Models;

public class ErrorViewModel
{
    public string? RequestId { get; init; }

    public bool ShowRequestId => string.IsNullOrEmpty(RequestId) == false;
}
