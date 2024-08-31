using YouTubeCommentsFetcher.Web.Models;

namespace YouTubeCommentsFetcher.Web.Services;

public interface IYouTubeService
{
    Task<string?> GetUploadsPlaylistIdAsync(string channelId, CancellationToken cancellationToken = default);
    Task<List<string>> GetVideoIdsFromPlaylistAsync(string uploadsPlaylistId, int pageSize, int maxPages, CancellationToken cancellationToken = default);
    Task<VideoComments> GetVideoCommentsAsync(string videoId, CancellationToken cancellationToken = default);
}
