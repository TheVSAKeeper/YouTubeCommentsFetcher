using Quartz;
using System.Diagnostics;
using System.Text.Json;
using YouTubeCommentsFetcher.Web.Models;

namespace YouTubeCommentsFetcher.Web.Services;

public class AnalyzeDataJob(IJobStatusService statusService, ILogger<AnalyzeDataJob> logger) : IJob
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task Execute(IJobExecutionContext context)
    {
        var jobId = context.JobDetail.JobDataMap.GetString("jobId")!;
        var filePath = context.JobDetail.JobDataMap.GetString("filePath")!;

        logger.LogInformation("Starting background analysis for job {JobId}, file: {FilePath}", jobId, filePath);

        try
        {
            statusService.ReportProgress(jobId, 10);

            if (!File.Exists(filePath))
            {
                logger.LogError("File not found: {FilePath}", filePath);
                statusService.ReportError(jobId, "Uploaded file not found");
                return;
            }

            logger.LogInformation("Reading file: {FilePath}", filePath);
            var json = await File.ReadAllTextAsync(filePath);
            statusService.ReportProgress(jobId, 20);

            logger.LogInformation("Deserializing JSON data");
            var model = JsonSerializer.Deserialize<YouTubeCommentsViewModel>(json);

            if (model == null)
            {
                logger.LogError("Failed to deserialize JSON data");
                statusService.ReportError(jobId, "Invalid JSON format");
                return;
            }

            statusService.ReportProgress(jobId, 30);

            logger.LogInformation("Starting analysis of {CommentsCount} comments from {VideosCount} videos",
                model.Comments.Count, model.Videos.Count);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            var stopwatch = Stopwatch.StartNew();

            statusService.ReportProgress(jobId, 40);
            model.Statistics = await Analyzer.AnalyzeAsync(model.Comments, model.Videos, cts.Token);

            stopwatch.Stop();

            logger.LogInformation("Analysis completed in {ElapsedMs} ms for {CommentsCount} comments",
                stopwatch.ElapsedMilliseconds, model.Comments.Count);

            statusService.ReportProgress(jobId, 80);

            var outputPath = Path.Combine("Data", $"analyzed_{jobId}.json");
            Directory.CreateDirectory("Data");

            var analyzedJson = JsonSerializer.Serialize(model, JsonSerializerOptions);
            await File.WriteAllTextAsync(outputPath, analyzedJson, cts.Token);

            statusService.ReportProgress(jobId, 90);

            try
            {
                File.Delete(filePath);
                logger.LogInformation("Cleaned up temporary file: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete temporary file: {FilePath}", filePath);
            }

            statusService.ReportProgress(jobId, 100);
            statusService.MarkCompleted(jobId);

            logger.LogInformation("Background analysis completed successfully for job {JobId}, output saved to {OutputPath}",
                jobId, outputPath);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Analysis job {JobId} was cancelled due to timeout", jobId);
            statusService.ReportError(jobId, "Analysis timed out. The file may be too large to process.");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "JSON deserialization error for job {JobId}", jobId);
            statusService.ReportError(jobId, "Invalid JSON format in uploaded file");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during analysis for job {JobId}", jobId);
            statusService.ReportError(jobId, "An unexpected error occurred during analysis");
        }
        finally
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up temporary file on error: {FilePath}", filePath);
            }
        }
    }
}
