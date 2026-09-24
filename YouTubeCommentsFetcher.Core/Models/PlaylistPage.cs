namespace YouTubeCommentsFetcher.Core.Models;

public record PlaylistPage(List<PlaylistVideo> Videos, string? NextPageToken, int? TotalResults);
