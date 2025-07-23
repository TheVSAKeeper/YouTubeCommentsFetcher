using YouTubeCommentsFetcher.Web.Models;

namespace YouTubeCommentsFetcher.Web.Services;

public static class Analyzer
{
    private const int Count = 3;

    public static async Task<CommentStatistics> AnalyzeAsync(List<Comment> comments, List<VideoComments> videos, CancellationToken cancellationToken = default)
    {
        if (comments.Count > 1000)
        {
            await Task.Yield();
        }

        var allCommentsWithReplies = comments.SelectMany(c => c.Replies.Append(c)).ToList();
        var allReplies = comments.SelectMany(c => c.Replies).ToList();

        cancellationToken.ThrowIfCancellationRequested();

        CommentStatistics statistics = new()
        {
            TotalComments = comments.Count,
            UniqueAuthors = comments.Select(c => c.AuthorDisplayName).Distinct().Count(),
            AverageCommentsPerVideo = videos.Count > 0
                ? Math.Round((double)comments.Count / videos.Count, 2)
                : 0,
            OldestCommentDate = comments.Count > 0 ? comments.Min(c => c.PublishedAt) : null,
            NewestCommentDate = comments.Count > 0 ? comments.Max(c => c.PublishedAt) : null,
            CommentAnalysis = await AnalyzeCommentsAsync(comments, allReplies, allCommentsWithReplies, cancellationToken),
        };

        cancellationToken.ThrowIfCancellationRequested();

        var videoAnalysis = await AnalyzeVideosAsync(videos, cancellationToken);

        statistics.TopCommentedVideos = videoAnalysis.TopCommentedVideos;
        statistics.TopLikedCommentsVideos = videoAnalysis.TopLikedCommentsVideos;
        statistics.TopRepliedVideos = videoAnalysis.TopRepliedVideos;
        statistics.TopInteractiveVideos = videoAnalysis.TopInteractiveVideos;
        statistics.TotalReplies = videos.Sum(v => v.Comments.Sum(c => c.Replies.Count));

        return statistics;
    }

    private static async Task<VideoAnalysisResult> AnalyzeVideosAsync(List<VideoComments> videos, CancellationToken cancellationToken = default)
    {
        if (videos.Count > 100)
        {
            await Task.Yield();
        }

        cancellationToken.ThrowIfCancellationRequested();

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

    private static async Task<CommentAnalysisResult> AnalyzeCommentsAsync(List<Comment> comments, List<Comment> allReplies, List<Comment> allCommentsWithReplies, CancellationToken cancellationToken = default)
    {
        if (comments.Count > 1000)
        {
            await Task.Yield();
        }

        cancellationToken.ThrowIfCancellationRequested();

        Top<TopWord> worldTop = new()
        {
            ByComments = await TopWordsAsync(comments, cancellationToken),
            ByReplies = await TopWordsAsync(allReplies, cancellationToken),
            ByActivity = await TopWordsAsync(allCommentsWithReplies, cancellationToken),
        };

        cancellationToken.ThrowIfCancellationRequested();

        Top<TopAuthor> authorTop = new()
        {
            ByComments = TopAuthors(comments),
            ByReplies = TopAuthors(allReplies),
            ByActivity = TopAuthors(allCommentsWithReplies),
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
    }

    private static async Task<List<TopWord>> TopWordsAsync(IEnumerable<Comment> all, CancellationToken cancellationToken)
    {
        var comments = all.ToList();

        const int chunkSize = 500;
        var wordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < comments.Count; i += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = comments.Skip(i).Take(chunkSize);

            var words = chunk
                .SelectMany(c => c.TextDisplay.Split([" ", "<br>"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(x => x.Trim(',', '.', ';', '-', '!', '?', '(', ')'))
                .Where(word => word.Length > 3 && !word.Contains("href", StringComparison.InvariantCultureIgnoreCase))
                .Select(word => word.ToLowerInvariant());

            foreach (var word in words)
            {
                wordCounts[word] = wordCounts.GetValueOrDefault(word, 0) + 1;
            }

            if (i % (chunkSize * 2) == 0)
            {
                await Task.Yield();
            }
        }

        var topWords = wordCounts
            .Select(kvp => new TopWord { Word = kvp.Key, Count = kvp.Value })
            .OrderByDescending(w => w.Count)
            .Take(15)
            .ToList();

        return topWords;
    }

    private static List<TopAuthor> TopAuthors(IEnumerable<Comment> all)
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
