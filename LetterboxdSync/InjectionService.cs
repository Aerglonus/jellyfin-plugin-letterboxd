using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LetterboxdSync;

/// <summary>
/// A hosted service that automatically injects the Letterboxd menu script
/// into the Jellyfin web client's index.html at startup.
/// This completely eliminates the need for users to manually configure
/// the third-party JavaScript Injector plugin.
/// </summary>
public class InjectionService : IHostedService
{
    private readonly ILogger<InjectionService> _logger;
    private readonly IApplicationPaths _appPaths;

    public InjectionService(ILogger<InjectionService> logger, IApplicationPaths appPaths)
    {
        _logger = logger;
        _appPaths = appPaths;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        InjectScript();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void InjectScript()
    {
        try
        {
            var indexPath = Path.Combine(_appPaths.WebPath, "index.html");
            if (!File.Exists(indexPath))
            {
                _logger.LogWarning("[LetterboxdSync] Could not find index.html at path: {Path}. Menu script will not be auto-injected.", indexPath);
                return;
            }

            var content = File.ReadAllText(indexPath);

            var startComment = "<!-- BEGIN LetterboxdSync Menu Injector -->";
            var endComment = "<!-- END LetterboxdSync Menu Injector -->";
            var scriptUrl = "../Jellyfin.Plugin.LetterboxdSync/MenuScript";
            var injectionBlock = $"{startComment}\n<script src=\"{scriptUrl}\"></script>\n{endComment}";

            if (content.Contains(startComment))
            {
                var start = content.IndexOf(startComment, StringComparison.Ordinal);
                var end = content.IndexOf(endComment, start, StringComparison.Ordinal);
                if (end >= 0)
                {
                    end += endComment.Length;
                    content = content.Remove(start, end - start).Insert(start, injectionBlock);
                    File.WriteAllText(indexPath, content);
                    _logger.LogInformation("[LetterboxdSync] Refreshed menu script injection in index.html.");
                    return;
                }
            }

            var closingBodyTag = "</body>";
            if (content.Contains(closingBodyTag))
            {
                content = content.Replace(closingBodyTag, $"{injectionBlock}\n{closingBodyTag}");
                File.WriteAllText(indexPath, content);
                _logger.LogInformation("[LetterboxdSync] Successfully self-injected menu script into index.html.");
            }
            else
            {
                _logger.LogWarning("[LetterboxdSync] Could not find </body> tag in index.html. Scripts not injected.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LetterboxdSync] Error while trying to inject script block into index.html.");
        }
    }
}
