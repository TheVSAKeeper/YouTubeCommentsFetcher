using YouTubeCommentsFetcher.Web.Models;

namespace YouTubeCommentsFetcher.Web;

public static class Analyzer
{
    private const int Count = 3;

    public static CommentStatistics Analyze(List<Comment> comments, List<VideoComments> videos)
    {
        CommentStatistics statistics = new()
        {
            TotalComments = comments.Count,
            UniqueAuthors = comments.Select(c => c.AuthorDisplayName).Distinct().Count(),
            AverageCommentsPerVideo = videos.Count > 0
                ? Math.Round((double)comments.Count / videos.Count, 2)
                : 0,
            OldestCommentDate = comments.Min(c => c.PublishedAt),
            NewestCommentDate = comments.Max(c => c.PublishedAt),
            CommentAnalysis = AnalyzeComments(comments),
        };

        var videoAnalysis = AnalyzeVideos(videos);

        statistics.TopCommentedVideos = videoAnalysis.TopCommentedVideos;
        statistics.TopLikedCommentsVideos = videoAnalysis.TopLikedCommentsVideos;
        statistics.TopRepliedVideos = videoAnalysis.TopRepliedVideos;
        statistics.TopInteractiveVideos = videoAnalysis.TopInteractiveVideos;
        statistics.TotalReplies = videos.Sum(v => v.Comments.Sum(c => c.Replies.Count));

        return statistics;
    }

    private static VideoAnalysisResult AnalyzeVideos(List<VideoComments> videos)
    {
        VideoAnalysisResult analysis = new()
        {
            TopCommentedVideos = videos
                .Where(x => x.Comments.Count > 0)
                .Select(v => new TopVideo
                {
                    VideoTitle = v.VideoTitle,
                    VideoId = v.VideoId,
                    CommentsCount = v.Comments.Count,
                    ThumbnailUrl = v.ThumbnailUrl,
                })
                .OrderByDescending(v => v.CommentsCount)
                .Take(Count)
                .ToList(),
            TopLikedCommentsVideos = videos
                .Where(x => x.Comments.Count > 0)
                .Select(v => new TopVideo
                {
                    VideoTitle = v.VideoTitle,
                    VideoId = v.VideoId,
                    CommentsCount = v.Comments.Sum(c => c.LikeCount),
                    ThumbnailUrl = v.ThumbnailUrl,
                })
                .OrderByDescending(v => v.CommentsCount)
                .Take(Count)
                .ToList(),
            TopRepliedVideos = videos
                .Where(x => x.Comments.Count > 0)
                .Select(v => new TopVideo
                {
                    VideoTitle = v.VideoTitle,
                    VideoId = v.VideoId,
                    CommentsCount = v.Comments.Sum(c => c.Replies.Count),
                    ThumbnailUrl = v.ThumbnailUrl,
                })
                .OrderByDescending(v => v.CommentsCount)
                .Take(Count)
                .ToList(),
            TopInteractiveVideos = videos
                .Where(x => x.Comments.Count > 0)
                .Select(v => new TopVideo
                {
                    VideoTitle = v.VideoTitle,
                    VideoId = v.VideoId,
                    CommentsCount = v.Comments.Count,
                    RepliesCount = v.Comments.Sum(c => c.Replies.Count),
                    TotalInteractions = v.Comments.Count + v.Comments.Sum(c => c.Replies.Count),
                    ThumbnailUrl = v.ThumbnailUrl,
                })
                .OrderByDescending(v => v.TotalInteractions)
                .Take(Count)
                .ToList(),
        };

        return analysis;
    }

    private static CommentAnalysisResult AnalyzeComments(List<Comment> comments)
    {
        Top<TopWord> worldTop = new()
        {
            ByComments = TopWords(comments),
            ByReplies = TopWords(comments.SelectMany(x => x.Replies)),
            ByActivity = TopWords(comments.SelectMany(x => x.Replies.Append(x))),
        };

        Top<TopAuthor> authorTop = new()
        {
            ByComments = TopAuthors(comments),
            ByReplies = TopAuthors(comments.SelectMany(x => x.Replies)),
            ByActivity = TopAuthors(comments.SelectMany(x => x.Replies.Append(x))),
        };

        CommentAnalysisResult analysis = new()
        {
            TopAuthors = authorTop,
            TopCommentsByReplies = comments
                .Where(c => c.Replies.Count > 0)
                .OrderByDescending(c => c.Replies.Count)
                .Take(Count)
                .Select(c => new TopComment
                {
                    CommentText = c.TextDisplay,
                    Count = c.Replies.Count,
                    Author = c.AuthorDisplayName,
                })
                .ToList(),
            TopCommentsByLikes = comments
                .Where(c => c.LikeCount is > 0)
                .OrderByDescending(c => c.LikeCount)
                .Take(Count)
                .Select(c => new TopComment
                {
                    CommentText = c.TextDisplay,
                    Count = c.LikeCount,
                    Author = c.AuthorDisplayName,
                })
                .ToList(),
            MostUsedWords = worldTop,
        };

        return analysis;

        List<TopWord> TopWords(IEnumerable<Comment> all)
        {
            var topWords = all
                .SelectMany(c => c.TextDisplay.Split([" ", "<br>"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(x => x.Trim(',', '.', ';', '-', '!', '?', '(', ')'))
                .Where(word => word.Length > 3 && !word.Contains("href", StringComparison.InvariantCultureIgnoreCase))
                .GroupBy(word => word.ToLowerInvariant())
                .Select(g => new TopWord(g.Key, g.Count()))
                .OrderByDescending(g => g.Count)
                .Take(15)
                .ToList();

            return topWords;
        }

        List<TopAuthor> TopAuthors(IEnumerable<Comment> all)
        {
            var topAuthors = all
                .GroupBy(c => c.AuthorDisplayName)
                .Select(g => new TopAuthor
                {
                    AuthorName = g.First().AuthorDisplayName,
                    CommentsCount = g.Count(),
                })
                .OrderByDescending(a => a.CommentsCount)
                .Take(Count)
                .ToList();

            return topAuthors;
        }
    }
}
